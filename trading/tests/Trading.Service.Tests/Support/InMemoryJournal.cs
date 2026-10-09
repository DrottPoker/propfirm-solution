using System.Runtime.CompilerServices;
using System.Text.Json;

using Trading.Engine;
using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Service.Engine;
using Trading.Service.Json;
using Trading.Service.Persistence;

namespace Trading.Service.Tests.Support;

/// <summary>
/// A journal in memory for tests. Everything goes through JSON like in Postgres, so serialization is
/// tested here too. Writes can be held back or made to fail.
/// </summary>
internal sealed class InMemoryJournal : IEngineJournal
{
    private static readonly JsonSerializerOptions Json = EngineJson.CreateOptions();

    private readonly Lock _lock = new();
    private readonly List<(long Sequence, string Json, string? Feed)> _inputs = [];
    private readonly List<(long Sequence, string? AccountId, string? GroupId, string Json, long? InputSequence)> _events = [];
    private readonly List<(JournalSnapshot Snapshot, string StateJson)> _snapshots = [];
    private int _appends;

    /// <summary>When set, writes wait until it completes.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>When set, writes throw it.</summary>
    public Exception? Failure { get; set; }

    public int Appends => Volatile.Read(ref _appends);

    public long LastSnapshotInputSequence
    {
        get
        {
            lock (_lock)
            {
                return _snapshots.Count == 0 ? 0 : _snapshots[^1].Snapshot.InputSequence;
            }
        }
    }

    public int InputCount
    {
        get
        {
            lock (_lock)
            {
                return _inputs.Count;
            }
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<JournalSnapshot?> LoadLatestSnapshotAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_snapshots.Count == 0)
            {
                return Task.FromResult<JournalSnapshot?>(null);
            }

            var (snapshot, stateJson) = _snapshots[^1];
            return Task.FromResult<JournalSnapshot?>(snapshot with { State = JsonSerializer.Deserialize<Trading.Engine.EngineState>(stateJson, Json)! });
        }
    }

    public async IAsyncEnumerable<JournaledInput> ReadInputsAsync(long afterSequence, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<(long Sequence, string Json, string? Feed)> inputs;
        lock (_lock)
        {
            inputs = _inputs.Where(i => i.Sequence > afterSequence).ToList();
        }

        foreach (var (sequence, json, feed) in inputs)
        {
            await Task.Yield();
            yield return new JournaledInput(sequence, JsonSerializer.Deserialize<EngineInput>(json, Json)!) { Feed = feed };
        }
    }

    public Task<long> GetLastInputSequenceAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_inputs.Count == 0 ? 0 : _inputs[^1].Sequence);
        }
    }

    public Task<long> GetLastEventSequenceAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_events.Count == 0 ? 0 : _events[^1].Sequence);
        }
    }

    public async Task AppendAsync(JournalBatch batch, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _appends);
        if (Gate is { } gate)
        {
            await gate.Task;
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        lock (_lock)
        {
            // Like the primary keys in Postgres.
            var lastInput = _inputs.Count == 0 ? 0 : _inputs[^1].Sequence;
            if (batch.Inputs.Count > 0 && batch.Inputs[0].Sequence != lastInput + 1)
            {
                throw new InvalidOperationException($"Input {batch.Inputs[0].Sequence} does not follow {lastInput}.");
            }

            _inputs.AddRange(batch.Inputs.Select(i => (i.Sequence, JsonSerializer.Serialize(i.Input, Json), i.Feed)));
            _events.AddRange(batch.Events.Select(e => (e.Envelope.Sequence, EventLog.AccountIdOf(e.Envelope.Event), e.GroupId, JsonSerializer.Serialize(e.Envelope.Event, Json), e.InputSequence)));
            if (batch.Snapshot is { } snapshot)
            {
                _snapshots.RemoveAll(s => s.Snapshot.InputSequence == snapshot.InputSequence);
                _snapshots.Add((snapshot, JsonSerializer.Serialize(snapshot.State, Json)));
            }
        }
    }

    public Task<IReadOnlyList<EventEnvelope>> ReadEventsAsync(string accountId, long afterSequence, int limit, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<EventEnvelope> events = _events
                .Where(e => e.AccountId == accountId && e.Sequence > afterSequence)
                .Take(limit)
                .Select(e => new EventEnvelope(e.Sequence, JsonSerializer.Deserialize<EngineEvent>(e.Json, Json)!))
                .ToList();
            return Task.FromResult(events);
        }
    }

    public Task<IReadOnlyList<EventEnvelope>> ReadEventsBeforeAsync(string accountId, long beforeSequence, int limit, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<EventEnvelope> events = _events
                .Where(e => e.AccountId == accountId && e.Sequence < beforeSequence)
                .TakeLast(limit)
                .Select(e => new EventEnvelope(e.Sequence, JsonSerializer.Deserialize<EngineEvent>(e.Json, Json)!))
                .ToList();
            return Task.FromResult(events);
        }
    }

    public Task<IReadOnlyList<EventEnvelope>> ReadAllEventsAsync(long afterSequence, int limit, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<EventEnvelope> events = _events
                .Where(e => e.Sequence > afterSequence)
                .Take(limit)
                .Select(e => new EventEnvelope(e.Sequence, JsonSerializer.Deserialize<EngineEvent>(e.Json, Json)!))
                .ToList();
            return Task.FromResult(events);
        }
    }

    public Task<IReadOnlyList<EventEnvelope>> ReadGroupEventsAsync(IReadOnlyCollection<string> groupIds, long afterSequence, int limit, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<EventEnvelope> events = _events
                .Where(e => e.GroupId is { } groupId && groupIds.Contains(groupId) && e.Sequence > afterSequence)
                .Take(limit)
                .Select(e => new EventEnvelope(e.Sequence, JsonSerializer.Deserialize<EngineEvent>(e.Json, Json)!))
                .ToList();
            return Task.FromResult(events);
        }
    }

    public async IAsyncEnumerable<Quote> ReadQuotesAsync(DateTimeOffset since, string feed, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var journaled in ReadInputsAsync(0, cancellationToken))
        {
            if (journaled.Input is Quote quote && quote.Timestamp >= since && journaled.Feed == feed)
            {
                yield return quote;
            }
        }
    }

    public Task<string?> GetLastQuoteFeedAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var last = _inputs.LastOrDefault(i => JsonSerializer.Deserialize<EngineInput>(i.Json, Json) is Quote);
            return Task.FromResult(last.Json is null ? null : last.Feed);
        }
    }

    public Task<IReadOnlyList<RecordedEvent>> ReadPositionEventsAsync(string accountId, string positionId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<RecordedEvent> events = _events
                .Where(e => e.AccountId == accountId)
                .Select(Recorded)
                .Where(e => PositionIdOf(e.Envelope.Event) == positionId)
                .ToList();
            return Task.FromResult(events);
        }
    }

    public Task<RecordedQuote?> FindQuoteAsync(string symbol, DateTimeOffset atOrBefore, long? notAfterInput, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var quote = Quotes()
                .Where(q => q.Quote.Symbol == symbol && q.Quote.Timestamp <= atOrBefore && (notAfterInput is null || q.Sequence <= notAfterInput))
                .LastOrDefault();
            return Task.FromResult(quote);
        }
    }

    public async IAsyncEnumerable<RecordedQuote> ReadQuotesBetweenAsync(
        IReadOnlyCollection<string> symbols,
        DateTimeOffset first,
        DateTimeOffset last,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<RecordedQuote> quotes;
        lock (_lock)
        {
            quotes = Quotes().Where(q => symbols.Contains(q.Quote.Symbol) && q.Quote.Timestamp >= first && q.Quote.Timestamp <= last).ToList();
        }

        foreach (var quote in quotes)
        {
            await Task.Yield();
            yield return quote;
        }
    }

    public Task<IReadOnlyList<RecordedEvent>> ReadGroupEventsSinceAsync(
        IReadOnlyCollection<string> groupIds,
        DateTimeOffset after,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<RecordedEvent> events = _events
                .Where(e => e.GroupId is { } groupId && groupIds.Contains(groupId) && e.Sequence > afterSequence)
                .Select(Recorded)
                .Where(e => e.Envelope.Event.Timestamp > after)
                .Take(limit)
                .ToList();
            return Task.FromResult(events);
        }
    }

    public Task<IReadOnlyList<EventEnvelope>> ReadLatestGroupEventsAsync(
        IReadOnlyCollection<string> groupIds,
        string? accountId,
        int limit,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<EventEnvelope> events = _events
                .Where(e => e.GroupId is { } groupId && groupIds.Contains(groupId) && (accountId is null || e.AccountId == accountId))
                .Reverse()
                .Take(limit)
                .Select(e => new EventEnvelope(e.Sequence, JsonSerializer.Deserialize<EngineEvent>(e.Json, Json)!))
                .ToList();
            return Task.FromResult(events);
        }
    }

    public Task<IReadOnlyDictionary<string, GroupActivity>> CountGroupActivityAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var events = _events
                .Where(e => e.GroupId is not null)
                .Select(e => (GroupId: e.GroupId!, Event: JsonSerializer.Deserialize<EngineEvent>(e.Json, Json)!))
                .Where(e => e.Event.Timestamp >= since)
                .ToList();
            IReadOnlyDictionary<string, GroupActivity> activity = events
                .GroupBy(e => e.GroupId, StringComparer.Ordinal)
                .Where(g => g.Any(e => e.Event is PositionOpened or InputRejected))
                .ToDictionary(
                    g => g.Key,
                    g => new GroupActivity(
                        g.Count(e => e.Event is PositionOpened),
                        g.Count(e => e.Event is InputRejected),
                        g.Count(e => e.Event is InputRejected { Reason: RejectReason.StalePrice })),
                    StringComparer.Ordinal);
            return Task.FromResult(activity);
        }
    }

    public Task<JournalStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(new JournalStats(
                null,
                [.. _snapshots.Select(s => new SnapshotInfo(s.Snapshot.InputSequence, null)).OrderByDescending(s => s.InputSequence)]));
        }
    }

    private static RecordedEvent Recorded((long Sequence, string? AccountId, string? GroupId, string Json, long? InputSequence) e) =>
        new(new EventEnvelope(e.Sequence, JsonSerializer.Deserialize<EngineEvent>(e.Json, Json)!), e.InputSequence);

    // As the index in Postgres finds them: by position id, or by order id for the order it came from.
    private static string? PositionIdOf(EngineEvent engineEvent)
    {
        var json = JsonSerializer.SerializeToElement(engineEvent, Json);
        return json.TryGetProperty("positionId", out var position) ? position.GetString()
            : json.TryGetProperty("orderId", out var order) ? order.GetString()
            : null;
    }

    // Caller holds the lock.
    private IEnumerable<RecordedQuote> Quotes() =>
        _inputs
            .Select(i => (i.Sequence, Input: JsonSerializer.Deserialize<EngineInput>(i.Json, Json)!, i.Feed))
            .Where(i => i.Input is Quote)
            .Select(i => new RecordedQuote(i.Sequence, (Quote)i.Input, i.Feed));

    /// <summary>A copy of what is stored right now, as a crashed service would leave it.</summary>
    public InMemoryJournal Clone()
    {
        var copy = new InMemoryJournal();
        lock (_lock)
        {
            copy._inputs.AddRange(_inputs);
            copy._events.AddRange(_events);
            copy._snapshots.AddRange(_snapshots);
        }

        return copy;
    }

    public void RemoveLastEvent()
    {
        lock (_lock)
        {
            _events.RemoveAt(_events.Count - 1);
        }
    }
}
