using Microsoft.AspNetCore.SignalR.Client;

using Trading.Service.Candles;
using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>The charts across restarts and switches of the price feed (ADR 0048, ADR 0051).</summary>
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

        // Three years in day bars for the week and month charts, 180 days in hour bars, the latest 30 in 15 minute bars,
        // and the latest 2 and today in minute bars.
        Assert.Equal(
            [
                new HistorySpan(Timeframe.D1, Today.AddDays(-1095), Today.AddDays(-180)),
                new HistorySpan(Timeframe.H1, Today.AddDays(-180), Today.AddDays(-30)),
                new HistorySpan(Timeframe.M15, Today.AddDays(-30), Today.AddDays(-2)),
                new HistorySpan(Timeframe.M1, Today.AddDays(-2), Today.AddHours(8)),
            ],
            Assert.Single(feed.HistoryRequests));
    }

    [Fact]
    public async Task HistoryIsLoadedAgainWhenTheChartsReachFurtherBack()
    {
        var backend = new InMemoryBackend();
        var shorter = new Dictionary<string, string> { ["Charts:DayHistory"] = "30.00:00:00", ["Charts:History"] = "30.00:00:00" };
        using (var first = new ServiceFactory(backend, settings: shorter, feed: new ManualPriceFeed("Real") { History = History }))
        {
            (await first.CreateTraderClientAsync(AccountId)).Dispose();
            await first.PushQuoteAsync("EURUSD", 1.12000m, 1.12010m);
        }

        var same = new ManualPriceFeed("Real") { History = History };
        using (var second = new ServiceFactory(backend, settings: shorter, feed: same))
        {
            (await second.LoginAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword)).Dispose();
        }

        var longer = new ManualPriceFeed("Real") { History = History };
        using var third = new ServiceFactory(backend, feed: longer);
        (await third.LoginAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword)).Dispose();

        // Asked at most for the gap after the stored bars, never for the history again.
        Assert.DoesNotContain(same.HistoryRequests, spans => spans[0].From == Today.AddDays(-30));
        Assert.Equal(Today.AddDays(-1095), longer.HistoryRequests[0][0].From);
    }

    [Fact]
    public async Task OlderCandlesAreReadBeforeATime()
    {
        using var factory = new ServiceFactory(feed: new ManualPriceFeed("Real") { History = History });
        using var client = await factory.CreateTraderClientAsync(AccountId);

        var before = await client.GetJsonAsync(
            $"/api/accounts/{AccountId}/candles/EURUSD?timeframe=M15&before={Uri.EscapeDataString(History[1].Candle.Time.ToString("O"))}");

        Assert.Equal(History[0].Candle.Time, Assert.Single(before.EnumerateArray()).GetProperty("time").GetDateTimeOffset());
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

        Assert.DoesNotContain(feed.HistoryRequests, LoadsTheHistory);
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

        Assert.True(LoadsTheHistory(feed.HistoryRequests[0]));
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

    // ADR 0056: the minutes while the service was stopped come from the feed's history once prices come again.
    [Fact]
    public async Task AGapWhileTheServiceWasStoppedIsFilledFromTheFeed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new InMemoryBackend();
        using (var first = new ServiceFactory(backend, feed: new ManualPriceFeed("Real")))
        {
            (await first.CreateTraderClientAsync(AccountId)).Dispose();
            await first.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
            first.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var feed = new ManualPriceFeed("Real") { History = [Minute(10, 1.08500m), Minute(20, 1.08700m)] };
        using var second = new ServiceFactory(backend, feed: feed);
        var cookie = await second.LoginCookieAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword);
        using var restarted = await second.LoginAsync(ServiceFactory.EmailOf(AccountId), ServiceFactory.TraderPassword);
        await using var connection = second.CreateHubConnection(cookie);
        var told = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On("Charts", () => told.TrySetResult());
        await connection.StartAsync(cancellationToken);
        await connection.InvokeAsync("Subscribe", AccountId, cancellationToken);

        // The service was stopped for half an hour, and the first price after it fills the minutes between.
        second.Time.Advance(TimeSpan.FromMinutes(30));
        await second.PushQuoteAsync("EURUSD", 1.09000m, 1.09010m);
        await Eventually.Within(told.Task);

        Assert.Equal([new HistorySpan(Timeframe.M1, Today.AddHours(8).AddMinutes(1), Today.AddHours(8).AddMinutes(30))], Assert.Single(feed.HistoryRequests));
        var minutes = await CandlesAsync(restarted, "M1");
        Assert.Equal([0, 10, 20, 30], minutes.Select(c => (int)(c.GetProperty("time").GetDateTimeOffset() - Today.AddHours(8)).TotalMinutes));
        Assert.Equal([1.08000m, 1.08500m, 1.08700m, 1.09000m], minutes.Select(c => c.GetProperty("close").GetDecimal() + Markup));
        Assert.Contains(backend.Charts.Bars("Real"), b => b.Candle.Time == Today.AddHours(8).AddMinutes(20));
    }

    // A feed that stops while the service runs leaves a gap too. The engine gets the prices after it at once.
    [Fact]
    public async Task AGapInTheFeedIsFilledWhileTheEngineGoesOn()
    {
        var feed = new ManualPriceFeed("Real");
        using var factory = new ServiceFactory(feed: feed);
        using var client = await factory.CreateTraderClientAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        factory.Time.Advance(TimeSpan.FromSeconds(70));
        await factory.PushQuoteAsync("EURUSD", 1.08100m, 1.08110m);

        // A minute with prices on both sides is no gap, only the minutes without any are.
        feed.History = [Minute(3, 1.08300m)];
        factory.Time.Advance(TimeSpan.FromMinutes(4));
        await factory.PushQuoteAsync("EURUSD", 1.08400m, 1.08410m);

        await Eventually.ThatAsync(async () => (await CandlesAsync(client, "M1")).Count == 4, "the gap to be filled");
        Assert.Equal(new HistorySpan(Timeframe.M1, Today.AddHours(8).AddMinutes(2), Today.AddHours(8).AddMinutes(5)), Assert.Single(feed.HistoryRequests.Skip(1))[0]);
        var minutes = await CandlesAsync(client, "M1");
        Assert.Equal([1.08000m, 1.08100m, 1.08300m, 1.08400m], minutes.Select(c => c.GetProperty("close").GetDecimal() + Markup));
    }

    [Fact]
    public async Task AGapTheFeedCannotFillStaysAndThePricesAfterItAreKept()
    {
        var feed = new ManualPriceFeed("Real");
        using var factory = new ServiceFactory(feed: feed);
        using var client = await factory.CreateTraderClientAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);

        feed.HistoryFailure = new HttpRequestException("Too many requests");
        factory.Time.Advance(TimeSpan.FromMinutes(5));
        await factory.PushQuoteAsync("EURUSD", 1.08400m, 1.08410m);

        // Asked again after 2, 10 and 30 seconds, and then the price after the gap is added without it.
        await Eventually.ThatAsync(
            async () =>
            {
                factory.Time.Advance(TimeSpan.FromSeconds(1));
                return (await CandlesAsync(client, "M1")).Count == 2;
            },
            "the price after the gap to be added");
        Assert.Equal(1 + 4, feed.HistoryRequests.Count);
    }

    // Made-up prices continue from the last ones, so there is nothing true to fill with.
    [Fact]
    public async Task AMadeUpFeedLeavesItsGapsAlone()
    {
        var feed = new ContinuingPriceFeed("Synthetic");
        using var factory = new ServiceFactory(feed: feed);
        using var client = await factory.CreateTraderClientAsync(AccountId);
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
        factory.Time.Advance(TimeSpan.FromMinutes(5));
        await factory.PushQuoteAsync("EURUSD", 1.08400m, 1.08410m);

        Assert.Equal(2, (await CandlesAsync(client, "M1")).Count);
        Assert.Single(feed.HistoryRequests);
    }

    // The whole history, from the first day the charts reach, rather than a gap.
    private static bool LoadsTheHistory(IReadOnlyList<HistorySpan> spans) => spans[0].From == Today.AddDays(-1095);

    // A minute bar from the feed's history, at the minute after 08:00 today.
    private static ChartBar Minute(int minute, decimal close) =>
        new("EURUSD", Timeframe.M1, new Candle(Today.AddHours(8).AddMinutes(minute), close, close, close, close, 0));

    // The standard group's markup on the bid of EURUSD, which the candles include.
    private static decimal Markup => 0.00001m;

    private static readonly string[] PriceNames = ["open", "high", "low", "close"];

    private static decimal[] Prices(System.Text.Json.JsonElement candle) => [.. PriceNames.Select(p => candle.GetProperty(p).GetDecimal() + Markup)];

    private static async Task<List<System.Text.Json.JsonElement>> CandlesAsync(HttpClient client, string timeframe) =>
        [.. (await client.GetJsonAsync($"/api/accounts/{AccountId}/candles/EURUSD?timeframe={timeframe}")).EnumerateArray()];
}
