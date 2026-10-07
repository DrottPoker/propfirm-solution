using Trading.Engine.Inputs;
using Trading.Engine.Internal;

namespace Trading.Engine;

/// <summary>
/// Values positions the way the engine does, from raw prices played in order, for reports after the fact: the equity
/// before a broken loss limit and an account's equity when an outage began (ADR 0053). It uses the engine's own price
/// book and valuation, so the amounts are the engine's for the same prices and conditions.
/// </summary>
public sealed class Revaluation
{
    private readonly PriceBook _prices;
    private readonly Valuation _valuation;

    public Revaluation(EngineConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _prices = new PriceBook(configuration.Instruments);
        _valuation = new Valuation(_prices, configuration.CurrencyDecimals);
    }

    /// <summary>The latest raw price of its symbol from now on, as when the engine receives it.</summary>
    public void Update(Quote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        _prices.Update(quote);
    }

    /// <summary>
    /// The balance plus every position's profit at the price it would close at now, in <paramref name="currency"/>.
    /// Null when a position's symbol has no price yet, or its profit no conversion rate.
    /// </summary>
    public decimal? Equity(decimal balance, IEnumerable<RevaluedPosition> positions, string currency)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(currency);
        var equity = balance;
        foreach (var position in positions)
        {
            if (!_prices.TryGetLatest(position.Instrument.Symbol, out var quote) || !_prices.TryGetRate(position.Instrument.QuoteCurrency, currency, out _))
            {
                return null;
            }

            var closePrice = PriceBook.ToClientPrice(position.Instrument, position.Conditions, quote).ClosePrice(position.Side);
            equity += _valuation.Profit(position.Instrument, position.Side, position.OpenPrice, closePrice, position.Volume, currency);
        }

        return equity;
    }
}

/// <summary>An open position as <see cref="Revaluation"/> values it, with the conditions of its group at the time.</summary>
public sealed record RevaluedPosition(Instrument Instrument, SymbolConditions Conditions, Side Side, decimal Volume, decimal OpenPrice);
