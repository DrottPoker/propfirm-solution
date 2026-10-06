using Microsoft.Extensions.Options;

using Trading.Service.Feeds;
using Trading.Service.Persistence;

namespace Trading.Service.Candles;

/// <summary>
/// The charts' history (ADR 0048). The charts show only the current feed's prices: its history for the last 30 days,
/// loaded when the service switches to the feed, and minute bars made from its live prices, stored as they finish. At
/// start the stored bars fill the charts and the prices after them are replayed from the journal.
/// </summary>
internal sealed partial class ChartHistory(
    IPriceFeed feed,
    IChartStore store,
    IEngineJournal journal,
    CandleStore candles,
    TimeProvider time,
    IOptions<ChartOptions> options,
    ILogger<ChartHistory> logger)
{
    // Without stored bars, for example when the history could not be loaded, the latest day of prices is replayed.
    private static readonly TimeSpan ReplayWithoutBars = TimeSpan.FromDays(1);

    // Lets a price stamped just before a minute ended reach its bar before the bar is stored.
    private static readonly TimeSpan RecordDelay = TimeSpan.FromSeconds(2);

    // Bars before this are stored. Used by the recorder only, after the load.
    private DateTimeOffset _recordedUntil;

    /// <summary>Fills the charts. Loads the feed's history first when the service has switched to the feed.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var from = Today(now) - options.Value.History;
        await store.PruneAsync(from, cancellationToken);

        // The last price is from another feed, or there is none: the service has switched to this feed.
        if (await journal.GetLastQuoteFeedAsync(cancellationToken) != feed.Name)
        {
            await store.ForgetHistoryAsync(feed.Name, cancellationToken);
        }

        if (!await store.HasHistoryAsync(feed.Name, cancellationToken))
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
        var replayFrom = CandleStore.BarStart(Timeframe.M1, storedUntil ?? now - ReplayWithoutBars);
        var prices = 0;
        await foreach (var quote in journal.ReadQuotesAsync(replayFrom, feed.Name, cancellationToken))
        {
            candles.Add(quote.Symbol, quote.Bid, quote.Timestamp);
            prices++;
        }

        _recordedUntil = replayFrom;
        LogLoaded(logger, bars, prices, feed.Name);
    }

    /// <summary>Stores the minute bars that have finished since the last call.</summary>
    public async Task RecordAsync(CancellationToken cancellationToken)
    {
        var until = CandleStore.BarStart(Timeframe.M1, time.GetUtcNow() - RecordDelay);
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

    private static DateTimeOffset Today(DateTimeOffset now) => CandleStore.BarStart(Timeframe.D1, now);

    // Minute bars for the latest days, 15 minute bars before them.
    private async Task LoadFeedHistoryAsync(DateTimeOffset from, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var minutesFrom = Today(now) - options.Value.MinuteHistory;
        HistorySpan[] spans = [.. new[] { new HistorySpan(Timeframe.M15, from, minutesFrom), new HistorySpan(Timeframe.M1, minutesFrom, now) }.Where(s => s.From < s.Until)];
        try
        {
            var bars = await feed.GetHistoryAsync(spans, cancellationToken);
            await store.ReplaceHistoryAsync(feed.Name, now, bars, cancellationToken);
            LogHistoryLoaded(logger, bars.Count, feed.Name);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The charts then fill from live prices. Since the history is not marked as loaded, the next start tries again.
            LogHistoryFailed(logger, feed.Name, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Filled the charts with {Bars} stored bars and {Prices} recorded prices of {Feed}")]
    private static partial void LogLoaded(ILogger logger, int bars, int prices, string feed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} bars of history from {Feed}")]
    private static partial void LogHistoryLoaded(ILogger logger, int count, string feed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load the history from {Feed}. It is tried again at the next start.")]
    private static partial void LogHistoryFailed(ILogger logger, string feed, Exception exception);
}
