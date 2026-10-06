using Microsoft.Extensions.Options;

using Trading.Service.Candles;
using Trading.Service.Configuration;
using Trading.Service.Feeds;

namespace Trading.Service.Tests;

public sealed class SyntheticPriceFeedTests
{
    private static readonly DateTimeOffset Today = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    // 15 minute bars for a day, then minute bars up to now, as the charts ask for them.
    private static readonly HistorySpan[] Spans =
    [
        new(Timeframe.M15, Today.AddDays(-1), Today),
        new(Timeframe.M1, Today, Today.AddHours(8).AddSeconds(30)),
    ];

    [Fact]
    public async Task HistoryIsWholeBarsOnTheGridThatFollowOnFromEachOther()
    {
        var bars = await HistoryAsync(CreateFeed(seed: 7));

        var eurusd = bars.Where(b => b.Symbol == "EURUSD").ToList();
        Assert.Equal(96 + 480, eurusd.Count);
        Assert.Equal(Enumerable.Range(0, 96).Select(i => Today.AddDays(-1).AddMinutes(15 * i)), eurusd.Take(96).Select(b => b.Candle.Time));
        Assert.Equal(Enumerable.Range(0, 480).Select(i => Today.AddMinutes(i)), eurusd.Skip(96).Select(b => b.Candle.Time));
        Assert.All(eurusd.Take(96), b => Assert.Equal(Timeframe.M15, b.Resolution));
        Assert.All(eurusd.Skip(96), b => Assert.Equal(Timeframe.M1, b.Resolution));
        Assert.All(eurusd, b => Assert.True(
            b.Candle.High >= Math.Max(b.Candle.Open, b.Candle.Close)
            && b.Candle.Low <= Math.Min(b.Candle.Open, b.Candle.Close)
            && b.Candle.Low > 0
            && b.Candle.TickCount == 0
            && new[] { b.Candle.Open, b.Candle.High, b.Candle.Low, b.Candle.Close }.All(p => p % 0.00001m == 0m)));
        Assert.All(eurusd.Zip(eurusd.Skip(1)), pair => Assert.Equal(pair.First.Candle.Close, pair.Second.Candle.Open));
    }

    // So the charts do not jump where the live prices take over, also after a switch from a real feed.
    [Fact]
    public async Task HistoryEndsWhereTheLivePricesContinue()
    {
        var feed = CreateFeed(seed: 7);
        feed.ContinueFrom([new FeedQuote("EURUSD", 1.12345m, 1.12347m, Today)]);

        var bars = await HistoryAsync(feed);

        Assert.Equal(1.12345m, bars.Last(b => b.Symbol == "EURUSD").Candle.Close);
        Assert.Equal(1.26000m, bars.Last(b => b.Symbol == "GBPUSD").Candle.Close);
    }

    [Fact]
    public async Task TheSameSeedGivesTheSamePrices()
    {
        Assert.Equal(await HistoryAsync(CreateFeed(seed: 7)), await HistoryAsync(CreateFeed(seed: 7)));
        Assert.NotEqual(await HistoryAsync(CreateFeed(seed: 7)), await HistoryAsync(CreateFeed(seed: 8)));
    }

    [Fact]
    public void SymbolMustBeAConfiguredInstrument()
    {
        var options = new SyntheticFeedOptions { Symbols = [new SyntheticSymbolOptions { Symbol = "BTCUSD", StartBid = 60_000m }] };

        Assert.Throws<InvalidOperationException>(() => new SyntheticPriceFeed(Options.Create(options), Options.Create(new TradingOptions()), TimeProvider.System));
    }

    private static async Task<IReadOnlyList<ChartBar>> HistoryAsync(SyntheticPriceFeed feed) =>
        await feed.GetHistoryAsync(Spans, TestContext.Current.CancellationToken);

    private static SyntheticPriceFeed CreateFeed(int seed)
    {
        var trading = new TradingOptions
        {
            Instruments =
            [
                new InstrumentOptions { Symbol = "EURUSD", Digits = 5 },
                new InstrumentOptions { Symbol = "GBPUSD", Digits = 5 },
            ],
        };
        var feed = new SyntheticFeedOptions
        {
            Seed = seed,
            Interval = TimeSpan.FromMilliseconds(250),
            Symbols =
            [
                new SyntheticSymbolOptions { Symbol = "EURUSD", StartBid = 1.08000m, SpreadPoints = 2, StepPoints = 2 },
                new SyntheticSymbolOptions { Symbol = "GBPUSD", StartBid = 1.26000m, SpreadPoints = 3, StepPoints = 3 },
            ],
        };
        return new SyntheticPriceFeed(Options.Create(feed), Options.Create(trading), TimeProvider.System);
    }
}
