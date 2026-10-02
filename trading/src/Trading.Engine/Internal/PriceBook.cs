using System.Diagnostics.CodeAnalysis;

using Trading.Engine.Inputs;

namespace Trading.Engine.Internal;

/// <summary>Latest raw quote per symbol, and currency conversion based on them.</summary>
internal sealed class PriceBook
{
    private readonly Dictionary<string, Quote> _latest = new(StringComparer.Ordinal);

    // (from, to) -> symbol to convert with, and whether the rate is inverted. First configured match wins.
    private readonly Dictionary<(string From, string To), (string Symbol, bool Inverse)> _conversions = [];

    public PriceBook(IEnumerable<Instrument> instruments)
    {
        foreach (var instrument in instruments)
        {
            _conversions.TryAdd((instrument.BaseCurrency, instrument.QuoteCurrency), (instrument.Symbol, false));
            _conversions.TryAdd((instrument.QuoteCurrency, instrument.BaseCurrency), (instrument.Symbol, true));
        }
    }

    public void Update(Quote quote) => _latest[quote.Symbol] = quote;

    public IEnumerable<Quote> LatestQuotes => _latest.Values;

    public bool TryGetLatest(string symbol, [NotNullWhen(true)] out Quote? quote) => _latest.TryGetValue(symbol, out quote);

    /// <summary>Rate to multiply an amount in <paramref name="from"/> with to get <paramref name="to"/>. Uses the raw mid price.</summary>
    public bool TryGetRate(string from, string to, out decimal rate)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            rate = 1m;
            return true;
        }

        if (_conversions.TryGetValue((from, to), out var conversion) && _latest.TryGetValue(conversion.Symbol, out var quote))
        {
            var mid = (quote.Bid + quote.Ask) / 2m;
            rate = conversion.Inverse ? 1m / mid : mid;
            return true;
        }

        rate = 0m;
        return false;
    }

    public static ClientPrice ToClientPrice(Instrument instrument, SymbolConditions conditions, Quote quote) =>
        new(
            quote.Bid - (conditions.BidMarkupPoints * instrument.Point),
            quote.Ask + (conditions.AskMarkupPoints * instrument.Point),
            quote.Timestamp);
}

/// <summary>Price after spread markup, as seen by the accounts in a group.</summary>
internal readonly record struct ClientPrice(decimal Bid, decimal Ask, DateTimeOffset Timestamp)
{
    public decimal OpenPrice(Side side) => side == Side.Buy ? Ask : Bid;

    public decimal ClosePrice(Side side) => side == Side.Buy ? Bid : Ask;
}
