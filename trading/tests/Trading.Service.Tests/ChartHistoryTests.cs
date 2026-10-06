using Trading.Service.Candles;
using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>The charts across restarts and switches of the price feed (ADR 0048).</summary>
public sealed class ChartHistoryTests
{
    private const string AccountId = "T1";

    // The factories' clock starts at 2026-10-05 08:00 UTC.
    private static readonly DateTimeOffset Today = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    // Two 15 minute bars from the feed's history, which fill one hour.
    private static readonly ChartBar[] History =
    [
        new("EURUSD", Timeframe.M15, new Candle(Today.AddDays(-1).AddHours(8), 1.10000m, 1.10300m, 1.09900m, 1.10200m, 0)),
        new("EURUSD", Timeframe.M15, new Candle(Today.AddDays(-1).AddHours(8).AddMinutes(15), 1.10200m, 1.10400m, 1.10100m, 1.10150m, 0)),
    ];

    [Fact]
    public async Task HistoryIsLoadedWhenTheServiceSwitchesToAFeed()
    {
        var feed = new ManualPriceFeed("Real") { History = History };
        using var factory = new ServiceFactory(feed: feed);
        using var client = await factory.CreateTraderClientAsync(AccountId);

        var hours = await CandlesAsync(client, "H1");

        Assert.Equal(Today.AddDays(-1).AddHours(8), Assert.Single(hours).GetProperty("time").GetDateTimeOffset());
        Assert.Equal([1.10000m, 1.10400m, 1.09900m, 1.10150m], Prices(hours[0]));
        Assert.Empty(await CandlesAsync(client, "M5"));

        // 30 days, the latest 2 and today in minute bars.
        Assert.Equal(
            [new HistorySpan(Timeframe.M15, Today.AddDays(-30), Today.AddDays(-2)), new HistorySpan(Timeframe.M1, Today.AddDays(-2), Today.AddHours(8))],
            Assert.Single(feed.HistoryRequests));
    }

    [Fact]
    public async Task HistoryIsKeptAndNotLoadedAgainOnARestart()
    {
        var backend = new InMemoryBackend();
        using (var first = new ServiceFactory(backend, feed: new ManualPriceFeed("Real") { History = History }))
        {
            (await first.CreateTraderClientAsync(AccountId)).Dispose();
            await first.PushQuoteAsync("EURUSD", 1.12000m, 1.12010m);
        }

        var feed = new ManualPriceFeed("Real");
        using var second = new ServiceFactory(backend, feed: feed);
        using var restarted = await second.LoginAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword);

        Assert.Empty(feed.HistoryRequests);
        Assert.Equal([Today.AddDays(-1).AddHours(8), Today.AddHours(8)], (await CandlesAsync(restarted, "H1")).Select(c => c.GetProperty("time").GetDateTimeOffset()));
    }

    // For example made-up prices from a weekend, when the real feed is back on Monday.
    [Fact]
    public async Task ChartsOfAFeedLeaveOutThePricesOfOtherFeeds()
    {
        var backend = new InMemoryBackend();
        using (var first = new ServiceFactory(backend, feed: new ContinuingPriceFeed("Synthetic")))
        {
            (await first.CreateTraderClientAsync(AccountId)).Dispose();
            await first.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        }

        var feed = new ManualPriceFeed("Real");
        using var second = new ServiceFactory(backend, feed: feed);
        using var restarted = await second.LoginAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword);

        Assert.Single(feed.HistoryRequests);
        Assert.Empty(await CandlesAsync(restarted, "M1"));
        await second.PushQuoteAsync("EURUSD", 1.12000m, 1.12010m);
        Assert.Equal(1.12000m, Assert.Single(await CandlesAsync(restarted, "M1")).GetProperty("close").GetDecimal() + Markup);
    }

    [Fact]
    public async Task SwitchingBackLoadsTheHistoryAgain()
    {
        var backend = new InMemoryBackend();
        foreach (var feed in new ManualPriceFeed[] { new("Real"), new ContinuingPriceFeed("Synthetic") })
        {
            using var factory = new ServiceFactory(backend, feed: feed);
            await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        }

        var real = new ManualPriceFeed("Real");
        using var again = new ServiceFactory(backend, feed: real);
        await again.PushQuoteAsync("EURUSD", 1.12000m, 1.12010m);

        Assert.Single(real.HistoryRequests);
    }

    [Fact]
    public async Task HistoryThatCouldNotBeLoadedIsTriedAgainAtTheNextStart()
    {
        var backend = new InMemoryBackend();
        using (var first = new ServiceFactory(backend, feed: new ManualPriceFeed("Real") { HistoryFailure = new HttpRequestException("Too many requests") }))
        {
            (await first.CreateTraderClientAsync(AccountId)).Dispose();
            await first.PushQuoteAsync("EURUSD", 1.12000m, 1.12010m);
        }

        var feed = new ManualPriceFeed("Real") { History = History };
        using var second = new ServiceFactory(backend, feed: feed);
        using var restarted = await second.LoginAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword);

        Assert.Single(feed.HistoryRequests);
        Assert.Equal(2, (await CandlesAsync(restarted, "H1")).Count);
    }

    // The journal has every price, so a crash loses nothing: the minutes that were not stored are replayed.
    [Fact]
    public async Task FinishedMinutesAreStoredAndTheRestIsReplayed()
    {
        using var first = new ServiceFactory();
        (await first.CreateTraderClientAsync(AccountId)).Dispose();
        await first.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        first.Time.Advance(TimeSpan.FromSeconds(70));
        await first.PushQuoteAsync("EURUSD", 1.09000m, 1.09010m);

        await Eventually.ThatAsync(() => first.Backend.Charts.Bars(ManualPriceFeed.DefaultName).Count == 1, "the finished minute to be stored");
        Assert.Equal(Today.AddHours(8), first.Backend.Charts.Bars(ManualPriceFeed.DefaultName)[0].Candle.Time);

        using var second = new ServiceFactory(first.Backend.Crashed());
        using var restarted = await second.LoginAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword);

        var minutes = await CandlesAsync(restarted, "M1");
        Assert.Equal([1.08000m, 1.09000m], minutes.Select(c => c.GetProperty("close").GetDecimal() + Markup));
    }

    [Fact]
    public async Task ANormalStopStoresTheFinishedMinutes()
    {
        var backend = new InMemoryBackend();
        using (var factory = new ServiceFactory(backend))
        {
            (await factory.CreateTraderClientAsync(AccountId)).Dispose();
            await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
            factory.Time.Advance(TimeSpan.FromSeconds(3));
            await factory.PushQuoteAsync("EURUSD", 1.08100m, 1.08110m);
            factory.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var bar = Assert.Single(backend.Charts.Bars(ManualPriceFeed.DefaultName));
        Assert.Equal(new Candle(Today.AddHours(8), 1.08000m, 1.08100m, 1.08000m, 1.08100m, 2), bar.Candle);
    }

    // The standard group's markup on the bid of EURUSD, which the candles include.
    private static decimal Markup => 0.00001m;

    private static readonly string[] PriceNames = ["open", "high", "low", "close"];

    private static decimal[] Prices(System.Text.Json.JsonElement candle) => [.. PriceNames.Select(p => candle.GetProperty(p).GetDecimal() + Markup)];

    private static async Task<List<System.Text.Json.JsonElement>> CandlesAsync(HttpClient client, string timeframe) =>
        [.. (await client.GetJsonAsync($"/api/accounts/{AccountId}/candles/EURUSD?timeframe={timeframe}")).EnumerateArray()];
}
