using System.Runtime.CompilerServices;
using System.Text.Json;

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
    private readonly List<(long Sequence, string Json)> _inputs = [];
    private readonly List<(long Sequence, string? AccountId, string? GroupId, string Json)> _events = [];
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
        List<(long Sequence, string Json)> inputs;
        lock (_lock)
        {
            inputs = _inputs.Where(i => i.Sequence > afterSequence).ToList();
        }

        foreach (var (sequence, json) in inputs)
        {
            await Task.Yield();
            yield return new JournaledInput(sequence, JsonSerializer.Deserialize<EngineInput>(json, Json)!);
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

            _inputs.AddRange(batch.Inputs.Select(i => (i.Sequence, JsonSerializer.Serialize(i.Input, Json))));
            _events.AddRange(batch.Events.Select(e => (e.Envelope.Sequence, EventLog.AccountIdOf(e.Envelope.Event), e.GroupId, JsonSerializer.Serialize(e.Envelope.Event, Json))));
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

    public async IAsyncEnumerable<Quote> ReadQuotesAsync(DateTimeOffset since, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var (_, input) in ReadInputsAsync(0, cancellationToken))
        {
            if (input is Quote quote && quote.Timestamp >= since)
            {
                yield return quote;
            }
        }
    }

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
