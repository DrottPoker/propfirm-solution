using Trading.Service.Candles;

namespace Trading.Service.Feeds;

/// <summary>Raw price from a price source, before any markup.</summary>
public sealed record FeedQuote(string Symbol, decimal Bid, decimal Ask, DateTimeOffset Timestamp);

/// <summary>Adapter for a price source. One implementation per data vendor. Prices must be on the instrument's price grid.</summary>
public interface IPriceFeed
{
    /// <summary>Stored with every price it gives, the same as its <c>PriceFeed:Provider</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Whether the prices come from markets that open and close. The engine then follows the instruments' trading hours.
    /// Made-up prices run around the clock, so with them every market is always open (ADR 0050).
    /// </summary>
    bool FollowsTradingHours { get; }

    /// <summary>
    /// Bars of bid for every instrument, at each span's resolution, oldest first. Only whole bars inside the spans.
    /// Loaded into the charts when the service switches to the feed, and never sent to the engine.
    /// </summary>
    Task<IReadOnlyList<ChartBar>> GetHistoryAsync(IReadOnlyList<HistorySpan> spans, CancellationToken cancellationToken);

    /// <summary>Live prices until cancelled.</summary>
    IAsyncEnumerable<FeedQuote> StreamAsync(CancellationToken cancellationToken);
}

/// <summary>A feed that makes up its prices and continues from the last recorded ones, so a restart or a switch does not make them jump.</summary>
public interface IContinuablePriceFeed : IPriceFeed
{
    void ContinueFrom(IReadOnlyList<FeedQuote> lastQuotes);
}
