using Microsoft.Extensions.Options;

using Trading.Service.Configuration;
using Trading.Service.Feeds;

namespace Trading.Service.Tests;

public sealed class SyntheticPriceFeedTests
{
    private static readonly DateTimeOffset Until = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BackfillIsOnTheGridWithAPositiveSpreadAndInTimeOrder()
    {
        var quotes = CreateFeed(seed: 7).GetBackfill(Until).ToList();

        Assert.Equal(2 * 240, quotes.Count);
        Assert.All(quotes, q => Assert.True(q.Ask > q.Bid && q.Bid % 0.00001m == 0m && q.Ask % 0.00001m == 0m));
        Assert.All(quotes, q => Assert.True(q.Timestamp >= Until.AddMinutes(-1) && q.Timestamp < Until));
        Assert.Equal(quotes.Select(q => q.Timestamp).Order(), quotes.Select(q => q.Timestamp));
    }

    [Fact]
    public void TheSameSeedGivesTheSamePrices()
    {
        Assert.Equal(CreateFeed(seed: 7).GetBackfill(Until), CreateFeed(seed: 7).GetBackfill(Until));
        Assert.NotEqual(CreateFeed(seed: 7).GetBackfill(Until), CreateFeed(seed: 8).GetBackfill(Until));
    }

    [Fact]
    public void SymbolMustBeAConfiguredInstrument()
    {
        var options = new SyntheticFeedOptions { Symbols = [new SyntheticSymbolOptions { Symbol = "BTCUSD", StartBid = 60_000m }] };

        Assert.Throws<InvalidOperationException>(() => new SyntheticPriceFeed(Options.Create(options), Options.Create(new TradingOptions()), TimeProvider.System));
    }

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
            Backfill = TimeSpan.FromMinutes(1),
            Symbols =
            [
                new SyntheticSymbolOptions { Symbol = "EURUSD", StartBid = 1.08000m, SpreadPoints = 2, StepPoints = 2 },
                new SyntheticSymbolOptions { Symbol = "GBPUSD", StartBid = 1.26000m, SpreadPoints = 3, StepPoints = 3 },
            ],
        };
        return new SyntheticPriceFeed(Options.Create(feed), Options.Create(trading), TimeProvider.System);
    }
}
