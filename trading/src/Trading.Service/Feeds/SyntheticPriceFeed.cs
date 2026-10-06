using System.Runtime.CompilerServices;

using Microsoft.Extensions.Options;

using Trading.Service.Candles;
using Trading.Service.Configuration;

namespace Trading.Service.Feeds;

public sealed class SyntheticFeedOptions
{
    public const string SectionName = "SyntheticFeed";

    public int Seed { get; init; } = 1;

    public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(250);

    public IReadOnlyList<SyntheticSymbolOptions> Symbols { get; init; } = [];
}

public sealed class SyntheticSymbolOptions
{
    public string Symbol { get; init; } = "";

    public decimal StartBid { get; init; }

    public int SpreadPoints { get; init; } = 1;

    /// <summary>Largest bid move per interval, in points.</summary>
    public int StepPoints { get; init; } = 2;
}

/// <summary>
/// Random walk prices for local development, so nothing depends on a data vendor.
/// The same seed gives the same prices. Not a market simulation.
/// </summary>
internal sealed class SyntheticPriceFeed : IContinuablePriceFeed
{
    // Moves simulated per history bar. Each is as large as the many intervals it stands for would be together.
    private const int StepsPerHistoryBar = 60;

    private readonly SyntheticFeedOptions _options;
    private readonly TimeProvider _time;
    private readonly Random _random;
    private readonly List<Walk> _walks;

    public SyntheticPriceFeed(IOptions<SyntheticFeedOptions> options, IOptions<TradingOptions> trading, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
        _random = new Random(_options.Seed);

        var digits = trading.Value.Instruments.ToDictionary(i => i.Symbol, i => i.Digits, StringComparer.Ordinal);
        _walks = _options.Symbols
            .Select(s => new Walk(
                s,
                digits.TryGetValue(s.Symbol, out var symbolDigits)
                    ? symbolDigits
                    : throw new InvalidOperationException($"Synthetic feed symbol {s.Symbol} is not a configured instrument.")))
            .ToList();
    }

    public string Name => PriceFeedOptions.SyntheticProvider;

    /// <summary>
    /// Made-up bars that end where the live prices continue, so the charts never jump. Walks backwards in time from the
    /// current price, the newest span first.
    /// </summary>
    public Task<IReadOnlyList<ChartBar>> GetHistoryAsync(IReadOnlyList<HistorySpan> spans, CancellationToken cancellationToken)
    {
        var bars = new List<ChartBar>();
        foreach (var walk in _walks)
        {
            var symbolBars = new List<ChartBar>();
            var closePoints = walk.BidPoints;
            foreach (var span in spans.OrderByDescending(s => s.Until))
            {
                var length = CandleStore.Duration(span.Resolution);
                var intervals = Math.Max(1, (int)(length / _options.Interval));
                var steps = Math.Min(intervals, StepsPerHistoryBar);
                var scale = Math.Sqrt((double)intervals / steps);
                for (var start = CandleStore.BarStart(span.Resolution, span.Until) - length; start >= span.From; start -= length)
                {
                    var (bar, openPoints) = walk.Back(_random, closePoints, steps, scale, span.Resolution, start);
                    symbolBars.Add(bar);
                    closePoints = openPoints;
                }
            }

            symbolBars.Reverse();
            bars.AddRange(symbolBars);
        }

        return Task.FromResult<IReadOnlyList<ChartBar>>(bars);
    }

    /// <summary>Continues each walk from the last recorded price, so a restart does not make prices jump.</summary>
    public void ContinueFrom(IReadOnlyList<FeedQuote> lastQuotes)
    {
        foreach (var quote in lastQuotes)
        {
            _walks.Find(w => w.Symbol == quote.Symbol)?.MoveTo(quote.Bid);
        }
    }

    public async IAsyncEnumerable<FeedQuote> StreamAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.Interval, _time);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var now = _time.GetUtcNow();
            foreach (var walk in _walks)
            {
                yield return walk.Next(_random, now);
            }
        }
    }

    private sealed class Walk
    {
        private readonly SyntheticSymbolOptions _options;
        private readonly decimal _point;
        private long _bidPoints;

        public Walk(SyntheticSymbolOptions options, int digits)
        {
            _options = options;
            _point = new decimal(1, 0, 0, false, (byte)digits);
            _bidPoints = (long)(options.StartBid / _point);
        }

        public string Symbol => _options.Symbol;

        public long BidPoints => _bidPoints;

        public void MoveTo(decimal bid) => _bidPoints = (long)(bid / _point);

        public FeedQuote Next(Random random, DateTimeOffset time)
        {
            _bidPoints = Math.Max(1, _bidPoints + random.Next(-_options.StepPoints, _options.StepPoints + 1));
            var spreadPoints = _options.SpreadPoints + random.Next(0, 2);
            return new FeedQuote(_options.Symbol, _bidPoints * _point, (_bidPoints + spreadPoints) * _point, time);
        }

        /// <summary>A bar that ends at the close, walked backwards. Returns it with its open, where the bar before it ends.</summary>
        public (ChartBar Bar, long OpenPoints) Back(Random random, long closePoints, int steps, double scale, Timeframe resolution, DateTimeOffset start)
        {
            var points = closePoints;
            var high = closePoints;
            var low = closePoints;
            for (var i = 0; i < steps; i++)
            {
                points = Math.Max(1, points + (long)Math.Round(random.Next(-_options.StepPoints, _options.StepPoints + 1) * scale));
                high = Math.Max(high, points);
                low = Math.Min(low, points);
            }

            var candle = new Candle(start, points * _point, high * _point, low * _point, closePoints * _point, 0);
            return (new ChartBar(_options.Symbol, resolution, candle), points);
        }
    }
}
