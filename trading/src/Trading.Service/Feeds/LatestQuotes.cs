namespace Trading.Service.Feeds;

/// <summary>
/// The newest price per symbol that has not been taken yet. A newer price replaces an older one, so a busy
/// market cannot flood the engine and the journal.
/// </summary>
internal sealed class LatestQuotes
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, FeedQuote> _pending = new(StringComparer.Ordinal);

    public void Set(FeedQuote quote)
    {
        lock (_lock)
        {
            _pending[quote.Symbol] = quote;
        }
    }

    /// <summary>Takes the pending prices, ordered by symbol.</summary>
    public IReadOnlyList<FeedQuote> TakeAll()
    {
        lock (_lock)
        {
            if (_pending.Count == 0)
            {
                return [];
            }

            var quotes = _pending.Values.OrderBy(q => q.Symbol, StringComparer.Ordinal).ToList();
            _pending.Clear();
            return quotes;
        }
    }
}
