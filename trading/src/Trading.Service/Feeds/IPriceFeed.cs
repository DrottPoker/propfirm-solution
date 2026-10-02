namespace Trading.Service.Feeds;

/// <summary>Raw price from a price source, before any markup.</summary>
public sealed record FeedQuote(string Symbol, decimal Bid, decimal Ask, DateTimeOffset Timestamp);

/// <summary>Adapter for a price source. One implementation per data vendor. Prices must be on the instrument's price grid.</summary>
public interface IPriceFeed
{
    /// <summary>Recent history used to fill the charts at the first start. Never sent to the engine.</summary>
    IEnumerable<FeedQuote> GetBackfill(DateTimeOffset until);

    /// <summary>Live prices until cancelled.</summary>
    IAsyncEnumerable<FeedQuote> StreamAsync(CancellationToken cancellationToken);
}

/// <summary>A feed that makes up its prices and can continue from the last recorded ones after a restart.</summary>
public interface IContinuablePriceFeed : IPriceFeed
{
    void ContinueFrom(IReadOnlyList<FeedQuote> lastQuotes);
}
