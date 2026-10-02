using System.Threading.Channels;

using Trading.Engine;
using Trading.Engine.Events;
using Trading.Engine.Inputs;

namespace Trading.Service.Engine;

/// <summary>
/// Owns the engine. The engine is not thread-safe, so every input and query goes through one queue
/// and runs one at a time in a single loop. Each input is stamped with the current time here,
/// never earlier than the previous input.
/// </summary>
internal sealed partial class EngineHost : BackgroundService
{
    private readonly Channel<EngineWork> _queue =
        Channel.CreateUnbounded<EngineWork>(new UnboundedChannelOptions { SingleReader = true });

    private readonly TradingEngine _engine;
    private readonly EventLog _eventLog;
    private readonly TimeProvider _time;
    private readonly ILogger<EngineHost> _logger;
    private DateTimeOffset _lastTimestamp = DateTimeOffset.MinValue;
    private long _quotesApplied;

    public EngineHost(EngineConfiguration configuration, EventLog eventLog, TimeProvider time, ILogger<EngineHost> logger)
    {
        _engine = new TradingEngine(configuration);
        _eventLog = eventLog;
        _time = time;
        _logger = logger;
    }

    /// <summary>Number of prices the engine has applied so far.</summary>
    public long QuotesApplied => Interlocked.Read(ref _quotesApplied);

    public void EnqueueQuote(string symbol, decimal bid, decimal ask) =>
        Enqueue(new QuoteWork(symbol, bid, ask));

    /// <summary>Applies a command and returns its events. The function receives the timestamp to use.</summary>
    public Task<IReadOnlyList<EventEnvelope>> SendAsync(Func<DateTimeOffset, EngineInput> createInput, CancellationToken cancellationToken = default)
    {
        var work = new CommandWork(createInput);
        Enqueue(work);
        return work.Completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Runs a read-only query against the engine between inputs.</summary>
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
            await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                work.Run(this);
            }
        }
        finally
        {
            _queue.Writer.TryComplete();
            while (_queue.Reader.TryRead(out var work))
            {
                work.Cancel();
            }
        }
    }

    private void Enqueue(EngineWork work)
    {
        if (!_queue.Writer.TryWrite(work))
        {
            throw new InvalidOperationException("The engine has stopped.");
        }
    }

    // Engine loop only.
    private IReadOnlyList<EventEnvelope> Apply(EngineInput input)
    {
        var events = _engine.Apply(input);
        foreach (var rejected in events.OfType<InputRejected>())
        {
            if (rejected.Input is Quote quote)
            {
                LogQuoteRejected(_logger, quote.Symbol, quote.Bid, quote.Ask, rejected.Reason);
            }
        }

        return _eventLog.Append(events);
    }

    // Engine loop only. The engine rejects inputs that go back in time, so the clock is never allowed to.
    private DateTimeOffset NextTimestamp()
    {
        var now = _time.GetUtcNow();
        if (now < _lastTimestamp)
        {
            now = _lastTimestamp;
        }

        _lastTimestamp = now;
        return now;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Price rejected for {Symbol} ({Bid}/{Ask}): {Reason}")]
    private static partial void LogQuoteRejected(ILogger logger, string symbol, decimal bid, decimal ask, RejectReason reason);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The engine failed. Stopping the service to protect account state.")]
    private static partial void LogEngineFailure(ILogger logger, Exception exception);

    private abstract class EngineWork
    {
        public abstract void Run(EngineHost host);

        public abstract void Cancel();
    }

    private sealed class QuoteWork(string symbol, decimal bid, decimal ask) : EngineWork
    {
        public override void Run(EngineHost host)
        {
            try
            {
                host.Apply(new Quote(host.NextTimestamp(), symbol, bid, ask));
                Interlocked.Increment(ref host._quotesApplied);
            }
            catch (Exception exception)
            {
                LogEngineFailure(host._logger, exception);
                throw;
            }
        }

        public override void Cancel()
        {
        }
    }

    private sealed class CommandWork(Func<DateTimeOffset, EngineInput> createInput) : EngineWork
    {
        public TaskCompletionSource<IReadOnlyList<EventEnvelope>> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // A failing command means the engine state can no longer be trusted, so the failure stops the loop.
        public override void Run(EngineHost host)
        {
            try
            {
                Completion.TrySetResult(host.Apply(createInput(host.NextTimestamp())));
            }
            catch (Exception exception)
            {
                Completion.TrySetException(exception);
                LogEngineFailure(host._logger, exception);
                throw;
            }
        }

        public override void Cancel() => Completion.TrySetCanceled();
    }

    // Queries do not change state, so a failing query only fails its caller.
    private sealed class QueryWork<T>(Func<TradingEngine, T> query) : EngineWork
    {
        public TaskCompletionSource<T> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Run(EngineHost host)
        {
            try
            {
                Completion.TrySetResult(query(host._engine));
            }
            catch (Exception exception)
            {
                Completion.TrySetException(exception);
            }
        }

        public override void Cancel() => Completion.TrySetCanceled();
    }
}
