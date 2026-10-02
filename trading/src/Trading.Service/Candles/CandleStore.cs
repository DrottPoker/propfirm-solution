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

/// <summary>Bid candles per symbol and timeframe, built from raw prices and kept in memory.</summary>
internal sealed class CandleStore(int capacityPerSeries)
{
    public const int DefaultCapacity = 5_000;

    private readonly ConcurrentDictionary<(string Symbol, Timeframe Timeframe), CandleSeries> _series = new();

    public void Add(string symbol, decimal bid, DateTimeOffset time)
    {
        foreach (var timeframe in Enum.GetValues<Timeframe>())
        {
            _series.GetOrAdd((symbol, timeframe), key => new CandleSeries(key.Timeframe, capacityPerSeries)).Add(bid, time);
        }
    }

    /// <summary>The latest candles, oldest first, with every price moved by <paramref name="shift"/>.</summary>
    public IReadOnlyList<Candle> Get(string symbol, Timeframe timeframe, int count, decimal shift) =>
        _series.TryGetValue((symbol, timeframe), out var series) ? series.Latest(count, shift) : [];

    public static DateTimeOffset BarStart(Timeframe timeframe, DateTimeOffset time)
    {
        var utcTicks = time.UtcTicks;
        var barTicks = timeframe switch
        {
            Timeframe.M1 => TimeSpan.TicksPerMinute,
            Timeframe.M5 => 5 * TimeSpan.TicksPerMinute,
            Timeframe.M15 => 15 * TimeSpan.TicksPerMinute,
            Timeframe.M30 => 30 * TimeSpan.TicksPerMinute,
            Timeframe.H1 => TimeSpan.TicksPerHour,
            Timeframe.H4 => 4 * TimeSpan.TicksPerHour,
            Timeframe.D1 => TimeSpan.TicksPerDay,
            _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, null),
        };
        return new DateTimeOffset(utcTicks - (utcTicks % barTicks), TimeSpan.Zero);
    }

    private sealed class CandleSeries(Timeframe timeframe, int capacity)
    {
        private readonly Lock _lock = new();
        private readonly List<Candle> _candles = [];

        public void Add(decimal price, DateTimeOffset time)
        {
            var start = BarStart(timeframe, time);
            lock (_lock)
            {
                if (_candles.Count > 0)
                {
                    var last = _candles[^1];
                    if (start == last.Time)
                    {
                        _candles[^1] = last with
                        {
                            High = Math.Max(last.High, price),
                            Low = Math.Min(last.Low, price),
                            Close = price,
                            TickCount = last.TickCount + 1,
                        };
                        return;
                    }

                    // Older than the current bar
                    if (start < last.Time)
                    {
                        return;
                    }
                }

                _candles.Add(new Candle(start, price, price, price, price, 1));

                // Trim in batches so the list is not shifted on every new bar.
                if (_candles.Count > capacity + (capacity / 4))
                {
                    _candles.RemoveRange(0, _candles.Count - capacity);
                }
            }
        }

        public List<Candle> Latest(int count, decimal shift)
        {
            lock (_lock)
            {
                var take = Math.Min(Math.Min(count, capacity), _candles.Count);
                return _candles
                    .Skip(_candles.Count - take)
                    .Select(c => c with { Open = c.Open + shift, High = c.High + shift, Low = c.Low + shift, Close = c.Close + shift })
                    .ToList();
            }
        }
    }
}
