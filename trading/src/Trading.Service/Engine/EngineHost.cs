using System.Threading.Channels;

using Microsoft.Extensions.Options;

using Trading.Engine;
using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Service.Persistence;

namespace Trading.Service.Engine;

/// <summary>
/// Owns the engine and its journal.
/// <para>
/// The engine is not thread-safe, so every input and query goes through one queue and runs one at a
/// time in a single loop. Each input gets a gapless sequence number and the current time, never earlier
/// than the previous input.
/// </para>
/// <para>
/// Inputs are applied at once and written to the journal in batches by a separate writer. Nothing that
/// depends on an input (the command's result, its events, a query that saw it) is released until the
/// batch holding the input is stored, so a restart never undoes anything a client has seen. If the
/// journal cannot be written, the service stops.
/// </para>
/// </summary>
internal sealed partial class EngineHost : BackgroundService
{
    private const int WriteAttempts = 3;

    private readonly Channel<EngineWork> _queue =
        Channel.CreateUnbounded<EngineWork>(new UnboundedChannelOptions { SingleReader = true });

    // Holds at most one batch while another is written, so batches grow under load.
    private readonly Channel<Batch> _toWrite =
        Channel.CreateBounded<Batch>(new BoundedChannelOptions(1) { SingleReader = true, SingleWriter = true });

    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly EngineConfiguration _configuration;
    private readonly string _fingerprint;
    private readonly IEngineJournal _journal;
    private readonly EventLog _eventLog;
    private readonly TimeProvider _time;
    private readonly JournalOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly EngineMetrics _metrics;
    private readonly ILogger<EngineHost> _logger;

    // Engine loop only.
    private TradingEngine _engine;
    private long _inputSequence;
    private long _eventSequence;
    private long _inputsSinceSnapshot;
    private DateTimeOffset _lastTimestamp = DateTimeOffset.MinValue;
    private Batch _pending = new();
    private Batch? _lastHandedOff;

    private long _quotesApplied;
    private int _queued;
    private volatile Exception? _journalFailure;

    public EngineHost(
        EngineConfiguration configuration,
        IEngineJournal journal,
        EventLog eventLog,
        TimeProvider time,
        IOptions<JournalOptions> options,
        IHostApplicationLifetime lifetime,
        EngineMetrics metrics,
        ILogger<EngineHost> logger)
    {
        _configuration = configuration;
        _fingerprint = ConfigurationFingerprint.Of(configuration);
        _engine = new TradingEngine(configuration);
        _journal = journal;
        _eventLog = eventLog;
        _time = time;
        _options = options.Value;
        _lifetime = lifetime;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>Completes when the journal has been replayed and the engine accepts inputs.</summary>
    public Task Ready => _ready.Task;

    /// <summary>True once the journal could not be written. The service is then stopping.</summary>
    public bool JournalFailed => _journalFailure is not null;

    /// <summary>Number of live prices the engine has applied.</summary>
    public long QuotesApplied => Interlocked.Read(ref _quotesApplied);

    /// <summary>Prices, commands and queries waiting for the engine loop now.</summary>
    public int QueueLength => Math.Max(0, Volatile.Read(ref _queued));

    /// <summary>Applies a live price. The feed is stored with it, so the charts can tell real prices from made-up ones.</summary>
    public void EnqueueQuote(string feed, string symbol, decimal bid, decimal ask) =>
        Write(new QuoteWork(feed, symbol, bid, ask));

    /// <summary>Applies a command and returns its events once they are stored. The function receives the timestamp to use.</summary>
    public Task<IReadOnlyList<EventEnvelope>> SendAsync(Func<DateTimeOffset, EngineInput> createInput, CancellationToken cancellationToken = default)
    {
        var work = new CommandWork(createInput);
        Enqueue(work);
        return work.Completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Runs a read-only query between inputs. The result is released once everything it saw is stored.</summary>
    public Task<T> QueryAsync<T>(Func<TradingEngine, T> query, CancellationToken cancellationToken = default)
    {
        var work = new QueryWork<T>(query);
        Enqueue(work);
        return work.Completion.Task.WaitAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverAsync(stoppingToken);
            _ready.TrySetResult();
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
            LogRecoveryFailed(_logger, exception);
            _queue.Writer.TryComplete();
            while (_queue.Reader.TryRead(out var work))
            {
                Interlocked.Decrement(ref _queued);
                work.Fail(exception);
            }

            throw;
        }

        var writer = Task.Run(WriteBatchesAsync, CancellationToken.None);
        var engineFailed = false;
        try
        {
            await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                if (_journalFailure is { } failure)
                {
                    work.Fail(failure);
                    continue;
                }

                _metrics.QueueSampled(Interlocked.Decrement(ref _queued));
                work.Run(this);
                HandOffPendingBatch();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch
        {
            engineFailed = true;
            throw;
        }
        finally
        {
            // A snapshot after an engine failure could hold half-applied state, so none is taken then.
            await ShutDownAsync(writer, takeSnapshot: !engineFailed);
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        var started = _time.GetTimestamp();
        await _journal.InitializeAsync(cancellationToken);

        var snapshot = await _journal.LoadLatestSnapshotAsync(cancellationToken);
        if (snapshot is null)
        {
            LogNoSnapshot(_logger);
        }
        else
        {
            _engine = TradingEngine.FromState(_configuration, snapshot.State);
            _inputSequence = snapshot.InputSequence;
            _eventSequence = snapshot.EventSequence;
            _lastTimestamp = snapshot.State.Clock;
        }

        // Inputs after a snapshot with another configuration were applied with the old one. The new configuration is
        // only accepted if it gives exactly the events they gave then, for example when it only adds instruments.
        var stored = snapshot is not null && snapshot.ConfigurationFingerprint != _fingerprint ? new StoredEvents(_journal, _eventSequence) : null;
        var replayed = 0L;
        await foreach (var (sequence, input) in _journal.ReadInputsAsync(_inputSequence, cancellationToken))
        {
            if (sequence != _inputSequence + 1)
            {
                throw new InvalidOperationException($"The journal has a gap: input {_inputSequence + 1} is missing before {sequence}.");
            }

            var events = _engine.Apply(input);
            if (stored is not null && !await stored.MatchAsync(events, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"The configuration has changed since the latest snapshot, and input {sequence} after it gives other events with the new configuration. " +
                    "Start once with the previous configuration so a new snapshot is taken, then change the configuration.");
            }

            _eventSequence += events.Count;
            _inputSequence = sequence;
            if (input.Timestamp > _lastTimestamp)
            {
                _lastTimestamp = input.Timestamp;
            }

            replayed++;
        }

        var storedEvents = await _journal.GetLastEventSequenceAsync(cancellationToken);
        if (storedEvents != _eventSequence)
        {
            throw new InvalidOperationException(
                $"Replaying the journal gave events up to {_eventSequence}, but the journal has events up to {storedEvents}. " +
                "Either the engine is not deterministic or the journal is damaged.");
        }

        if (stored is not null)
        {
            LogConfigurationChanged(_logger, replayed);
        }

        // Every start begins with a snapshot that records the current configuration.
        await _journal.AppendAsync(new JournalBatch([], [], CreateSnapshot()), cancellationToken);
        _metrics.Started(_time.GetUtcNow(), _time.GetElapsedTime(started), replayed, stored is not null);
        LogRecovered(_logger, _inputSequence, replayed);
    }

    private async Task WriteBatchesAsync()
    {
        await foreach (var batch in _toWrite.Reader.ReadAllAsync())
        {
            if (_journalFailure is { } earlierFailure)
            {
                batch.Fail(earlierFailure);
                continue;
            }

            try
            {
                var saving = _time.GetTimestamp();
                await AppendWithRetryAsync(batch.ToJournalBatch());
                _metrics.BatchSaved(_time.GetElapsedTime(saving));
            }
            catch (Exception exception)
            {
                _journalFailure = exception;
                LogJournalFailed(_logger, exception);
                batch.Fail(exception);
                _lifetime.StopApplication();
                continue;
            }

            batch.Release();

            // Lets the loop hand off the batch that built up in the meantime.
            Write(FlushWork.Instance);
        }
    }

    private async Task AppendWithRetryAsync(JournalBatch batch)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _journal.AppendAsync(batch, CancellationToken.None);
                return;
            }
            catch (Exception exception) when (attempt < WriteAttempts)
            {
                LogWriteRetry(_logger, attempt, exception);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));

                // A call that failed can still have committed.
                if (batch.LastInputSequence > 0 && await _journal.GetLastInputSequenceAsync(CancellationToken.None) >= batch.LastInputSequence)
                {
                    return;
                }
            }
        }
    }

    private async Task ShutDownAsync(Task writer, bool takeSnapshot)
    {
        // Work that never ran is cancelled. Its inputs were never applied.
        _queue.Writer.TryComplete();
        CancelQueuedWork();

        if (takeSnapshot && _journalFailure is null)
        {
            _pending.Snapshot = CreateSnapshot();
        }

        if (!_pending.IsEmpty)
        {
            await _toWrite.Writer.WriteAsync(_pending);
        }

        _toWrite.Writer.TryComplete();
        await writer;
    }

    private void CancelQueuedWork()
    {
        while (_queue.Reader.TryRead(out var work))
        {
            Interlocked.Decrement(ref _queued);
            work.Cancel();
        }
    }

    private void Enqueue(EngineWork work)
    {
        if (!Write(work))
        {
            throw new InvalidOperationException("The engine has stopped.");
        }
    }

    // Counts what waits, since the queue cannot count itself with one reader.
    private bool Write(EngineWork work)
    {
        Interlocked.Increment(ref _queued);
        if (_queue.Writer.TryWrite(work))
        {
            return true;
        }

        Interlocked.Decrement(ref _queued);
        return false;
    }

    // Engine loop only.
    private List<EventEnvelope> Apply(EngineInput input, string? feed = null)
    {
        var events = _engine.Apply(input);
        _pending.Inputs.Add(new JournaledInput(++_inputSequence, input) { Feed = feed });
        _metrics.InputApplied(input, events);

        var envelopes = new List<EventEnvelope>(events.Count);
        foreach (var engineEvent in events)
        {
            var envelope = new EventEnvelope(++_eventSequence, engineEvent);
            envelopes.Add(envelope);

            // The group never changes, so it is taken once, here, where the engine is at hand.
            var groupId = EventLog.AccountIdOf(engineEvent) is { } accountId ? _engine.GetGroupId(accountId) : null;
            _pending.Events.Add(new JournaledEvent(envelope, groupId) { InputSequence = _inputSequence });
            if (engineEvent is InputRejected { Input: Quote quote } rejected)
            {
                LogQuoteRejected(_logger, quote.Symbol, quote.Bid, quote.Ask, rejected.Reason);
            }
        }

        if (envelopes.Count > 0)
        {
            _pending.WhenStored(() => _eventLog.Publish(envelopes), static _ => { });
        }

        if (++_inputsSinceSnapshot >= _options.SnapshotInterval)
        {
            _pending.Snapshot = CreateSnapshot();
        }

        return envelopes;
    }

    // Engine loop only.
    private JournalSnapshot CreateSnapshot()
    {
        _inputsSinceSnapshot = 0;
        return new JournalSnapshot(_inputSequence, _eventSequence, _fingerprint, _engine.ExportState());
    }

    // Engine loop only.
    private void HandOffPendingBatch()
    {
        if (_pending.IsEmpty || !_toWrite.Writer.TryWrite(_pending))
        {
            return;
        }

        _lastHandedOff = _pending;
        _pending = new Batch();
    }

    // Engine loop only. Runs the action once every input applied so far is stored.
    private void WhenAppliedInputsStored(Action onStored, Action<Exception> onFailed)
    {
        var batch = _pending.Inputs.Count > 0 ? _pending : _lastHandedOff;
        if (batch is null)
        {
            onStored();
        }
        else
        {
            batch.WhenStored(onStored, onFailed);
        }
    }

    // Engine loop only. The journal stores microseconds, so finer precision would make a replay see other times.
    private DateTimeOffset NextTimestamp()
    {
        var ticks = _time.GetUtcNow().UtcTicks;
        var now = new DateTimeOffset(ticks - (ticks % TimeSpan.TicksPerMicrosecond), TimeSpan.Zero);
        if (now < _lastTimestamp)
        {
            now = _lastTimestamp;
        }

        _lastTimestamp = now;
        return now;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Engine recovered at input {InputSequence} after replaying {Replayed} inputs")]
    private static partial void LogRecovered(ILogger logger, long inputSequence, long replayed);

    [LoggerMessage(Level = LogLevel.Information, Message = "The configuration changed since the latest snapshot. The {Replayed} inputs after it gave the same events with the new configuration.")]
    private static partial void LogConfigurationChanged(ILogger logger, long replayed);

    [LoggerMessage(Level = LogLevel.Information, Message = "No snapshot found, replaying the whole journal")]
    private static partial void LogNoSnapshot(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Engine recovery failed. The service cannot start.")]
    private static partial void LogRecoveryFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Journal write failed on attempt {Attempt}, retrying")]
    private static partial void LogWriteRetry(ILogger logger, int attempt, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The journal could not be written. Stopping the service to protect account state.")]
    private static partial void LogJournalFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Price rejected for {Symbol} ({Bid}/{Ask}): {Reason}")]
    private static partial void LogQuoteRejected(ILogger logger, string symbol, decimal bid, decimal ask, RejectReason reason);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The engine failed. Stopping the service to protect account state.")]
    private static partial void LogEngineFailure(ILogger logger, Exception exception);

    /// <summary>The stored events after a snapshot, read a page at a time, to compare a replay with.</summary>
    private sealed class StoredEvents(IEngineJournal journal, long afterSequence)
    {
        private const int PageSize = 1_000;
        private static readonly System.Text.Json.JsonSerializerOptions Options = Trading.Service.Json.EngineJson.CreateOptions();

        private readonly Queue<EventEnvelope> _page = new();
        private long _after = afterSequence;

        /// <summary>True if the replayed events are the next stored ones, in order and alike in every field.</summary>
        public async Task<bool> MatchAsync(IReadOnlyList<EngineEvent> replayed, CancellationToken cancellationToken)
        {
            foreach (var engineEvent in replayed)
            {
                if (_page.Count == 0)
                {
                    foreach (var envelope in await journal.ReadAllEventsAsync(_after, PageSize, cancellationToken))
                    {
                        _page.Enqueue(envelope);
                    }
                }

                if (!_page.TryDequeue(out var next))
                {
                    return false;
                }

                _after = next.Sequence;
                if (System.Text.Json.JsonSerializer.Serialize(next.Event, Options) != System.Text.Json.JsonSerializer.Serialize(engineEvent, Options))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Inputs, events and an optional snapshot that are written together, and what waits for them.</summary>
    private sealed class Batch
    {
        private readonly Lock _lock = new();
        private readonly List<(Action OnStored, Action<Exception> OnFailed)> _waiting = [];
        private bool _stored;
        private Exception? _failure;

        public List<JournaledInput> Inputs { get; } = [];

        public List<JournaledEvent> Events { get; } = [];

        public JournalSnapshot? Snapshot { get; set; }

        public bool IsEmpty => Inputs.Count == 0 && Snapshot is null;

        public JournalBatch ToJournalBatch() => new(Inputs, Events, Snapshot);

        /// <summary>Runs the action once the batch is stored, at once if it already is.</summary>
        public void WhenStored(Action onStored, Action<Exception> onFailed)
        {
            lock (_lock)
            {
                if (!_stored && _failure is null)
                {
                    _waiting.Add((onStored, onFailed));
                    return;
                }
            }

            if (_failure is { } failure)
            {
                onFailed(failure);
            }
            else
            {
                onStored();
            }
        }

        public void Release()
        {
            List<(Action OnStored, Action<Exception> OnFailed)> waiting;
            lock (_lock)
            {
                _stored = true;
                waiting = [.. _waiting];
                _waiting.Clear();
            }

            foreach (var (onStored, _) in waiting)
            {
                onStored();
            }
        }

        public void Fail(Exception exception)
        {
            List<(Action OnStored, Action<Exception> OnFailed)> waiting;
            lock (_lock)
            {
                _failure = exception;
                waiting = [.. _waiting];
                _waiting.Clear();
            }

            foreach (var (_, onFailed) in waiting)
            {
                onFailed(exception);
            }
        }
    }

    private abstract class EngineWork
    {
        public abstract void Run(EngineHost host);

        public virtual void Cancel()
        {
        }

        public virtual void Fail(Exception exception)
        {
        }
    }

    /// <summary>Does nothing. Running it lets the loop hand off a waiting batch.</summary>
    private sealed class FlushWork : EngineWork
    {
        public static readonly FlushWork Instance = new();

        public override void Run(EngineHost host)
        {
        }
    }

    private sealed class QuoteWork(string feed, string symbol, decimal bid, decimal ask) : EngineWork
    {
        public override void Run(EngineHost host)
        {
            try
            {
                host.Apply(new Quote(host.NextTimestamp(), symbol, bid, ask), feed);
                Interlocked.Increment(ref host._quotesApplied);
            }
            catch (Exception exception)
            {
                LogEngineFailure(host._logger, exception);
                throw;
            }
        }
    }

    private sealed class CommandWork(Func<DateTimeOffset, EngineInput> createInput) : EngineWork
    {
        public TaskCompletionSource<IReadOnlyList<EventEnvelope>> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // A failing command means the engine state can no longer be trusted, so the failure stops the loop.
        public override void Run(EngineHost host)
        {
            IReadOnlyList<EventEnvelope> envelopes;
            try
            {
                envelopes = host.Apply(createInput(host.NextTimestamp()));
            }
            catch (Exception exception)
            {
                Completion.TrySetException(exception);
                LogEngineFailure(host._logger, exception);
                throw;
            }

            host._pending.WhenStored(() => Completion.TrySetResult(envelopes), e => Completion.TrySetException(e));
        }

        public override void Cancel() => Completion.TrySetCanceled();

        public override void Fail(Exception exception) => Completion.TrySetException(exception);
    }

    // Queries do not change state, so a failing query only fails its caller.
    private sealed class QueryWork<T>(Func<TradingEngine, T> query) : EngineWork
    {
        public TaskCompletionSource<T> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Run(EngineHost host)
        {
            T result;
            try
            {
                result = query(host._engine);
            }
            catch (Exception exception)
            {
                Completion.TrySetException(exception);
                return;
            }

            host.WhenAppliedInputsStored(() => Completion.TrySetResult(result), e => Completion.TrySetException(e));
        }

        public override void Cancel() => Completion.TrySetCanceled();

        public override void Fail(Exception exception) => Completion.TrySetException(exception);
    }
}
