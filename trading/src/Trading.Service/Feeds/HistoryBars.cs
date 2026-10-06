using Trading.Service.Candles;

namespace Trading.Service.Feeds;

internal static class HistoryBars
{
    /// <summary>
    /// The bars, oldest first per symbol, without those that do not move from the bar before. Vendors repeat the last
    /// price while the market is closed, and the live prices have no bars then.
    /// </summary>
    public static IEnumerable<ChartBar> LeaveOutUnmoved(IEnumerable<ChartBar> bars)
    {
        foreach (var symbolBars in bars.GroupBy(b => b.Symbol, StringComparer.Ordinal))
        {
            Candle? previous = null;
            foreach (var bar in symbolBars.OrderBy(b => b.Candle.Time))
            {
                var candle = bar.Candle;
                var unmoved = candle.Open == candle.High && candle.High == candle.Low && candle.Low == candle.Close && candle.Close == previous?.Close;
                previous = candle;
                if (!unmoved)
                {
                    yield return bar;
                }
            }
        }
    }
}
