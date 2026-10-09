using System.Globalization;

using Trading.Engine;
using Trading.Service.Engine;
using Trading.Service.Feeds;

namespace Trading.Service.Staff;

/// <summary>
/// Watches the price feed for the staff panel (ADR 0057): which symbols have had no price for a minute while their
/// market is open, and whether the whole feed is silent. Changes are written to the platform's log, a whole silent feed
/// as one entry rather than one per symbol. Also logs each start of the service.
/// </summary>
internal sealed partial class PlatformWatcher(
    EngineHost engine,
    EngineConfiguration configuration,
    EngineMetrics metrics,
    IPriceFeed feed,
    PlatformEvents events,
    TimeProvider time,
    ILogger<PlatformWatcher> logger) : BackgroundService
{
    /// <summary>A symbol is silent when its market is open and its last price is older than this.</summary>
    public static readonly TimeSpan SilentAfter = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _checking = new(1, 1);
    private readonly DateTimeOffset _startedAt = time.GetUtcNow();
    private Dictionary<string, DateTimeOffset?> _silent = new(StringComparer.Ordinal);
    private DateTimeOffset? _feedSilentSince;
    private DateTimeOffset? _liveSince;

    /// <summary>The symbols that are silent now, with their last price, or null when they have had none.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset?> SilentSymbols
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, DateTimeOffset?>(_silent, StringComparer.Ordinal);
            }
        }
    }

    /// <summary>Since when every open market has been silent, or null when prices come.</summary>
    public DateTimeOffset? FeedSilentSince
    {
        get
        {
            lock (_lock)
            {
                return _feedSilentSince;
            }
        }
    }

    /// <summary>Since when prices have come without the whole feed falling silent, or null before the first price.</summary>
    public DateTimeOffset? LiveSince
    {
        get
        {
            lock (_lock)
            {
                return _liveSince;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            await engine.Ready.WaitAsync(stoppingToken);
            if (metrics.Start is { } start)
            {
                await events.RecordAsync(
                    PlatformEventKind.ServiceStarted,
                    null,
                    null,
                    new()
                    {
                        ["replayed"] = start.Replayed.ToString(CultureInfo.InvariantCulture),
                        ["milliseconds"] = ((long)start.Took.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
                    });
            }

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await CheckAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    LogCheckFailed(logger, exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    public override void Dispose()
    {
        _checking.Dispose();
        base.Dispose();
    }

    /// <summary>Looks at every symbol's last price now. Called every few seconds, and by tests. One check at a time.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        await _checking.WaitAsync(cancellationToken);
        try
        {
            await CheckOnceAsync(cancellationToken);
        }
        finally
        {
            _checking.Release();
        }
    }

    private async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var latest = (await engine.QueryAsync(e => e.GetLatestQuotes(), cancellationToken)).ToDictionary(q => q.Symbol, q => q.Timestamp, StringComparer.Ordinal);
        var open = configuration.Instruments.Where(i => i.TradingHours?.IsOpen(now) ?? true).Select(i => i.Symbol).ToList();
        var silent = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
        foreach (var symbol in open)
        {
            var last = latest.TryGetValue(symbol, out var at) ? at : (DateTimeOffset?)null;
            if (now - (last ?? _startedAt) > SilentAfter)
            {
                silent[symbol] = last;
            }
        }

        var lastPrice = latest.Count == 0 ? (DateTimeOffset?)null : latest.Values.Max();
        var feedSilent = open.Count > 0 && silent.Count == open.Count;
        List<(PlatformEventKind Kind, Dictionary<string, string> Detail)> changes = [];
        lock (_lock)
        {
            if (feedSilent && _feedSilentSince is null)
            {
                _feedSilentSince = lastPrice ?? _startedAt;
                _liveSince = null;
                changes.Add((PlatformEventKind.FeedSilent, new() { ["feed"] = feed.Name, ["lastPrice"] = Iso(lastPrice) }));
            }
            else if (!feedSilent && _feedSilentSince is { } since)
            {
                _feedSilentSince = null;
                changes.Add((PlatformEventKind.FeedBack, new() { ["feed"] = feed.Name, ["silentSeconds"] = Seconds(now - since) }));

                // The symbols that stay silent although the feed is back.
                foreach (var (symbol, last) in silent)
                {
                    changes.Add((PlatformEventKind.SymbolSilent, new() { ["symbol"] = symbol, ["feed"] = feed.Name, ["lastPrice"] = Iso(last) }));
                }
            }
            else if (!feedSilent)
            {
                foreach (var (symbol, last) in silent.Where(s => !_silent.ContainsKey(s.Key)))
                {
                    changes.Add((PlatformEventKind.SymbolSilent, new() { ["symbol"] = symbol, ["feed"] = feed.Name, ["lastPrice"] = Iso(last) }));
                }

                foreach (var (symbol, last) in _silent.Where(s => !silent.ContainsKey(s.Key)))
                {
                    changes.Add((PlatformEventKind.SymbolBack, new() { ["symbol"] = symbol, ["feed"] = feed.Name, ["silentSeconds"] = Seconds(now - (last ?? _startedAt)) }));
                }
            }

            if (!feedSilent && lastPrice is not null && _liveSince is null)
            {
                _liveSince = now;
            }

            _silent = silent;
        }

        foreach (var (kind, detail) in changes)
        {
            await events.RecordAsync(kind, null, null, detail);
        }
    }

    private static string Iso(DateTimeOffset? at) => at?.ToString("O", CultureInfo.InvariantCulture) ?? "";

    private static string Seconds(TimeSpan span) => ((long)span.TotalSeconds).ToString(CultureInfo.InvariantCulture);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not check the price feed for the staff panel")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);
}
