using System.Globalization;
using System.Threading.Channels;

using Microsoft.Extensions.Options;

using Trading.Service.Feeds;
using Trading.Service.Persistence;
using Trading.Service.Staff;

namespace Trading.Service.Candles;

/// <summary>
/// The charts' history (ADR 0048, ADR 0051, ADR 0056). The charts show only the current feed's prices: its history, loaded
/// when the service switches to the feed or the charts are set to reach further back, and minute bars made from its live
/// prices, stored as they finish. At start the stored bars fill the charts and the prices after them are replayed from the
/// journal. A whole minute or more without any price, because the service or the feed was down, is filled from the feed's
/// history once prices come again. Our staff can ask for a gap that could not be filled to be tried again, and for the
/// whole history to be loaded again (ADR 0057).
/// </summary>
internal sealed partial class ChartHistory(
    IPriceFeed feed,
    IChartStore store,
    IEngineJournal journal,
    CandleStore candles,
    PlatformEvents events,
    TimeProvider time,
    IOptions<ChartOptions> options,
    ILogger<ChartHistory> logger) : IDisposable
{
    // Without stored bars, for example when the history could not be loaded, the latest day of prices is replayed.
    private static readonly TimeSpan ReplayWithoutBars = TimeSpan.FromDays(1);

    // Lets a price stamped just before a minute ended reach its bar before the bar is stored.
    private static readonly TimeSpan RecordDelay = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Minute = CandleStore.Duration(Timeframe.M1);

    // A vendor that refuses for a moment, for example with too many calls, is asked again after these waits.
    private static readonly TimeSpan[] GapRetries = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    // Made-up prices continue from the last ones after a restart, so there is no true history to fill a gap with.
    private readonly bool _fillsGaps = feed is not IContinuablePriceFeed;

    private readonly Lock _gate = new();
    // One signal each time prices start to wait for a gap or for our staff's request.
    private readonly Channel<bool> _gapFound = Channel.CreateUnbounded<bool>();

    // Storing finished bars and building the charts again from the store never run at the same time.
    private readonly SemaphoreSlim _storing = new(1, 1);

    // The prices, gaps and requests that wait while a gap is filled, in the order they came, or null when nothing waits.
    private List<Waiting>? _waiting;

    // When the latest price came, or where the stored bars end.
    private DateTimeOffset? _lastPrice;

    // Bars before this are stored. Used by the recorder, and by a rebuild while the recorder cannot run.
    private DateTimeOffset _recordedUntil;

    /// <summary>Whether the feed has a true history, so gaps are filled and the history can be loaded again.</summary>
    public bool HasHistory => _fillsGaps;

    /// <summary>Fills the charts. Loads the feed's history first when the service has switched to the feed.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var from = Reach(now);
        await store.PruneAsync(from, cancellationToken);

        // The last price is from another feed, or there is none: the service has switched to this feed.
        if (await journal.GetLastQuoteFeedAsync(cancellationToken) != feed.Name)
        {
            await store.ForgetHistoryAsync(feed.Name, cancellationToken);
        }

        // Loaded again when the history is set to reach further back than it does.
        if (await store.GetHistoryReachAsync(feed.Name, cancellationToken) is not { } reach || reach > from)
        {
            await LoadFeedHistoryAsync(from, now, cancellationToken);
        }

        DateTimeOffset? storedUntil = null;
        var bars = 0;
        await foreach (var bar in store.ReadAsync(feed.Name, from, cancellationToken))
        {
            candles.AddBar(bar);
            var end = bar.Candle.Time + CandleStore.Duration(bar.Resolution);
            storedUntil = storedUntil > end ? storedUntil : end;
            bars++;
        }

        // The prices after the stored bars, for example of the minute that had not finished when the service stopped.
        // A gap before them or between them, from a stop or a feed that was down, is filled like a gap in live prices.
        var replayFrom = CandleStore.BarStart(Timeframe.M1, storedUntil ?? now - ReplayWithoutBars);
        _lastPrice = storedUntil - TimeSpan.FromTicks(1);
        var prices = 0;
        await foreach (var quote in journal.ReadQuotesAsync(replayFrom, feed.Name, cancellationToken))
        {
            AddPrice(quote.Symbol, quote.Bid, quote.Timestamp);
            prices++;
        }

        _recordedUntil = replayFrom;
        LogLoaded(logger, bars, prices, feed.Name);
    }

    /// <summary>
    /// Adds a price to the charts. After a whole minute or more without any price, the prices wait until the gap is
    /// filled from the feed's history, so the bars still come in time order.
    /// </summary>
    public void AddPrice(string symbol, decimal bid, DateTimeOffset at)
    {
        lock (_gate)
        {
            if (_fillsGaps && _lastPrice is { } last)
            {
                var from = Max(CandleStore.BarStart(Timeframe.M1, last) + Minute, Today(at) - options.Value.History);
                var until = CandleStore.BarStart(Timeframe.M1, at);
                if (from < until)
                {
                    Enqueue(new WaitingGap(new ChartGap(Guid.CreateVersion7(at), feed.Name, from, until, at, ChartGapState.Filling, 0, null, 0, null)));
                }
            }

            _lastPrice = at;
            if (_waiting is null)
            {
                candles.Add(symbol, bid, at);
            }
            else
            {
                _waiting.Add(new WaitingPrice(symbol, bid, at));
            }
        }
    }

    /// <summary>
    /// Asks for a gap that the feed could not fill, or had no prices for, to be filled again from the feed's history.
    /// False when there is no such gap of this feed or the feed has no true history.
    /// </summary>
    public async Task<bool> RetryGapAsync(Guid gapId, CancellationToken cancellationToken)
    {
        if (!_fillsGaps
            || (await store.ListGapsAsync(feed.Name, int.MaxValue, cancellationToken)).FirstOrDefault(g => g.Id == gapId) is not { } gap
            || gap.State is not (ChartGapState.NotFilled or ChartGapState.Empty))
        {
            return false;
        }

        lock (_gate)
        {
            if (_waiting?.Any(w => w is WaitingRetry retry && retry.Gap.Id == gapId) != true)
            {
                Enqueue(new WaitingRetry(gap));
            }
        }

        return true;
    }

    /// <summary>
    /// Asks for the feed's whole history to be loaded again, replacing its stored bars, which also fills the gaps that
    /// could not be filled. False when the feed has no true history.
    /// </summary>
    public bool RequestReload(string staffEmail)
    {
        if (!_fillsGaps)
        {
            return false;
        }

        lock (_gate)
        {
            if (_waiting?.Any(w => w is WaitingReload) != true)
            {
                Enqueue(new WaitingReload(time.GetUtcNow(), staffEmail));
            }
        }

        return true;
    }

    /// <summary>Whether the whole history is waiting to be loaded again or is being loaded now.</summary>
    public bool IsReloading
    {
        get
        {
            lock (_gate)
            {
                return _waiting?.Any(w => w is WaitingReload) == true;
            }
        }
    }

    /// <summary>Waits until a gap is found or our staff ask for something.</summary>
    public async Task WaitForGapAsync(CancellationToken cancellationToken) => await _gapFound.Reader.ReadAsync(cancellationToken);

    /// <summary>
    /// Fills the gaps found, in order, from the feed's history, stores the bars, and adds the prices that waited after
    /// each. Our staff's requests are done in the same order. Returns how many bars it added. A gap the feed has no history
    /// for stays, and the prices after it are added.
    /// </summary>
    public async Task<int> FillGapsAsync(CancellationToken cancellationToken)
    {
        var added = 0;
        while (true)
        {
            // The same list until it is emptied here, since prices and gaps are added to it while it waits.
            List<Waiting> waiting;
            Waiting work;
            lock (_gate)
            {
                if (_waiting is not [var first, ..] || first is WaitingPrice)
                {
                    return added;
                }

                waiting = _waiting;
                work = first;
            }

            switch (work)
            {
                case WaitingGap gap:
                    var bars = await LoadGapAsync(gap.Gap, cancellationToken);
                    lock (_gate)
                    {
                        foreach (var bar in bars.OrderBy(b => b.Candle.Time))
                        {
                            candles.AddBar(bar);
                        }
                    }

                    added += bars.Count;
                    break;
                case WaitingRetry retry:
                    added += await RetryAsync(retry.Gap, cancellationToken);
                    break;
                case WaitingReload reload:
                    added += await ReloadAsync(reload, cancellationToken);
                    break;
            }

            lock (_gate)
            {
                // The prices up to the next gap or request, if another one came meanwhile.
                var taken = 1;
                for (; taken < waiting.Count && waiting[taken] is WaitingPrice price; taken++)
                {
                    candles.Add(price.Symbol, price.Bid, price.At);
                }

                waiting.RemoveRange(0, taken);
                if (waiting.Count == 0)
                {
                    _waiting = null;
                    return added;
                }
            }
        }
    }

    /// <summary>Stores the minute bars that have finished since the last call. Waits while prices wait for a gap.</summary>
    public async Task RecordAsync(CancellationToken cancellationToken)
    {
        await _storing.WaitAsync(cancellationToken);
        try
        {
            var until = CandleStore.BarStart(Timeframe.M1, time.GetUtcNow() - RecordDelay);
            lock (_gate)
            {
                // The minutes of the prices that wait are not in the charts yet, and a restart replays them.
                if (_waiting is not null)
                {
                    return;
                }
            }

            if (until <= _recordedUntil)
            {
                return;
            }

            var bars = candles.GetBars(Timeframe.M1, _recordedUntil, until);
            if (bars.Count > 0)
            {
                await store.SaveAsync(feed.Name, bars, cancellationToken);
            }

            _recordedUntil = until;
        }
        finally
        {
            _storing.Release();
        }
    }

    public void Dispose() => _storing.Dispose();

    private static DateTimeOffset Today(DateTimeOffset now) => CandleStore.BarStart(Timeframe.D1, now);

    // The oldest the charts reach: the day bars' start, for the week and month timeframes (ADR 0058).
    private DateTimeOffset Reach(DateTimeOffset now) => Today(now) - options.Value.DayHistory;

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static string Iso(DateTimeOffset at) => at.ToString("O", CultureInfo.InvariantCulture);

    // Lock held. The first item makes the prices after it wait and wakes the filler.
    private void Enqueue(Waiting work)
    {
        if (_waiting is null)
        {
            _waiting = [];
            _gapFound.Writer.TryWrite(true);
        }

        _waiting.Add(work);
    }

    // Minute bars for the latest days, 15 minute bars before them and hour bars before those.
    private HistorySpan[] Spans(DateTimeOffset from, DateTimeOffset until, DateTimeOffset now)
    {
        var hoursFrom = Today(now) - options.Value.History;
        var quartersFrom = Today(now) - options.Value.QuarterHourHistory;
        var minutesFrom = Today(now) - options.Value.MinuteHistory;
        return
        [
            .. new[]
            {
                new HistorySpan(Timeframe.D1, from, Min(hoursFrom, until)),
                new HistorySpan(Timeframe.H1, Max(from, hoursFrom), Min(quartersFrom, until)),
                new HistorySpan(Timeframe.M15, Max(from, quartersFrom), Min(minutesFrom, until)),
                new HistorySpan(Timeframe.M1, Max(from, minutesFrom), until),
            }.Where(s => s.From < s.Until),
        ];
    }

    private async Task LoadFeedHistoryAsync(DateTimeOffset from, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var bars = await feed.GetHistoryAsync(Spans(from, now, now), cancellationToken);
            await store.ReplaceHistoryAsync(feed.Name, from, now, bars, cancellationToken);
            LogHistoryLoaded(logger, bars.Count, feed.Name);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The charts then fill from live prices. Since the history is not marked as loaded, the next start tries again.
            LogHistoryFailed(logger, feed.Name, exception);
        }
    }

    // The gap's bars from the feed, stored so a restart has them. None when the feed has none or cannot be reached, after
    // a few tries, while the prices after the gap wait. What came of it is saved for our staff.
    private async Task<IReadOnlyList<ChartBar>> LoadGapAsync(ChartGap gap, CancellationToken cancellationToken)
    {
        gap = gap with { State = ChartGapState.Filling, FinishedAt = null, Problem = null };
        await SaveGapAsync(gap);
        IReadOnlyList<ChartBar>? bars = null;
        for (var attempt = 0; bars is null; attempt++)
        {
            try
            {
                gap = gap with { Tries = gap.Tries + 1 };
                bars = await feed.GetHistoryAsync(Spans(gap.From, gap.Until, time.GetUtcNow()), cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == GapRetries.Length)
                {
                    LogGapFailed(logger, gap.From, gap.Until, feed.Name, exception);
                    await SaveGapAsync(gap with { State = ChartGapState.NotFilled, FinishedAt = time.GetUtcNow(), Problem = exception.Message });
                    await events.RecordAsync(
                        PlatformEventKind.ChartGapNotFilled,
                        null,
                        null,
                        new() { ["from"] = Iso(gap.From), ["until"] = Iso(gap.Until), ["problem"] = exception.Message });
                    return [];
                }

                LogGapRetry(logger, gap.From, gap.Until, feed.Name, GapRetries[attempt], exception.Message);
                await SaveGapAsync(gap with { Problem = exception.Message });
                await Task.Delay(GapRetries[attempt], time, cancellationToken);
            }
        }

        if (bars.Count > 0)
        {
            try
            {
                await store.SaveAsync(feed.Name, bars, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The charts still show them until the next start.
                LogGapNotStored(logger, exception);
            }
        }

        LogGapFilled(logger, bars.Count, gap.From, gap.Until, feed.Name);
        await SaveGapAsync(gap with { State = bars.Count > 0 ? ChartGapState.Filled : ChartGapState.Empty, FinishedAt = time.GetUtcNow(), Bars = bars.Count });
        if (bars.Count > 0)
        {
            await events.RecordAsync(
                PlatformEventKind.ChartGapFilled,
                null,
                null,
                new() { ["from"] = Iso(gap.From), ["until"] = Iso(gap.Until), ["bars"] = bars.Count.ToString(CultureInfo.InvariantCulture) });
        }

        return bars;
    }

    // A gap from earlier, filled again on our staff's request. The prices after it are in the charts already, so the
    // charts are built again with its bars.
    private async Task<int> RetryAsync(ChartGap gap, CancellationToken cancellationToken)
    {
        var bars = await LoadGapAsync(gap, cancellationToken);
        if (bars.Count == 0)
        {
            return 0;
        }

        await RebuildAsync(bars, cancellationToken);
        return bars.Count;
    }

    // The feed's whole history again, up to the minute of the request, then the charts built again from it.
    private async Task<int> ReloadAsync(WaitingReload reload, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var from = Reach(now);
        var until = CandleStore.BarStart(Timeframe.M1, reload.RequestedAt);
        IReadOnlyList<ChartBar> bars;
        try
        {
            bars = await feed.GetHistoryAsync(Spans(from, until, now), cancellationToken);
            await _storing.WaitAsync(cancellationToken);
            try
            {
                await store.ReplaceHistoryAsync(feed.Name, from, until, bars, cancellationToken);
                _recordedUntil = Max(_recordedUntil, until);
            }
            finally
            {
                _storing.Release();
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogHistoryFailed(logger, feed.Name, exception);
            await events.RecordAsync(PlatformEventKind.ChartHistoryReloaded, null, reload.StaffEmail, new() { ["problem"] = exception.Message });
            return 0;
        }

        await RebuildAsync([], cancellationToken);
        LogHistoryLoaded(logger, bars.Count, feed.Name);

        // The gaps in the period are covered by the history now.
        foreach (var gap in await store.ListGapsAsync(feed.Name, int.MaxValue, cancellationToken))
        {
            if (gap.State is (ChartGapState.NotFilled or ChartGapState.Empty) && gap.Until <= until && gap.From >= from)
            {
                var covered = bars.Count(b => b.Candle.Time >= gap.From && b.Candle.Time < gap.Until);
                if (covered > 0)
                {
                    await SaveGapAsync(gap with { State = ChartGapState.Filled, FinishedAt = time.GetUtcNow(), Bars = covered, Problem = null });
                }
            }
        }

        await events.RecordAsync(
            PlatformEventKind.ChartHistoryReloaded,
            null,
            reload.StaffEmail,
            new() { ["bars"] = bars.Count.ToString(CultureInfo.InvariantCulture) });
        return bars.Count;
    }

    // Builds the charts again, while the prices that come meanwhile wait: the stored bars before where the recorder has
    // come, the minute bars in memory from there on, which are not stored yet, and new bars from there on, such as a
    // gap's that was filled late.
    private async Task RebuildAsync(IReadOnlyList<ChartBar> added, CancellationToken cancellationToken)
    {
        await _storing.WaitAsync(cancellationToken);
        try
        {
            var recorded = _recordedUntil;
            var from = Reach(time.GetUtcNow());
            var bars = new List<ChartBar>();
            await foreach (var bar in store.ReadAsync(feed.Name, from, cancellationToken))
            {
                if (bar.Candle.Time < recorded)
                {
                    bars.Add(bar);
                }
            }

            bars.AddRange(added.Where(b => b.Candle.Time >= recorded));
            bars.AddRange(candles.GetBars(Timeframe.M1, recorded, DateTimeOffset.MaxValue));
            candles.Replace(bars.OrderBy(b => b.Candle.Time));
        }
        finally
        {
            _storing.Release();
        }
    }

    private async Task SaveGapAsync(ChartGap gap)
    {
        try
        {
            await store.SaveGapAsync(gap, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Only our staff's view of the gap is lost. The charts are filled all the same.
            LogGapRecordFailed(logger, exception);
        }
    }

    private abstract record Waiting;

    private sealed record WaitingPrice(string Symbol, decimal Bid, DateTimeOffset At) : Waiting;

    private sealed record WaitingGap(ChartGap Gap) : Waiting;

    private sealed record WaitingRetry(ChartGap Gap) : Waiting;

    private sealed record WaitingReload(DateTimeOffset RequestedAt, string StaffEmail) : Waiting;

    [LoggerMessage(Level = LogLevel.Information, Message = "Filled the charts with {Bars} stored bars and {Prices} recorded prices of {Feed}")]
    private static partial void LogLoaded(ILogger logger, int bars, int prices, string feed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} bars of history from {Feed}")]
    private static partial void LogHistoryLoaded(ILogger logger, int count, string feed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load the history from {Feed}. It is tried again at the next start.")]
    private static partial void LogHistoryFailed(ILogger logger, string feed, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Filled the charts' gap from {From} to {Until} with {Count} bars from {Feed}")]
    private static partial void LogGapFilled(ILogger logger, int count, DateTimeOffset from, DateTimeOffset until, string feed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Could not fill the charts' gap from {From} to {Until} from {Feed} yet, tries again in {Wait}: {Reason}")]
    private static partial void LogGapRetry(ILogger logger, DateTimeOffset from, DateTimeOffset until, string feed, TimeSpan wait, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not fill the charts' gap from {From} to {Until} from {Feed}")]
    private static partial void LogGapFailed(ILogger logger, DateTimeOffset from, DateTimeOffset until, string feed, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not store the bars that filled the charts' gap")]
    private static partial void LogGapNotStored(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save what came of a gap in the charts")]
    private static partial void LogGapRecordFailed(ILogger logger, Exception exception);
}
