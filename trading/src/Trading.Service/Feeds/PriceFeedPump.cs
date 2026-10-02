using Microsoft.Extensions.Options;

using Trading.Service.Candles;
using Trading.Service.Engine;
using Trading.Service.Persistence;

namespace Trading.Service.Feeds;

/// <summary>Moves prices from the feed into the candles and the engine.</summary>
internal sealed partial class PriceFeedPump(
    IPriceFeed feed,
    EngineHost engine,
    IEngineJournal journal,
    CandleStore candles,
    TimeProvider time,
    IOptions<JournalOptions> options,
    ILogger<PriceFeedPump> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await engine.Ready.WaitAsync(stoppingToken);

        var lastQuotes = await engine.QueryAsync(e => e.GetLatestQuotes(), stoppingToken);
        if (lastQuotes.Count > 0)
        {
            await RebuildCandlesFromJournalAsync(stoppingToken);
            if (feed is IContinuablePriceFeed continuable)
            {
                continuable.ContinueFrom(lastQuotes.Select(q => new FeedQuote(q.Symbol, q.Bid, q.Ask, q.Timestamp)).ToList());
            }
        }
        else
        {
            FillCandlesFromFeedHistory(stoppingToken);
        }

        await foreach (var quote in feed.StreamAsync(stoppingToken))
        {
            // Prices are stamped with the service clock, like everything the engine sees.
            candles.Add(quote.Symbol, quote.Bid, time.GetUtcNow());
            engine.EnqueueQuote(quote.Symbol, quote.Bid, quote.Ask);
        }
    }

    // Every price the engine has seen is in the journal, so the charts survive a restart.
    private async Task RebuildCandlesFromJournalAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        await foreach (var quote in journal.ReadQuotesAsync(time.GetUtcNow() - options.Value.CandleHistory, cancellationToken))
        {
            candles.Add(quote.Symbol, quote.Bid, quote.Timestamp);
            count++;
        }

        LogRebuilt(logger, count);
    }

    // First start: nothing recorded yet, so the feed's own history fills the charts.
    private void FillCandlesFromFeedHistory(CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var quote in feed.GetBackfill(time.GetUtcNow()))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            candles.Add(quote.Symbol, quote.Bid, quote.Timestamp);
            count++;
        }

        LogBackfilled(logger, count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rebuilt the charts from {Count} recorded prices")]
    private static partial void LogRebuilt(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Filled the charts with {Count} historical prices from the feed")]
    private static partial void LogBackfilled(ILogger logger, int count);
}
