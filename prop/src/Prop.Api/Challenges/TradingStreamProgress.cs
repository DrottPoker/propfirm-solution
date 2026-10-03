using System.Collections.Concurrent;

namespace Prop.Api.Challenges;

/// <summary>
/// How far each firm's event stream has been read: a time before which every event of the firm is handled. A
/// challenge may only run out of time once its last trades are known, so a trade made just before the deadline
/// still counts when its event arrives late, for example after a restart.
/// </summary>
internal sealed class TradingStreamProgress
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _caughtUp = new(StringComparer.Ordinal);
    private int _awaited;

    /// <summary>Set when a firm's stream catches up while someone waits for it.</summary>
    public WakeUp CaughtUp { get; } = new();

    /// <summary>Every event the platform had when this time came is handled. Null until the stream was read to its end.</summary>
    public DateTimeOffset? CaughtUpAt(string firmId) => _caughtUp.TryGetValue(firmId, out var at) ? at : null;

    /// <summary>The caller waits for a stream to catch up, and is woken through <see cref="CaughtUp"/>.</summary>
    public void Await() => Volatile.Write(ref _awaited, 1);

    /// <summary>The stream was read to its end by a request that started at <paramref name="requestedAt"/>, and its events are handled.</summary>
    public void Record(string firmId, DateTimeOffset requestedAt)
    {
        _caughtUp.AddOrUpdate(firmId, requestedAt, (_, before) => requestedAt > before ? requestedAt : before);
        if (Interlocked.Exchange(ref _awaited, 0) == 1)
        {
            CaughtUp.Set();
        }
    }
}
