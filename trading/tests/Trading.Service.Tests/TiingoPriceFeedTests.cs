using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Trading.Service.Candles;
using Trading.Service.Configuration;
using Trading.Service.Feeds;

namespace Trading.Service.Tests;

public sealed class TiingoPriceFeedTests
{
    private const string EurUsdQuote =
        """{"messageType":"A","service":"fx","data":["Q","eurusd","2026-10-05T08:00:00.123456+00:00",1000000.0,1.08123,1.08127,1000000.0,1.08131]}""";

    private const string GoldQuote =
        """{"messageType":"A","service":"fx","data":["Q","xauusd","2026-10-05T08:00:00+00:00",100.0,2650.127,2650.234,100.0,2650.341]}""";

    private const string Subscribed = """{"messageType":"I","data":{"subscriptionId":1},"response":{"code":200,"message":"Success"}}""";

    private const string Heartbeat = """{"messageType":"H","response":{"code":200,"message":"HeartBeat"}}""";

    private const string Refused = """{"messageType":"E","response":{"code":401,"message":"Not authorized"}}""";

    private const string GoldTopOfBook =
        """[{"ticker":"xauusd","quoteTimestamp":"2026-10-02T20:59:58.123+00:00","bidPrice":2650.127,"bidSize":100.0,"askPrice":2650.341,"askSize":100.0,"midPrice":2650.234}]""";

    [Fact]
    public void QuoteIsMappedToTheConfiguredSymbol()
    {
        var quote = CreateFeed().Parse(EurUsdQuote);

        Assert.Equal(("EURUSD", 1.08123m, 1.08131m), (quote!.Symbol, quote.Bid, quote.Ask));
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 8, 0, 0, 123, TimeSpan.Zero), quote.Timestamp, TimeSpan.FromMilliseconds(1));
    }

    // Gold has 3 decimals at Tiingo and 2 here. Rounding outwards never makes the spread smaller.
    [Fact]
    public void PricesAreRoundedOutwardsToTheInstrumentDigits()
    {
        var quote = CreateFeed().Parse(GoldQuote);

        Assert.Equal((2650.12m, 2650.35m), (quote!.Bid, quote.Ask));
    }

    [Theory]
    [InlineData(Subscribed)]
    [InlineData(Heartbeat)]
    [InlineData("""{"messageType":"A","service":"fx","data":["Q","audusd","2026-10-05T08:00:00+00:00",1.0,0.65,0.651,1.0,0.652]}""")]
    [InlineData("""{"messageType":"A","service":"fx","data":["Q","eurusd","2026-10-05T08:00:00+00:00",1.0,null,1.1,1.0,1.2]}""")]
    [InlineData("""{"messageType":"A","service":"fx","data":["Q","eurusd","2026-10-05T08:00:00+00:00",1.0,1.2,1.15,1.0,1.1]}""")]
    [InlineData("""{"messageType":"A","service":"fx","data":["T","eurusd"]}""")]
    public void ConfirmationsHeartbeatsUnknownTickersAndIncompleteQuotesAreSkipped(string message)
    {
        Assert.Null(CreateFeed().Parse(message));
    }

    [Fact]
    public void ErrorFailsTheStream()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateFeed().Parse(Refused));

        Assert.Contains("401 Not authorized", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamSubscribesWithTheKeyAndStartsFromTheLatestPrices()
    {
        await using var tiingo = await FakeTiingo.StartAsync(FakeTiingo.SendAndStay(Subscribed, EurUsdQuote));
        tiingo.LatestPrices = GoldTopOfBook;

        var quotes = await CollectAsync(CreateFeed(tiingo), Symbols("EURUSD", "XAUUSD"));

        // Gold only comes from the latest prices, as the stream sent no change for it.
        var gold = quotes.Single(q => q.Symbol == "XAUUSD");
        Assert.Equal((2650.12m, 2650.35m), (gold.Bid, gold.Ask));

        using var subscription = JsonDocument.Parse(Assert.Single(tiingo.Subscriptions));
        var root = subscription.RootElement;
        Assert.Equal("subscribe", root.GetProperty("eventName").GetString());
        Assert.Equal("test-key", root.GetProperty("authorization").GetString());
        Assert.Equal(5, root.GetProperty("eventData").GetProperty("thresholdLevel").GetInt32());
        Assert.Equal(["eurusd", "xauusd"], root.GetProperty("eventData").GetProperty("tickers").EnumerateArray().Select(t => t.GetString()));

        var request = Assert.Single(tiingo.PriceRequests);
        Assert.Equal(("/tiingo/fx/top?tickers=eurusd,xauusd", "Token test-key"), request);
    }

    // Tiingo's forex API has no indices, commodities or crypto.
    [Fact]
    public async Task StreamAsksOnlyForForexAndMetals()
    {
        await using var tiingo = await FakeTiingo.StartAsync(FakeTiingo.SendAndStay(Subscribed, EurUsdQuote));
        var index = new InstrumentOptions { Symbol = "US100", Category = InstrumentCategory.Indices, BaseCurrency = "US100", QuoteCurrency = "USD", Digits = 1 };

        await CollectAsync(CreateFeed(tiingo, extraInstrument: index), Symbols("EURUSD"));

        using var subscription = JsonDocument.Parse(Assert.Single(tiingo.Subscriptions));
        Assert.Equal(["eurusd", "xauusd"], subscription.RootElement.GetProperty("eventData").GetProperty("tickers").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal("/tiingo/fx/top?tickers=eurusd,xauusd", Assert.Single(tiingo.PriceRequests).Url);
    }

    [Fact]
    public async Task StreamWorksWithoutTheLatestPrices()
    {
        await using var tiingo = await FakeTiingo.StartAsync(FakeTiingo.SendAndStay(Subscribed, EurUsdQuote));
        tiingo.LatestPricesStatus = StatusCodes.Status429TooManyRequests;

        var quotes = await CollectAsync(CreateFeed(tiingo), Symbols("EURUSD"));

        Assert.Equal("EURUSD", Assert.Single(quotes).Symbol);
    }

    [Fact]
    public async Task RefusedStreamIsRetried()
    {
        await using var tiingo = await FakeTiingo.StartAsync(
            FakeTiingo.SendAndClose(Refused),
            FakeTiingo.SendAndStay(Subscribed, EurUsdQuote));

        await CollectAsync(CreateFeed(tiingo), Symbols("EURUSD"));

        Assert.Equal(2, tiingo.Subscriptions.Count);
    }

    [Fact]
    public async Task ClosedStreamIsReconnected()
    {
        await using var tiingo = await FakeTiingo.StartAsync(
            FakeTiingo.SendAndClose(Subscribed, EurUsdQuote),
            FakeTiingo.SendAndStay(Subscribed, GoldQuote));

        await CollectAsync(CreateFeed(tiingo), Symbols("EURUSD", "XAUUSD"));

        Assert.Equal(2, tiingo.Subscriptions.Count);
    }

    [Fact]
    public async Task SilentStreamIsReconnected()
    {
        await using var tiingo = await FakeTiingo.StartAsync(
            FakeTiingo.SendAndStay(Subscribed),
            FakeTiingo.SendAndStay(Subscribed, EurUsdQuote));

        await CollectAsync(CreateFeed(tiingo, idleTimeout: TimeSpan.FromMilliseconds(300)), Symbols("EURUSD"));

        Assert.True(tiingo.Subscriptions.Count >= 2);
    }

    // Tiingo's bars are of mid prices. Bars that do not move are Tiingo repeating the last price while the market is closed.
    [Fact]
    public async Task HistoryIsMovedToTheBidAndLeavesOutUnmovedAndUnfinishedBars()
    {
        await using var tiingo = await FakeTiingo.StartAsync();
        tiingo.LatestPrices =
            """
            [{"ticker":"eurusd","quoteTimestamp":"2026-10-05T08:03:00+00:00","bidPrice":1.08000,"askPrice":1.08010},
             {"ticker":"xauusd","quoteTimestamp":"2026-10-05T08:03:00+00:00","bidPrice":2650.10,"askPrice":2650.30}]
            """;
        tiingo.History =
            """
            [{"date":"2026-10-04T23:59:00.000Z","ticker":"eurusd","open":1.08,"high":1.08,"low":1.08,"close":1.08},
             {"date":"2026-10-05T08:00:00.000Z","ticker":"eurusd","open":1.08004,"high":1.08012,"low":1.07998,"close":1.08006},
             {"date":"2026-10-05T08:01:00.000Z","ticker":"eurusd","open":1.08006,"high":1.08006,"low":1.08006,"close":1.08006},
             {"date":"2026-10-05T08:02:00.000Z","ticker":"eurusd","open":1.0801,"high":1.0801,"low":1.0801,"close":1.0801},
             {"date":"2026-10-05T08:03:00.000Z","ticker":"eurusd","open":1.0801,"high":1.0802,"low":1.0801,"close":1.0802},
             {"date":"2026-10-05T08:00:00.000Z","ticker":"xauusd","open":2650.125,"high":2650.5,"low":2650.0,"close":2650.25}]
            """;
        var today = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

        var bars = await CreateFeed(tiingo).GetHistoryAsync(
            [new HistorySpan(Timeframe.M1, today, today.AddHours(8).AddMinutes(3).AddSeconds(30))],
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new ChartBar("EURUSD", Timeframe.M1, new Candle(today.AddHours(8), 1.07999m, 1.08007m, 1.07993m, 1.08001m, 0)),
                new ChartBar("EURUSD", Timeframe.M1, new Candle(today.AddHours(8).AddMinutes(2), 1.08005m, 1.08005m, 1.08005m, 1.08005m, 0)),
                new ChartBar("XAUUSD", Timeframe.M1, new Candle(today.AddHours(8), 2650.02m, 2650.40m, 2649.90m, 2650.15m, 0)),
            ],
            bars);
        Assert.Equal(
            [("/tiingo/fx/prices?tickers=eurusd,xauusd&startDate=2026-10-05&endDate=2026-10-05&resampleFreq=1min", "Token test-key")],
            tiingo.HistoryRequests);
    }

    // Tiingo takes whole days and answers at most 10 000 bars a call. A day has 1 440 minute bars.
    [Fact]
    public async Task HistoryIsSplitIntoCallsThatFitTiingosLimit()
    {
        await using var tiingo = await FakeTiingo.StartAsync();
        var today = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

        await CreateFeed(tiingo).GetHistoryAsync(
            [new HistorySpan(Timeframe.M15, today.AddDays(-30), today.AddDays(-8)), new HistorySpan(Timeframe.M1, today.AddDays(-8), today.AddHours(8))],
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "/tiingo/fx/prices?tickers=eurusd,xauusd&startDate=2026-09-05&endDate=2026-09-26&resampleFreq=15min",
                "/tiingo/fx/prices?tickers=eurusd&startDate=2026-09-27&endDate=2026-10-02&resampleFreq=1min",
                "/tiingo/fx/prices?tickers=xauusd&startDate=2026-09-27&endDate=2026-10-02&resampleFreq=1min",
                "/tiingo/fx/prices?tickers=eurusd,xauusd&startDate=2026-10-03&endDate=2026-10-05&resampleFreq=1min",
            ],
            tiingo.HistoryRequests.Select(r => r.Url));
    }

    [Fact]
    public async Task RefusedHistoryFails()
    {
        await using var tiingo = await FakeTiingo.StartAsync();
        tiingo.HistoryStatus = StatusCodes.Status429TooManyRequests;
        var today = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateFeed(tiingo).GetHistoryAsync(
            [new HistorySpan(Timeframe.M1, today, today.AddHours(8))],
            TestContext.Current.CancellationToken));
    }

    // The key must never be put in a file, so a missing one stops the start with instructions.
    [Fact]
    public void TiingoWithoutKeyStopsTheStart()
    {
        using var factory = new Support.ServiceFactory(settings: new Dictionary<string, string> { ["PriceFeed:Provider"] = "Tiingo" });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("dotnet user-secrets", exception.ToString(), StringComparison.Ordinal);
    }

    private static Func<IReadOnlyCollection<FeedQuote>, bool> Symbols(params string[] symbols) =>
        quotes => quotes.Select(q => q.Symbol).ToHashSet().SetEquals(symbols);

    /// <summary>Reads quotes until <paramref name="done"/> holds, then stops the feed.</summary>
    private static async Task<List<FeedQuote>> CollectAsync(TiingoPriceFeed feed, Func<IReadOnlyCollection<FeedQuote>, bool> done)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var quotes = new List<FeedQuote>();
        await foreach (var quote in feed.StreamAsync(timeout.Token))
        {
            quotes.Add(quote);
            if (done(quotes))
            {
                await timeout.CancelAsync();
                break;
            }
        }

        return quotes;
    }

    private static TiingoPriceFeed CreateFeed(FakeTiingo? tiingo = null, TimeSpan? idleTimeout = null, InstrumentOptions? extraInstrument = null)
    {
        var options = new PriceFeedOptions
        {
            Provider = PriceFeedOptions.TiingoProvider,
            Tiingo = new TiingoOptions
            {
                ApiKey = "test-key",
                QuoteInterval = TimeSpan.FromMilliseconds(20),
                IdleTimeout = idleTimeout ?? TimeSpan.FromMinutes(2),
                ReconnectDelay = TimeSpan.FromMilliseconds(10),
                StreamUrl = tiingo?.StreamUrl ?? new Uri("ws://127.0.0.1:9/fx"),
                ApiUrl = tiingo?.ApiUrl ?? new Uri("http://127.0.0.1:9/"),
            },
        };
        var trading = new TradingOptions
        {
            Instruments =
            [
                new InstrumentOptions { Symbol = "EURUSD", Category = InstrumentCategory.Forex, BaseCurrency = "EUR", QuoteCurrency = "USD", Digits = 5 },
                new InstrumentOptions { Symbol = "XAUUSD", Category = InstrumentCategory.Metals, BaseCurrency = "XAU", QuoteCurrency = "USD", Digits = 2 },
                .. extraInstrument is null ? [] : new[] { extraInstrument },
            ],
        };
        return new TiingoPriceFeed(new NewClientFactory(), Options.Create(options), Options.Create(trading), NullLogger<TiingoPriceFeed>.Instance);
    }

    private sealed class NewClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    /// <summary>A stand-in for Tiingo. Plays one script per stream connection, then stays silent, and answers the latest prices.</summary>
    private sealed class FakeTiingo : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ConcurrentQueue<Func<WebSocket, CancellationToken, Task>> _connections;
        private readonly CancellationTokenSource _stopping = new();
        private readonly ConcurrentQueue<string> _subscriptions = new();
        private readonly ConcurrentQueue<(string Url, string? Authorization)> _priceRequests = new();
        private readonly ConcurrentQueue<(string Url, string? Authorization)> _historyRequests = new();

        private FakeTiingo(Func<WebSocket, CancellationToken, Task>[] connections)
        {
            _connections = new(connections);

            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            _app = builder.Build();
            _app.UseWebSockets();
            _app.Map("/fx", StreamAsync);
            _app.MapGet("/tiingo/fx/top", (HttpContext context) =>
            {
                _priceRequests.Enqueue((context.Request.Path + context.Request.QueryString, context.Request.Headers.Authorization.ToString()));
                return Results.Content(LatestPrices, "application/json", statusCode: LatestPricesStatus);
            });
            _app.MapGet("/tiingo/fx/prices", (HttpContext context) =>
            {
                _historyRequests.Enqueue((context.Request.Path + context.Request.QueryString, context.Request.Headers.Authorization.ToString()));
                return Results.Content(History, "application/json", statusCode: HistoryStatus);
            });
        }

        public string LatestPrices { get; set; } = "[]";

        public int LatestPricesStatus { get; set; } = StatusCodes.Status200OK;

        /// <summary>The answer to every history request.</summary>
        public string History { get; set; } = "[]";

        public int HistoryStatus { get; set; } = StatusCodes.Status200OK;

        public IReadOnlyList<(string Url, string? Authorization)> HistoryRequests => [.. _historyRequests];

        public Uri StreamUrl => new UriBuilder(ApiUrl) { Scheme = "ws", Path = "fx" }.Uri;

        public Uri ApiUrl => new(_app.Urls.First() + "/");

        public IReadOnlyList<string> Subscriptions => [.. _subscriptions];

        public IReadOnlyList<(string Url, string? Authorization)> PriceRequests => [.. _priceRequests];

        public static async Task<FakeTiingo> StartAsync(params Func<WebSocket, CancellationToken, Task>[] connections)
        {
            var tiingo = new FakeTiingo(connections);
            await tiingo._app.StartAsync(TestContext.Current.CancellationToken);
            return tiingo;
        }

        public static Func<WebSocket, CancellationToken, Task> SendAndClose(params string[] messages) => async (socket, cancellationToken) =>
        {
            await SendAsync(socket, messages, cancellationToken);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
        };

        public static Func<WebSocket, CancellationToken, Task> SendAndStay(params string[] messages) => async (socket, cancellationToken) =>
        {
            await SendAsync(socket, messages, cancellationToken);
            await WaitForCloseAsync(socket, cancellationToken);
        };

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            await _app.DisposeAsync();
            _stopping.Dispose();
        }

        private async Task StreamAsync(HttpContext context)
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            using var aborted = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping.Token);
            try
            {
                _subscriptions.Enqueue(await ReceiveAsync(socket, aborted.Token));
                var script = _connections.TryDequeue(out var next) ? next : SendAndStay();
                await script(socket, aborted.Token);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or IOException)
            {
                // The client went away.
            }
        }

        private static async Task SendAsync(WebSocket socket, string[] messages, CancellationToken cancellationToken)
        {
            foreach (var message in messages)
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
        }

        private static async Task<string> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            return Encoding.UTF8.GetString(buffer, 0, result.Count);
        }

        private static async Task WaitForCloseAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            while ((await socket.ReceiveAsync(buffer, cancellationToken)).MessageType != WebSocketMessageType.Close)
            {
            }
        }
    }
}

public sealed class LatestQuotesTests
{
    [Fact]
    public void OnlyTheNewestPricePerSymbolIsTakenOnce()
    {
        var latest = new LatestQuotes();
        var time = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

        latest.Set(new FeedQuote("GBPUSD", 1.26000m, 1.26003m, time));
        latest.Set(new FeedQuote("EURUSD", 1.08000m, 1.08002m, time));
        latest.Set(new FeedQuote("EURUSD", 1.08010m, 1.08012m, time));

        Assert.Equal([("EURUSD", 1.08010m), ("GBPUSD", 1.26000m)], latest.TakeAll().Select(q => (q.Symbol, q.Bid)));
        Assert.Empty(latest.TakeAll());
    }
}
