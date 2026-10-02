using Trading.Service.Candles;

namespace Trading.Service.Tests;

public sealed class CandleStoreTests
{
    private static readonly DateTimeOffset Eight = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PricesAreAggregatedIntoBarsPerTimeframe()
    {
        var store = new CandleStore(100);
        store.Add("EURUSD", 1.1m, Eight.AddSeconds(5));
        store.Add("EURUSD", 1.3m, Eight.AddSeconds(20));
        store.Add("EURUSD", 1.0m, Eight.AddSeconds(40));
        store.Add("EURUSD", 1.2m, Eight.AddSeconds(59));
        store.Add("EURUSD", 1.25m, Eight.AddMinutes(1));

        Assert.Equal(
            [new Candle(Eight, 1.1m, 1.3m, 1.0m, 1.2m, 4), new Candle(Eight.AddMinutes(1), 1.25m, 1.25m, 1.25m, 1.25m, 1)],
            store.Get("EURUSD", Timeframe.M1, 10, 0m));
        Assert.Equal(
            [new Candle(Eight, 1.1m, 1.3m, 1.0m, 1.25m, 5)],
            store.Get("EURUSD", Timeframe.M5, 10, 0m));
    }

    [Theory]
    [InlineData(Timeframe.M15, "2026-10-05T10:44:59Z", "2026-10-05T10:30:00Z")]
    [InlineData(Timeframe.H4, "2026-10-05T10:30:00Z", "2026-10-05T08:00:00Z")]
    [InlineData(Timeframe.D1, "2026-10-05T23:59:59Z", "2026-10-05T00:00:00Z")]
    public void BarsStartOnTheTimeframeBoundaryInUtc(Timeframe timeframe, string time, string expectedStart)
    {
        Assert.Equal(DateTimeOffset.Parse(expectedStart, System.Globalization.CultureInfo.InvariantCulture), CandleStore.BarStart(timeframe, DateTimeOffset.Parse(time, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void PricesOlderThanTheCurrentBarAreIgnored()
    {
        var store = new CandleStore(100);
        store.Add("EURUSD", 1.2m, Eight.AddMinutes(1));
        store.Add("EURUSD", 9.9m, Eight);

        Assert.Equal(1.2m, Assert.Single(store.Get("EURUSD", Timeframe.M1, 10, 0m)).Close);
    }

    [Fact]
    public void OnlyTheLatestBarsUpToTheCapacityAreKept()
    {
        var store = new CandleStore(4);
        for (var minute = 0; minute < 10; minute++)
        {
            store.Add("EURUSD", minute, Eight.AddMinutes(minute));
        }

        var candles = store.Get("EURUSD", Timeframe.M1, 100, 0m);

        Assert.Equal([6m, 7m, 8m, 9m], candles.Select(c => c.Close));
    }

    [Fact]
    public void ShiftMovesEveryPrice()
    {
        var store = new CandleStore(100);
        store.Add("EURUSD", 1.08000m, Eight);

        var candle = Assert.Single(store.Get("EURUSD", Timeframe.M1, 1, -0.00001m));

        Assert.Equal(new Candle(Eight, 1.07999m, 1.07999m, 1.07999m, 1.07999m, 1), candle);
    }

    [Fact]
    public void UnknownSymbolHasNoCandles()
    {
        Assert.Empty(new CandleStore(100).Get("EURUSD", Timeframe.M1, 10, 0m));
    }
}
