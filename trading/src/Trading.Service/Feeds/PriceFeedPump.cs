using Trading.Service.Candles;
using Trading.Service.Engine;

namespace Trading.Service.Feeds;

/// <summary>Moves prices from the feed into the charts and the engine, once the charts are filled.</summary>
internal sealed class PriceFeedPump(
    IPriceFeed feed,
    EngineHost engine,
    ChartHistory history,
    CandleStore candles,
    TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await engine.Ready.WaitAsync(stoppingToken);

        // Before the history, so that made-up history ends where the live prices continue.
        if (feed is IContinuablePriceFeed continuable)
        {
            var lastQuotes = await engine.QueryAsync(e => e.GetLatestQuotes(), stoppingToken);
            if (lastQuotes.Count > 0)
            {
                continuable.ContinueFrom(lastQuotes.Select(q => new FeedQuote(q.Symbol, q.Bid, q.Ask, q.Timestamp)).ToList());
            }
        }

        await history.LoadAsync(stoppingToken);
        candles.MarkReady();

        await foreach (var quote in feed.StreamAsync(stoppingToken))
        {
            // Prices are stamped with the service clock, like everything the engine sees. The charts may hold them back
            // while they fill a gap, but the engine never waits.
            history.AddPrice(quote.Symbol, quote.Bid, time.GetUtcNow());
            engine.EnqueueQuote(feed.Name, quote.Symbol, quote.Bid, quote.Ask);
        }
    }
}
