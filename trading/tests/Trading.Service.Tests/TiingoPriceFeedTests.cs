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

    private static TiingoPriceFeed CreateFeed(FakeTiingo? tiingo = null, TimeSpan? idleTimeout = null)
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
                new InstrumentOptions { Symbol = "EURUSD", BaseCurrency = "EUR", QuoteCurrency = "USD", Digits = 5 },
                new InstrumentOptions { Symbol = "XAUUSD", BaseCurrency = "XAU", QuoteCurrency = "USD", Digits = 2 },
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
        }

        public string LatestPrices { get; set; } = "[]";

        public int LatestPricesStatus { get; set; } = StatusCodes.Status200OK;

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
