using Trading.Service.Candles;
using Trading.Service.Engine;

namespace Trading.Service.Feeds;

/// <summary>Moves prices from the feed into the candles and the engine.</summary>
internal sealed partial class PriceFeedPump(
    IPriceFeed feed,
    EngineHost engine,
    CandleStore candles,
    TimeProvider time,
    ILogger<PriceFeedPump> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The backfill is CPU work, so let startup finish first.
        await Task.Yield();

        var backfilled = 0;
        foreach (var quote in feed.GetBackfill(time.GetUtcNow()))
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            candles.Add(quote.Symbol, quote.Bid, quote.Timestamp);
            backfilled++;
        }

        LogBackfilled(logger, backfilled);

        await foreach (var quote in feed.StreamAsync(stoppingToken))
        {
            // Prices are stamped with the service clock, like everything the engine sees.
            candles.Add(quote.Symbol, quote.Bid, time.GetUtcNow());
            engine.EnqueueQuote(quote.Symbol, quote.Bid, quote.Ask);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Filled the charts with {Count} historical prices")]
    private static partial void LogBackfilled(ILogger logger, int count);
}
