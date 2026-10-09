using Trading.Engine.Events;
using Trading.Engine.Inputs;

namespace Trading.Service.Engine;

/// <summary>What kind of input the engine applied, as the staff panel counts them (ADR 0057).</summary>
public enum InputKind
{
    Prices,
    Orders,
    Closes,
    StopChanges,
    OwnLimits,
    FirmCommands,
}

/// <summary>
/// Live figures of the engine loop for the staff panel (ADR 0057), kept in memory minute by minute for the last hour:
/// inputs per kind, requests the engine refused, how long the queue grew, how long batches took to save, and prices per
/// symbol. Nothing here is part of the engine's state, so a restart starts them over.
/// </summary>
internal sealed class EngineMetrics(TimeProvider time)
{
    public const int Minutes = 60;

    private static readonly int Kinds = Enum.GetValues<InputKind>().Length;

    private readonly Lock _lock = new();
    private readonly Bucket[] _buckets = new Bucket[Minutes];
    private readonly Dictionary<string, SymbolCount> _symbols = new(StringComparer.Ordinal);

    /// <summary>How the latest start went, once the journal has been replayed.</summary>
    public EngineStart? Start { get; private set; }

    public void Started(DateTimeOffset at, TimeSpan took, long replayed, bool configurationChanged) =>
        Start = new EngineStart(at, took, replayed, configurationChanged);

    /// <summary>Counts an applied input and the requests among its events that the engine refused.</summary>
    public void InputApplied(EngineInput input, IReadOnlyList<EngineEvent> events)
    {
        var minute = CurrentMinute();
        var refused = 0;
        foreach (var engineEvent in events)
        {
            if (engineEvent is InputRejected { Input: not Quote })
            {
                refused++;
            }
        }

        lock (_lock)
        {
            var bucket = BucketFor(minute);
            bucket.Inputs[(int)KindOf(input)]++;
            bucket.Refused += refused;
            if (input is Quote quote)
            {
                if (!_symbols.TryGetValue(quote.Symbol, out var count))
                {
                    _symbols[quote.Symbol] = count = new SymbolCount();
                }

                count.Add(minute);
            }
        }
    }

    /// <summary>Notes how many items waited in the queue when the loop took the next one.</summary>
    public void QueueSampled(int waiting)
    {
        var minute = CurrentMinute();
        lock (_lock)
        {
            var bucket = BucketFor(minute);
            bucket.MaxQueue = Math.Max(bucket.MaxQueue, waiting);
        }
    }

    /// <summary>Notes how long a batch of the journal took to save.</summary>
    public void BatchSaved(TimeSpan took)
    {
        var minute = CurrentMinute();
        lock (_lock)
        {
            var bucket = BucketFor(minute);
            bucket.Saves++;
            bucket.SaveTicks += took.Ticks;
            bucket.MaxSaveTicks = Math.Max(bucket.MaxSaveTicks, took.Ticks);
        }
    }

    /// <summary>The last hour, oldest minute first, ending with the minute now going on.</summary>
    public IReadOnlyList<MinuteFigures> LastHour()
    {
        var now = CurrentMinute();
        lock (_lock)
        {
            var minutes = new List<MinuteFigures>(Minutes);
            for (var minute = now - Minutes + 1; minute <= now; minute++)
            {
                var bucket = _buckets[Index(minute)];
                var start = DateTimeOffset.UnixEpoch + TimeSpan.FromMinutes(minute);
                minutes.Add(bucket is not null && bucket.Minute == minute
                    ? new MinuteFigures(
                        start,
                        Enum.GetValues<InputKind>().ToDictionary(k => k, k => bucket.Inputs[(int)k]),
                        bucket.Refused,
                        bucket.MaxQueue,
                        bucket.Saves,
                        bucket.Saves == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(bucket.SaveTicks / bucket.Saves),
                        TimeSpan.FromTicks(bucket.MaxSaveTicks))
                    : new MinuteFigures(start, Enum.GetValues<InputKind>().ToDictionary(k => k, _ => 0), 0, 0, 0, TimeSpan.Zero, TimeSpan.Zero));
            }

            return minutes;
        }
    }

    /// <summary>Prices each symbol got in the last 60 seconds, estimated from this minute and the one before.</summary>
    public IReadOnlyDictionary<string, int> PricesLastMinute()
    {
        var (minute, window) = Window();
        lock (_lock)
        {
            return _symbols.ToDictionary(
                s => s.Key,
                s =>
                {
                    var (current, previous) = s.Value.Counts(minute);
                    return (int)Math.Round(window.Count(current, previous));
                },
                StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Inputs per second of the kind, or of every kind when it is null, over the last 60 seconds, or over the time since
    /// the start when that is shorter. Estimated from this minute and the one before, as if inputs came evenly.
    /// </summary>
    public decimal PerSecond(InputKind? kind)
    {
        var (minute, window) = Window();
        lock (_lock)
        {
            int CountIn(long m) => _buckets[Index(m)] is { } bucket && bucket.Minute == m
                ? kind is { } k ? bucket.Inputs[(int)k] : bucket.Inputs.Sum()
                : 0;
            return window.Count(CountIn(minute), CountIn(minute - 1)) / window.Seconds;
        }
    }

    // The minute now, and how much of the minute before still falls within the last 60 seconds of running.
    private (long Minute, RateWindow Window) Window()
    {
        var now = time.GetUtcNow();
        var minute = (now.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / TimeSpan.TicksPerMinute;
        var intoMinute = (decimal)(now.UtcTicks % TimeSpan.TicksPerMinute) / TimeSpan.TicksPerSecond;
        var running = Start is { } start ? (decimal)(now - start.At).TotalSeconds : 60m;
        return (minute, running >= 60m
            ? new RateWindow(60m, Math.Max(0m, 60m - intoMinute) / 60m)
            : new RateWindow(Math.Max(1m, running), 1m));
    }

    private readonly record struct RateWindow(decimal Seconds, decimal PreviousShare)
    {
        public decimal Count(int current, int previous) => current + (previous * PreviousShare);
    }

    private static InputKind KindOf(EngineInput input) => input switch
    {
        Quote => InputKind.Prices,
        PlaceOrder or CancelOrder or ModifyOrder => InputKind.Orders,
        ClosePosition or CloseAllPositions => InputKind.Closes,
        ModifyPosition => InputKind.StopChanges,
        SetOwnLimits or LockTrading => InputKind.OwnLimits,
        _ => InputKind.FirmCommands,
    };

    private static int Index(long minute) => (int)(((minute % Minutes) + Minutes) % Minutes);

    private long CurrentMinute() => (time.GetUtcNow().UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / TimeSpan.TicksPerMinute;

    // Lock held.
    private Bucket BucketFor(long minute)
    {
        var index = Index(minute);
        if (_buckets[index] is not { } bucket || bucket.Minute != minute)
        {
            _buckets[index] = bucket = new Bucket(minute, Kinds);
        }

        return bucket;
    }

    private sealed class Bucket(long minute, int kinds)
    {
        public long Minute { get; } = minute;

        public int[] Inputs { get; } = new int[kinds];

        public int Refused { get; set; }

        public int MaxQueue { get; set; }

        public int Saves { get; set; }

        public long SaveTicks { get; set; }

        public long MaxSaveTicks { get; set; }
    }

    private sealed class SymbolCount
    {
        private long _minute = long.MinValue;
        private int _count;
        private int _previous;

        public void Add(long minute)
        {
            if (minute != _minute)
            {
                _previous = minute == _minute + 1 ? _count : 0;
                _minute = minute;
                _count = 0;
            }

            _count++;
        }

        /// <summary>This minute's prices and the minute before's, seen from the minute now.</summary>
        public (int Current, int Previous) Counts(long now) => now == _minute ? (_count, _previous) : now == _minute + 1 ? (0, _count) : (0, 0);
    }
}

/// <summary>How the engine started: when, how long the replay took, how many inputs it replayed after the snapshot, and
/// whether the configuration had changed since the snapshot.</summary>
public sealed record EngineStart(DateTimeOffset At, TimeSpan Took, long Replayed, bool ConfigurationChanged);

/// <summary>One minute of the engine loop.</summary>
public sealed record MinuteFigures(
    DateTimeOffset Start,
    IReadOnlyDictionary<InputKind, int> Inputs,
    int Refused,
    int MaxQueue,
    int Saves,
    TimeSpan AverageSave,
    TimeSpan SlowestSave);
