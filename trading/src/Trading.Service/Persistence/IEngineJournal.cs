using Trading.Engine;
using Trading.Engine.Inputs;
using Trading.Service.Engine;

namespace Trading.Service.Persistence;

/// <summary>
/// Durable record of every engine input, the events they caused and periodic snapshots.
/// The engine state can always be rebuilt from the latest snapshot and the inputs after it.
/// </summary>
public interface IEngineJournal
{
    /// <summary>Creates or upgrades the storage. Safe to call on every start.</summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<JournalSnapshot?> LoadLatestSnapshotAsync(CancellationToken cancellationToken);

    /// <summary>Inputs after the sequence number, in order.</summary>
    IAsyncEnumerable<JournaledInput> ReadInputsAsync(long afterSequence, CancellationToken cancellationToken);

    Task<long> GetLastInputSequenceAsync(CancellationToken cancellationToken);

    Task<long> GetLastEventSequenceAsync(CancellationToken cancellationToken);

    /// <summary>Stores the batch atomically: all of it or nothing.</summary>
    Task AppendAsync(JournalBatch batch, CancellationToken cancellationToken);

    /// <summary>The account's first events after the sequence number, oldest first.</summary>
    Task<IReadOnlyList<EventEnvelope>> ReadEventsAsync(string accountId, long afterSequence, int limit, CancellationToken cancellationToken);

    /// <summary>The account's last events before the sequence number, oldest first.</summary>
    Task<IReadOnlyList<EventEnvelope>> ReadEventsBeforeAsync(string accountId, long beforeSequence, int limit, CancellationToken cancellationToken);

    /// <summary>All events after the sequence number, oldest first, whoever they belong to.</summary>
    Task<IReadOnlyList<EventEnvelope>> ReadAllEventsAsync(long afterSequence, int limit, CancellationToken cancellationToken);

    /// <summary>Events of accounts in the groups after the sequence number, oldest first. A firm reads its own groups.</summary>
    Task<IReadOnlyList<EventEnvelope>> ReadGroupEventsAsync(IReadOnlyCollection<string> groupIds, long afterSequence, int limit, CancellationToken cancellationToken);

    /// <summary>The feed's recorded prices since the given time, in order. Used to rebuild the charts.</summary>
    IAsyncEnumerable<Quote> ReadQuotesAsync(DateTimeOffset since, string feed, CancellationToken cancellationToken);

    /// <summary>The feed of the last recorded price, or null when there is none or its feed is unknown.</summary>
    Task<string?> GetLastQuoteFeedAsync(CancellationToken cancellationToken);
}

/// <summary>An input with its gapless sequence number.</summary>
public sealed record JournaledInput(long Sequence, EngineInput Input)
{
    /// <summary>The price feed a quote came from. Null for other inputs, and for quotes recorded before feeds were.</summary>
    public string? Feed { get; init; }
}

/// <summary>
/// Engine state after <paramref name="InputSequence"/>. The fingerprint identifies the configuration
/// the inputs after it were applied with.
/// </summary>
public sealed record JournalSnapshot(long InputSequence, long EventSequence, string ConfigurationFingerprint, EngineState State);

/// <summary>An event with the trading group of its account, or null when it has none.</summary>
public sealed record JournaledEvent(EventEnvelope Envelope, string? GroupId);

public sealed record JournalBatch(IReadOnlyList<JournaledInput> Inputs, IReadOnlyList<JournaledEvent> Events, JournalSnapshot? Snapshot)
{
    public long LastInputSequence => Inputs.Count == 0 ? 0 : Inputs[^1].Sequence;
}

public sealed class JournalOptions
{
    public const string SectionName = "Journal";

    /// <summary>Inputs between snapshots. A restart replays at most this many inputs.</summary>
    public int SnapshotInterval { get; init; } = 10_000;

    /// <summary>Snapshots kept. Older ones are deleted. Inputs and events are always kept.</summary>
    public int SnapshotsToKeep { get; init; } = 3;
}
