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

    /// <summary>
    /// The position's events and those of the order it came from, oldest first, each with the input it came from
    /// (ADR 0053). Empty for an unknown position.
    /// </summary>
    Task<IReadOnlyList<RecordedEvent>> ReadPositionEventsAsync(string accountId, string positionId, CancellationToken cancellationToken);

    /// <summary>
    /// The symbol's last recorded price at or before the time and, when given, not after the input. Null when there
    /// is none.
    /// </summary>
    Task<RecordedQuote?> FindQuoteAsync(string symbol, DateTimeOffset atOrBefore, long? notAfterInput, CancellationToken cancellationToken);

    /// <summary>The symbols' recorded prices from the first time to the last, both included, in order.</summary>
    IAsyncEnumerable<RecordedQuote> ReadQuotesBetweenAsync(IReadOnlyCollection<string> symbols, DateTimeOffset first, DateTimeOffset last, CancellationToken cancellationToken);

    /// <summary>
    /// Events of accounts in the groups that happened after the time, with a sequence number after the given one,
    /// oldest first, at most the limit. Read again from the last sequence number for the next page.
    /// </summary>
    Task<IReadOnlyList<RecordedEvent>> ReadGroupEventsSinceAsync(
        IReadOnlyCollection<string> groupIds,
        DateTimeOffset after,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// The latest events of accounts in the groups, or of one of their accounts when <paramref name="accountId"/> is
    /// given, newest first, at most the limit (ADR 0057).
    /// </summary>
    Task<IReadOnlyList<EventEnvelope>> ReadLatestGroupEventsAsync(IReadOnlyCollection<string> groupIds, string? accountId, int limit, CancellationToken cancellationToken);

    /// <summary>Per group, the positions opened and the requests refused since the time (ADR 0057).</summary>
    Task<IReadOnlyDictionary<string, GroupActivity>> CountGroupActivityAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>How big the journal is and the snapshots it keeps, newest first (ADR 0057).</summary>
    Task<JournalStats> GetStatsAsync(CancellationToken cancellationToken);
}

/// <summary>A group's positions opened and requests refused, and how many of those were refused for an old price.</summary>
public sealed record GroupActivity(int PositionsOpened, int Refused, int RefusedForOldPrices);

/// <summary>The journal's size on disk, null when the storage cannot tell, and its snapshots, newest first.</summary>
public sealed record JournalStats(long? SizeBytes, IReadOnlyList<SnapshotInfo> Snapshots);

/// <summary>A snapshot after the input, and when it was taken, null when the storage does not keep it.</summary>
public sealed record SnapshotInfo(long InputSequence, DateTimeOffset? CreatedAt);

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
public sealed record JournaledEvent(EventEnvelope Envelope, string? GroupId)
{
    /// <summary>The input that caused the event. Null only in tests that store events on their own.</summary>
    public long? InputSequence { get; init; }
}

/// <summary>A stored event with the input it came from, or null for events stored before that was kept.</summary>
public sealed record RecordedEvent(EventEnvelope Envelope, long? InputSequence);

/// <summary>A recorded raw price with its input sequence number and its feed, or null when the feed is unknown.</summary>
public sealed record RecordedQuote(long Sequence, Quote Quote, string? Feed);

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
