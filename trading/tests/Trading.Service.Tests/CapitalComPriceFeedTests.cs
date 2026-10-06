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

public sealed class CapitalComPriceFeedTests
{
    private const string EurUsdQuote =
        """{"status":"OK","destination":"quote","payload":{"epic":"EURUSD","product":"CFD","bid":1.081234,"bidQty":1000000.0,"ofr":1.081316,"ofrQty":1000000.0,"timestamp":1759651200123}}""";

    private const string GoldQuote =
        """{"status":"OK","destination":"quote","payload":{"epic":"GOLD","product":"CFD","bid":2650.127,"bidQty":100.0,"ofr":2650.341,"ofrQty":100.0,"timestamp":1759651200000}}""";

    private const string Subscribed =
        """{"status":"OK","destination":"marketData.subscribe","correlationId":"1","payload":{"subscriptions":{"EURUSD":"PROCESSED","GOLD":"PROCESSED"}}}""";

    private const string SessionEnded =
        """{"status":"ERROR","destination":"marketData.subscribe","correlationId":"1","payload":{"errorCode":"error.invalid.session.token"}}""";

    private static readonly DateTimeOffset Today = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void QuoteIsMappedFromTheEpicAndRoundedOutwardsToTheInstrumentDigits()
    {
        var gold = CreateFeed().Parse(GoldQuote);

        Assert.Equal(("XAUUSD", 2650.12m, 2650.35m), (gold!.Symbol, gold.Bid, gold.Ask));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1759651200000), gold.Timestamp);
    }

    [Theory]
    [InlineData(Subscribed)]
    [InlineData("""{"status":"OK","destination":"ping","correlationId":"2","payload":{}}""")]
    [InlineData("""{"status":"OK","destination":"quote","payload":{"epic":"US500","bid":5800.1,"ofr":5800.6,"timestamp":1}}""")]
    [InlineData("""{"status":"OK","destination":"quote","payload":{"epic":"EURUSD","bid":null,"ofr":1.1,"timestamp":1}}""")]
    [InlineData("""{"status":"OK","destination":"quote","payload":{"epic":"EURUSD","bid":1.2,"ofr":1.1,"timestamp":1}}""")]
    public void ConfirmationsPingsUnknownInstrumentsAndIncompletePricesAreSkipped(string message)
    {
        Assert.Null(CreateFeed().Parse(message));
    }

    [Fact]
    public void ErrorFailsTheStream()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateFeed().Parse(SessionEnded));

        Assert.Contains("error.invalid.session.token", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MoreInstrumentsThanCapitalComStreamsStopTheStart()
    {
        var instruments = Enumerable.Range(0, CapitalComPriceFeed.MaxInstruments + 1)
            .Select(i => new InstrumentOptions { Symbol = $"SYM{i}", Category = InstrumentCategory.Indices, Digits = 1 })
            .ToList();

        Assert.Throws<InvalidOperationException>(() => CreateFeed(instruments: instruments));
    }

    [Fact]
    public async Task StreamLogsInSubscribesAndStartsFromTheLatestPrices()
    {
        await using var capital = await FakeCapitalCom.StartAsync(FakeCapitalCom.SendAndStay(Subscribed, EurUsdQuote));
        capital.Markets = """{"marketDetails":[{"instrument":{"epic":"GOLD"},"snapshot":{"marketStatus":"TRADEABLE","bid":2650.127,"offer":2650.341}}]}""";

        var quotes = await CollectAsync(CreateFeed(capital), Symbols("EURUSD", "XAUUSD"));

        // Gold only comes from the latest prices, as the stream sent no change for it.
        var gold = quotes.Single(q => q.Symbol == "XAUUSD");
        Assert.Equal((2650.12m, 2650.35m), (gold.Bid, gold.Ask));

        var login = Assert.Single(capital.Sessions);
        Assert.Equal("test-key", login.ApiKey);
        using (var body = JsonDocument.Parse(login.Body))
        {
            Assert.Equal("trader@example.com", body.RootElement.GetProperty("identifier").GetString());
            Assert.Equal("key-password", body.RootElement.GetProperty("password").GetString());
            Assert.False(body.RootElement.GetProperty("encryptedPassword").GetBoolean());
        }

        Assert.Equal(("/api/v1/markets?epics=EURUSD,GOLD", "test-key", "cst-1", "token-1"), Assert.Single(capital.Requests));

        using var subscription = JsonDocument.Parse(Assert.Single(capital.Subscriptions));
        var root = subscription.RootElement;
        Assert.Equal("marketData.subscribe", root.GetProperty("destination").GetString());
        Assert.Equal(("cst-1", "token-1"), (root.GetProperty("cst").GetString(), root.GetProperty("securityToken").GetString()));
        Assert.Equal(["EURUSD", "GOLD"], root.GetProperty("payload").GetProperty("epics").EnumerateArray().Select(e => e.GetString()));
    }

    // A session ends after 10 minutes without use.
    [Fact]
    public async Task StreamIsPingedWithTheSession()
    {
        await using var capital = await FakeCapitalCom.StartAsync(FakeCapitalCom.SendAndStay(Subscribed));
        var feed = CreateFeed(capital, pingInterval: TimeSpan.FromMilliseconds(50));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var streaming = Task.Run(
            async () =>
            {
                await foreach (var _ in feed.StreamAsync(timeout.Token))
                {
                }
            },
            TestContext.Current.CancellationToken);
        await Support.Eventually.ThatAsync(() => capital.Pings.Count >= 2, "the stream to be pinged");
        await timeout.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => streaming);

        using var ping = JsonDocument.Parse(capital.Pings[0]);
        Assert.Equal(("cst-1", "token-1"), (ping.RootElement.GetProperty("cst").GetString(), ping.RootElement.GetProperty("securityToken").GetString()));
    }

    [Fact]
    public async Task RefusedSessionIsRetried()
    {
        await using var capital = await FakeCapitalCom.StartAsync(FakeCapitalCom.SendAndStay(Subscribed, EurUsdQuote));
        capital.RefusedSessions = 1;

        await CollectAsync(CreateFeed(capital), Symbols("EURUSD"));

        Assert.Equal(2, capital.Sessions.Count);
    }

    [Fact]
    public async Task EndedSessionIsReconnected()
    {
        await using var capital = await FakeCapitalCom.StartAsync(FakeCapitalCom.SendAndStay(SessionEnded), FakeCapitalCom.SendAndStay(Subscribed, GoldQuote));

        await CollectAsync(CreateFeed(capital), Symbols("XAUUSD"));

        Assert.Equal(2, capital.Subscriptions.Count);
    }

    // Capital.com's history is in bid prices already. Bars that do not move are left out, like the live prices have no bars then.
    [Fact]
    public async Task HistoryIsBidBarsRoundedDownWithoutUnmovedAndUnfinishedBars()
    {
        await using var capital = await FakeCapitalCom.StartAsync();
        capital.History = (epic, _) => epic == "GOLD"
            ? """
              {"prices":[
                {"snapshotTimeUTC":"2026-10-05T07:58:00","openPrice":{"bid":2650.127,"ask":2650.4},"highPrice":{"bid":2651.009,"ask":2651.3},"lowPrice":{"bid":2649.5,"ask":2649.8},"closePrice":{"bid":2650.75,"ask":2651.0}},
                {"snapshotTimeUTC":"2026-10-05T07:59:00","openPrice":{"bid":2650.75,"ask":2651.0},"highPrice":{"bid":2650.75,"ask":2651.0},"lowPrice":{"bid":2650.75,"ask":2651.0},"closePrice":{"bid":2650.75,"ask":2651.0}},
                {"snapshotTimeUTC":"2026-10-05T08:00:00","openPrice":{"bid":2651.0,"ask":2651.3},"highPrice":{"bid":2651.5,"ask":2651.8},"lowPrice":{"bid":2651.0,"ask":2651.3},"closePrice":{"bid":2651.2,"ask":2651.5}}
              ]}
              """
            : null;

        var bars = await CreateFeed(capital).GetHistoryAsync(
            [new HistorySpan(Timeframe.M1, Today, Today.AddHours(8).AddSeconds(30))],
            TestContext.Current.CancellationToken);

        Assert.Equal([new ChartBar("XAUUSD", Timeframe.M1, new Candle(Today.AddHours(7).AddMinutes(58), 2650.12m, 2651.00m, 2649.50m, 2650.75m, 0))], bars);
        Assert.Equal(
            [
                ("/api/v1/prices/EURUSD?resolution=MINUTE&max=1000&from=2026-10-05T00:00:00&to=2026-10-05T08:00:30", "test-key", "cst-1", "token-1"),
                ("/api/v1/prices/GOLD?resolution=MINUTE&max=1000&from=2026-10-05T00:00:00&to=2026-10-05T08:00:30", "test-key", "cst-1", "token-1"),
            ],
            capital.Requests);
    }

    // Capital.com answers at most 1 000 bars a call and refuses a period that holds that many, so 950 bars are asked for at a time.
    [Fact]
    public async Task HistoryIsFetchedInPeriodsWithinCapitalComsLimit()
    {
        await using var capital = await FakeCapitalCom.StartAsync();

        await CreateFeed(capital, instruments: [Instrument("US100", InstrumentCategory.Indices, 1)]).GetHistoryAsync(
            [new HistorySpan(Timeframe.M15, Today.AddDays(-15), Today)],
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "/api/v1/prices/US100?resolution=MINUTE_15&max=1000&from=2026-09-20T00:00:00&to=2026-09-29T21:30:00",
                "/api/v1/prices/US100?resolution=MINUTE_15&max=1000&from=2026-09-29T21:30:00&to=2026-10-05T00:00:00",
            ],
            capital.Requests.Select(r => r.Url));
    }

    [Fact]
    public async Task RefusedSessionFailsTheHistory()
    {
        await using var capital = await FakeCapitalCom.StartAsync();
        capital.RefusedSessions = 1;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateFeed(capital).GetHistoryAsync(
            [new HistorySpan(Timeframe.M1, Today, Today.AddHours(8))],
            TestContext.Current.CancellationToken));

        Assert.Contains("error.invalid.details", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PriceFeed:CapitalCom:Identifier", exception.Message, StringComparison.Ordinal);
    }

    // The key and password must never be put in a file, so missing ones stop the start with instructions.
    [Fact]
    public void CapitalComWithoutKeyStopsTheStart()
    {
        using var factory = new Support.ServiceFactory(settings: new Dictionary<string, string> { ["PriceFeed:Provider"] = "CapitalCom" });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("dotnet user-secrets", exception.ToString(), StringComparison.Ordinal);
    }

    private static InstrumentOptions Instrument(string symbol, InstrumentCategory category, int digits) =>
        new() { Symbol = symbol, Category = category, Digits = digits };

    private static Func<IReadOnlyCollection<FeedQuote>, bool> Symbols(params string[] symbols) =>
        quotes => quotes.Select(q => q.Symbol).ToHashSet().SetEquals(symbols);

    /// <summary>Reads quotes until <paramref name="done"/> holds, then stops the feed.</summary>
    private static async Task<List<FeedQuote>> CollectAsync(CapitalComPriceFeed feed, Func<IReadOnlyCollection<FeedQuote>, bool> done)
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

    private static CapitalComPriceFeed CreateFeed(FakeCapitalCom? capital = null, TimeSpan? pingInterval = null, IReadOnlyList<InstrumentOptions>? instruments = null)
    {
        var options = new PriceFeedOptions
        {
            Provider = PriceFeedOptions.CapitalComProvider,
            CapitalCom = new CapitalComOptions
            {
                ApiKey = "test-key",
                Identifier = "trader@example.com",
                Password = "key-password",
                QuoteInterval = TimeSpan.FromMilliseconds(20),
                PingInterval = pingInterval ?? TimeSpan.FromMinutes(4),
                ReconnectDelay = TimeSpan.FromMilliseconds(10),
                HistoryCallInterval = TimeSpan.Zero,
                ApiUrl = capital?.ApiUrl ?? new Uri("http://127.0.0.1:9/"),
                StreamUrl = capital?.StreamUrl ?? new Uri("ws://127.0.0.1:9/connect"),
            },
        };
        var trading = new TradingOptions
        {
            Instruments = instruments ??
            [
                new InstrumentOptions { Symbol = "EURUSD", Category = InstrumentCategory.Forex, BaseCurrency = "EUR", QuoteCurrency = "USD", Digits = 5 },
                new InstrumentOptions { Symbol = "XAUUSD", Category = InstrumentCategory.Metals, BaseCurrency = "XAU", QuoteCurrency = "USD", Digits = 2 },
            ],
        };
        return new CapitalComPriceFeed(new NewClientFactory(), Options.Create(options), Options.Create(trading), NullLogger<CapitalComPriceFeed>.Instance);
    }

    private sealed class NewClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    /// <summary>
    /// A stand-in for Capital.com: sessions, latest prices and history over HTTP, and the stream over a WebSocket that
    /// plays one script per connection, answers pings and then stays open.
    /// </summary>
    private sealed class FakeCapitalCom : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ConcurrentQueue<Func<WebSocket, CancellationToken, Task>> _connections;
        private readonly CancellationTokenSource _stopping = new();
        private readonly ConcurrentQueue<(string ApiKey, string Body)> _sessions = new();
        private readonly ConcurrentQueue<(string Url, string ApiKey, string Cst, string Token)> _requests = new();
        private readonly ConcurrentQueue<string> _subscriptions = new();
        private readonly ConcurrentQueue<string> _pings = new();
        private int _sessionCount;

        private FakeCapitalCom(Func<WebSocket, CancellationToken, Task>[] connections)
        {
            _connections = new(connections);

            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            _app = builder.Build();
            _app.UseWebSockets();
            _app.Map("/connect", StreamAsync);
            _app.MapPost("/api/v1/session", async (HttpContext context) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                _sessions.Enqueue((context.Request.Headers["X-CAP-API-KEY"].ToString(), await reader.ReadToEndAsync()));
                var number = Interlocked.Increment(ref _sessionCount);
                if (number <= RefusedSessions)
                {
                    return Results.Content("""{"errorCode":"error.invalid.details"}""", "application/json", statusCode: StatusCodes.Status401Unauthorized);
                }

                context.Response.Headers["CST"] = $"cst-{number - RefusedSessions}";
                context.Response.Headers["X-SECURITY-TOKEN"] = $"token-{number - RefusedSessions}";
                return Results.Content("""{"accountType":"CFD"}""", "application/json");
            });
            _app.MapGet("/api/v1/markets", (HttpContext context) =>
            {
                Record(context);
                return Results.Content(Markets, "application/json");
            });
            _app.MapGet("/api/v1/prices/{epic}", (HttpContext context, string epic) =>
            {
                Record(context);
                return History(epic, context.Request.QueryString.Value ?? "") is { } prices
                    ? Results.Content(prices, "application/json")
                    : Results.Content("""{"errorCode":"error.prices.not-found"}""", "application/json", statusCode: StatusCodes.Status404NotFound);
            });
        }

        public string Markets { get; set; } = """{"marketDetails":[]}""";

        /// <summary>The answer to a history call for an epic and query, or null for not found.</summary>
        public Func<string, string, string?> History { get; set; } = (_, _) => null;

        /// <summary>How many of the first session calls are refused.</summary>
        public int RefusedSessions { get; set; }

        public Uri ApiUrl => new(_app.Urls.First() + "/");

        public Uri StreamUrl => new UriBuilder(ApiUrl) { Scheme = "ws", Path = "connect" }.Uri;

        public IReadOnlyList<(string ApiKey, string Body)> Sessions => [.. _sessions];

        public IReadOnlyList<(string Url, string ApiKey, string Cst, string Token)> Requests => [.. _requests];

        public IReadOnlyList<string> Subscriptions => [.. _subscriptions];

        public IReadOnlyList<string> Pings => [.. _pings];

        public static async Task<FakeCapitalCom> StartAsync(params Func<WebSocket, CancellationToken, Task>[] connections)
        {
            var capital = new FakeCapitalCom(connections);
            await capital._app.StartAsync(TestContext.Current.CancellationToken);
            return capital;
        }

        public static Func<WebSocket, CancellationToken, Task> SendAndStay(params string[] messages) => async (socket, cancellationToken) =>
        {
            foreach (var message in messages)
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
        };

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            await _app.DisposeAsync();
            _stopping.Dispose();
        }

        private static async Task<string?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            return result.MessageType == WebSocketMessageType.Close ? null : Encoding.UTF8.GetString(buffer, 0, result.Count);
        }

        private void Record(HttpContext context) =>
            _requests.Enqueue((
                context.Request.Path + context.Request.QueryString,
                context.Request.Headers["X-CAP-API-KEY"].ToString(),
                context.Request.Headers["CST"].ToString(),
                context.Request.Headers["X-SECURITY-TOKEN"].ToString()));

        private async Task StreamAsync(HttpContext context)
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            using var aborted = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping.Token);
            try
            {
                _subscriptions.Enqueue(await ReceiveAsync(socket, aborted.Token) ?? "");
                var script = _connections.TryDequeue(out var next) ? next : SendAndStay();
                await script(socket, aborted.Token);

                // Answers pings until the client goes away.
                while (await ReceiveAsync(socket, aborted.Token) is { } message)
                {
                    _pings.Enqueue(message);
                    await socket.SendAsync(
                        Encoding.UTF8.GetBytes("""{"status":"OK","destination":"ping","correlationId":"2","payload":{}}"""),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        aborted.Token);
                }
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or IOException)
            {
                // The client went away.
            }
        }
    }
}
