using System.Runtime.CompilerServices;

using Trading.Service.Candles;

namespace Trading.Service.Tests.Support;

/// <summary>The charts' bars in memory, kept like in Postgres: one bar per feed, time, symbol and resolution.</summary>
internal sealed class InMemoryChartStore : IChartStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Feed, DateTimeOffset Time, string Symbol, Timeframe Resolution), ChartBar> _bars = [];
    private readonly Dictionary<string, DateTimeOffset> _histories = new(StringComparer.Ordinal);

    public int Saves { get; private set; }

    public IReadOnlyList<ChartBar> Bars(string feed)
    {
        lock (_lock)
        {
            return [.. Ordered(_bars.Where(b => b.Key.Feed == feed))];
        }
    }

    public Task<DateTimeOffset?> GetHistoryReachAsync(string feed, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<DateTimeOffset?>(_histories.TryGetValue(feed, out var reach) ? reach : null);
        }
    }

    public Task ForgetHistoryAsync(string feed, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _histories.Remove(feed);
            return Task.CompletedTask;
        }
    }

    public Task ReplaceHistoryAsync(string feed, DateTimeOffset reach, DateTimeOffset until, IReadOnlyList<ChartBar> bars, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            foreach (var key in _bars.Keys.Where(k => k.Feed == feed && k.Time < until).ToList())
            {
                _bars.Remove(key);
            }

            foreach (var bar in bars)
            {
                _bars.Add((feed, bar.Candle.Time, bar.Symbol, bar.Resolution), bar);
            }

            _histories[feed] = reach;
            return Task.CompletedTask;
        }
    }

    public Task SaveAsync(string feed, IReadOnlyList<ChartBar> bars, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            foreach (var bar in bars)
            {
                _bars[(feed, bar.Candle.Time, bar.Symbol, bar.Resolution)] = bar;
            }

            Saves++;
            return Task.CompletedTask;
        }
    }

    public async IAsyncEnumerable<ChartBar> ReadAsync(string feed, DateTimeOffset since, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<ChartBar> bars;
        lock (_lock)
        {
            bars = [.. Ordered(_bars.Where(b => b.Key.Feed == feed && b.Key.Time >= since))];
        }

        foreach (var bar in bars)
        {
            await Task.Yield();
            yield return bar;
        }
    }

    public Task PruneAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            foreach (var key in _bars.Keys.Where(k => k.Time < before).ToList())
            {
                _bars.Remove(key);
            }

            return Task.CompletedTask;
        }
    }

    private static IEnumerable<ChartBar> Ordered(IEnumerable<KeyValuePair<(string Feed, DateTimeOffset Time, string Symbol, Timeframe Resolution), ChartBar>> bars) =>
        bars.OrderBy(b => b.Key.Time).ThenBy(b => b.Key.Symbol, StringComparer.Ordinal).ThenBy(b => b.Key.Resolution.ToString(), StringComparer.Ordinal).Select(b => b.Value);
}
