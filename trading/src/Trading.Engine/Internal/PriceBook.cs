using System.Diagnostics.CodeAnalysis;

using Trading.Engine.Inputs;

namespace Trading.Engine.Internal;

/// <summary>Latest raw quote per symbol, and currency conversion based on them.</summary>
internal sealed class PriceBook
{
    private readonly Dictionary<string, Quote> _latest = new(StringComparer.Ordinal);

    // (from, to) -> symbol to convert with, and whether the rate is inverted. First configured match wins.
    private readonly Dictionary<(string From, string To), (string Symbol, bool Inverse)> _conversions = [];

    // Every currency and priced asset, in configuration order, so the currency a rate goes through is always the same.
    private readonly List<string> _currencies = [];

    // (from, to) -> the currency other than USD that the rate goes through, or null when there is none. Depends only on
    // the configuration, so it is kept as it is found.
    private readonly Dictionary<(string From, string To), string?> _otherRoutes = [];

    public PriceBook(IEnumerable<Instrument> instruments)
    {
        foreach (var instrument in instruments)
        {
            _conversions.TryAdd((instrument.BaseCurrency, instrument.QuoteCurrency), (instrument.Symbol, false));
            _conversions.TryAdd((instrument.QuoteCurrency, instrument.BaseCurrency), (instrument.Symbol, true));
            foreach (var currency in new[] { instrument.BaseCurrency, instrument.QuoteCurrency })
            {
                if (!_currencies.Contains(currency, StringComparer.Ordinal))
                {
                    _currencies.Add(currency);
                }
            }
        }
    }

    public void Update(Quote quote) => _latest[quote.Symbol] = quote;

    public IEnumerable<Quote> LatestQuotes => _latest.Values;

    public bool TryGetLatest(string symbol, [NotNullWhen(true)] out Quote? quote) => _latest.TryGetValue(symbol, out quote);

    /// <summary>The currency a rate goes through when no instrument has the pair itself.</summary>
    public const string CrossCurrency = "USD";

    /// <summary>
    /// Rate to multiply an amount in <paramref name="from"/> with to get <paramref name="to"/>. Uses the raw mid price of
    /// an instrument with the pair, or else the two rates through <see cref="CrossCurrency"/>, so an account in EUR can
    /// trade AUDUSD with EURUSD and AUDUSD. Only when the configuration has neither does the rate go through another
    /// currency, the first in configuration order that has both pairs, so a USD account can trade the EUR index DE40 with
    /// DE40 and EURUSD. The way is chosen from the configuration, and a missing price on it means no rate.
    /// </summary>
    public bool TryGetRate(string from, string to, out decimal rate)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            rate = 1m;
            return true;
        }

        if (TryGetDirectRate(from, to, out rate) || (IsRoute(CrossCurrency, from, to) && TryGetRateThrough(CrossCurrency, from, to, out rate)))
        {
            return true;
        }

        // Rates that the pair itself or USD can give never go another way, so earlier inputs give the same events.
        if (_conversions.ContainsKey((from, to)) || IsRoute(CrossCurrency, from, to))
        {
            rate = 0m;
            return false;
        }

        if (!_otherRoutes.TryGetValue((from, to), out var through))
        {
            through = _currencies.FirstOrDefault(c => !string.Equals(c, CrossCurrency, StringComparison.Ordinal) && IsRoute(c, from, to));
            _otherRoutes[(from, to)] = through;
        }

        if (through is not null)
        {
            return TryGetRateThrough(through, from, to, out rate);
        }

        rate = 0m;
        return false;
    }

    private bool IsRoute(string through, string from, string to) =>
        !string.Equals(from, through, StringComparison.Ordinal)
        && !string.Equals(to, through, StringComparison.Ordinal)
        && _conversions.ContainsKey((from, through))
        && _conversions.ContainsKey((through, to));

    private bool TryGetRateThrough(string through, string from, string to, out decimal rate)
    {
        if (TryGetDirectRate(from, through, out var toThrough) && TryGetDirectRate(through, to, out var fromThrough))
        {
            rate = toThrough * fromThrough;
            return true;
        }

        rate = 0m;
        return false;
    }

    private bool TryGetDirectRate(string from, string to, out decimal rate)
    {
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
