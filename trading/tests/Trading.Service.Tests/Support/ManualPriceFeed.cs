using System.Collections.Concurrent;
using System.Threading.Channels;

using Trading.Service.Candles;
using Trading.Service.Feeds;

namespace Trading.Service.Tests.Support;

/// <summary>A price feed the test pushes prices into, with the history the test gives it.</summary>
internal class ManualPriceFeed(string name = ManualPriceFeed.DefaultName) : IPriceFeed
{
    public const string DefaultName = "Manual";

    private readonly Channel<FeedQuote> _quotes = Channel.CreateUnbounded<FeedQuote>();
    private readonly ConcurrentQueue<IReadOnlyList<HistorySpan>> _historyRequests = new();

    public string Name => name;

    /// <summary>True by default, like a real feed. The test clock starts on a Monday morning, when every market is open.</summary>
    public bool FollowsTradingHours { get; init; } = true;

    /// <summary>The feed's history. A request gets the bars that lie whole inside its spans, as from a real feed.</summary>
    public IReadOnlyList<ChartBar> History { get; set; } = [];

    /// <summary>When set, history requests throw it.</summary>
    public Exception? HistoryFailure { get; set; }

    public IReadOnlyList<IReadOnlyList<HistorySpan>> HistoryRequests => [.. _historyRequests];

    public void Push(string symbol, decimal bid, decimal ask) =>
        _quotes.Writer.TryWrite(new FeedQuote(symbol, bid, ask, DateTimeOffset.MinValue));

    public Task<IReadOnlyList<ChartBar>> GetHistoryAsync(IReadOnlyList<HistorySpan> spans, CancellationToken cancellationToken)
    {
        _historyRequests.Enqueue(spans);
        if (HistoryFailure is { } failure)
        {
            return Task.FromException<IReadOnlyList<ChartBar>>(failure);
        }

        IReadOnlyList<ChartBar> bars =
            [.. History.Where(b => spans.Any(s => b.Candle.Time >= s.From && b.Candle.Time + CandleStore.Duration(b.Resolution) <= s.Until))];
        return Task.FromResult(bars);
    }

    public IAsyncEnumerable<FeedQuote> StreamAsync(CancellationToken cancellationToken) => _quotes.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>A manual feed that, like the synthetic one, continues from the last recorded prices.</summary>
internal sealed class ContinuingPriceFeed(string name) : ManualPriceFeed(name), IContinuablePriceFeed
{
    public void ContinueFrom(IReadOnlyList<FeedQuote> lastQuotes)
    {
    }
}
