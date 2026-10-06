using System.Collections.Concurrent;

namespace Trading.Service.Candles;

public enum Timeframe
{
    M1,
    M5,
    M15,
    M30,
    H1,
    H4,
    D1,
}

/// <summary>Bar of bid prices. Time is the start of the bar in UTC.</summary>
public sealed record Candle(DateTimeOffset Time, decimal Open, decimal High, decimal Low, decimal Close, int TickCount);

/// <summary>A bar of a symbol and the timeframe it covers. Bars from a feed's history have no ticks.</summary>
public sealed record ChartBar(string Symbol, Timeframe Resolution, Candle Candle);

/// <summary>Bid candles per symbol and timeframe, built from raw prices and bars, and kept in memory.</summary>
internal sealed class CandleStore(int capacityPerSeries)
{
    public const int DefaultCapacity = 5_000;

    private readonly ConcurrentDictionary<(string Symbol, Timeframe Timeframe), CandleSeries> _series = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the history is loaded at startup. Until then the charts lack their latest part.</summary>
    public Task Ready => _ready.Task;

    public void MarkReady() => _ready.TrySetResult();

    public void Add(string symbol, decimal bid, DateTimeOffset time) => AddBar(new ChartBar(symbol, Timeframe.M1, new Candle(time, bid, bid, bid, bid, 1)));

    /// <summary>Merges the bar into its own timeframe and every longer one. Bars must come in time order.</summary>
    public void AddBar(ChartBar bar)
    {
        var length = Duration(bar.Resolution);
        foreach (var timeframe in Enum.GetValues<Timeframe>())
        {
            if (Duration(timeframe) >= length)
            {
                _series.GetOrAdd((bar.Symbol, timeframe), key => new CandleSeries(key.Timeframe, capacityPerSeries)).Add(bar.Candle);
            }
        }
    }

    /// <summary>
    /// The latest candles, or the latest that start before <paramref name="before"/>, oldest first, with every price moved
    /// by <paramref name="shift"/>.
    /// </summary>
    public IReadOnlyList<Candle> Get(string symbol, Timeframe timeframe, int count, decimal shift, DateTimeOffset? before = null) =>
        _series.TryGetValue((symbol, timeframe), out var series) ? series.Latest(count, shift, before) : [];

    /// <summary>Every symbol's bars of the timeframe that start in the period.</summary>
    public IReadOnlyList<ChartBar> GetBars(Timeframe timeframe, DateTimeOffset from, DateTimeOffset until) =>
        [.. _series
            .Where(s => s.Key.Timeframe == timeframe)
            .OrderBy(s => s.Key.Symbol, StringComparer.Ordinal)
            .SelectMany(s => s.Value.Between(from, until).Select(c => new ChartBar(s.Key.Symbol, timeframe, c)))];

    public static TimeSpan Duration(Timeframe timeframe) => timeframe switch
    {
        Timeframe.M1 => TimeSpan.FromMinutes(1),
        Timeframe.M5 => TimeSpan.FromMinutes(5),
        Timeframe.M15 => TimeSpan.FromMinutes(15),
        Timeframe.M30 => TimeSpan.FromMinutes(30),
        Timeframe.H1 => TimeSpan.FromHours(1),
        Timeframe.H4 => TimeSpan.FromHours(4),
        Timeframe.D1 => TimeSpan.FromDays(1),
        _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, null),
    };

    public static DateTimeOffset BarStart(Timeframe timeframe, DateTimeOffset time)
    {
        var utcTicks = time.UtcTicks;
        return new DateTimeOffset(utcTicks - (utcTicks % Duration(timeframe).Ticks), TimeSpan.Zero);
    }

    private sealed class CandleSeries(Timeframe timeframe, int capacity)
    {
        private readonly Lock _lock = new();
        private readonly List<Candle> _candles = [];

        /// <summary>Merges a price or a bar into the bar its time falls in.</summary>
        public void Add(Candle part)
        {
            var start = BarStart(timeframe, part.Time);
            lock (_lock)
            {
                if (_candles.Count > 0)
                {
                    var last = _candles[^1];
                    if (start == last.Time)
                    {
                        _candles[^1] = last with
                        {
                            High = Math.Max(last.High, part.High),
                            Low = Math.Min(last.Low, part.Low),
                            Close = part.Close,
                            TickCount = last.TickCount + part.TickCount,
                        };
                        return;
                    }

                    // Older than the current bar
                    if (start < last.Time)
                    {
                        return;
                    }
                }

                _candles.Add(part with { Time = start });

                // Trim in batches so the list is not shifted on every new bar.
                if (_candles.Count > capacity + (capacity / 4))
                {
                    _candles.RemoveRange(0, _candles.Count - capacity);
                }
            }
        }

        public List<Candle> Latest(int count, decimal shift, DateTimeOffset? before)
        {
            lock (_lock)
            {
                // The candles are in time order, so the ones before a time end where the first one at or after it is.
                var end = before is { } limit ? _candles.FindIndex(c => c.Time >= limit) : -1;
                var available = end < 0 ? _candles.Count : end;
                var take = Math.Min(Math.Min(count, capacity), available);
                return _candles
                    .Skip(available - take)
                    .Take(take)
                    .Select(c => c with { Open = c.Open + shift, High = c.High + shift, Low = c.Low + shift, Close = c.Close + shift })
                    .ToList();
            }
        }

        public List<Candle> Between(DateTimeOffset from, DateTimeOffset until)
        {
            lock (_lock)
            {
                return _candles.Where(c => c.Time >= from && c.Time < until).ToList();
            }
        }
    }
}
