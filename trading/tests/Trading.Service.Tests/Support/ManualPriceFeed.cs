using System.Threading.Channels;

using Trading.Service.Feeds;

namespace Trading.Service.Tests.Support;

/// <summary>A price feed the test pushes prices into. No backfill.</summary>
internal sealed class ManualPriceFeed : IPriceFeed
{
    private readonly Channel<FeedQuote> _quotes = Channel.CreateUnbounded<FeedQuote>();

    public void Push(string symbol, decimal bid, decimal ask) =>
        _quotes.Writer.TryWrite(new FeedQuote(symbol, bid, ask, DateTimeOffset.MinValue));

    public IEnumerable<FeedQuote> GetBackfill(DateTimeOffset until) => [];

    public IAsyncEnumerable<FeedQuote> StreamAsync(CancellationToken cancellationToken) => _quotes.Reader.ReadAllAsync(cancellationToken);
}
