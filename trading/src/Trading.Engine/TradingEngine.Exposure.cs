namespace Trading.Engine;

// What traders hold across every account, valued in one currency, for the platform's staff (ADR 0057).
public sealed partial class TradingEngine
{
    /// <summary>
    /// Every open position, ordered by account and then as the account holds them, with its value, open profit and margin
    /// in <paramref name="currency"/> at the latest prices. A figure is null while there is no rate to the currency.
    /// </summary>
    public IReadOnlyList<PositionValue> GetOpenPositions(string currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        var positions = new List<PositionValue>();
        foreach (var account in _accountsById.Values.OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            var accountCurrency = account.Group.Currency;
            var toCurrency = _prices.TryGetRate(accountCurrency, currency, out var rate) ? rate : (decimal?)null;
            foreach (var position in account.Positions)
            {
                var instrument = position.Instrument;

                // The value of what is held: the base currency amount, such as 100,000 EUR or 100 oz of gold.
                decimal? value = _prices.TryGetRate(instrument.BaseCurrency, currency, out var baseRate)
                    ? _valuation.Round(position.Volume * instrument.ContractSize * baseRate, currency)
                    : null;
                var closePrice = _valuation.CurrentPrice(instrument, position.Conditions).ClosePrice(position.Side);
                var profit = _valuation.Profit(position, closePrice, accountCurrency);
                var margin = _valuation.Margin(instrument, position.Conditions, position.Volume, accountCurrency);
                positions.Add(new PositionValue(
                    account.Id,
                    account.Group.Id,
                    position.Id,
                    instrument.Symbol,
                    position.Side,
                    position.Volume,
                    value,
                    toCurrency is { } p ? _valuation.Round(profit * p, currency) : null,
                    toCurrency is { } m ? _valuation.Round(margin * m, currency) : null));
            }
        }

        return positions;
    }

    /// <summary>How many accounts each group has in each status, by group id.</summary>
    public IReadOnlyDictionary<string, AccountCounts> GetAccountCounts() =>
        _accountsById.Values
            .GroupBy(a => a.Group.Id, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => new AccountCounts(
                    g.Count(a => a.Status == AccountStatus.Active),
                    g.Count(a => a.Status == AccountStatus.Suspended),
                    g.Count(a => a.Status == AccountStatus.Disabled)),
                StringComparer.Ordinal);
}

/// <summary>A group's accounts that trade, that are paused and that are closed.</summary>
public sealed record AccountCounts(int Active, int Suspended, int Disabled);

/// <summary>
/// An open position valued in one currency: <paramref name="Value"/> is what it holds, at the base currency's rate,
/// <paramref name="Profit"/> its open profit and <paramref name="Margin"/> the margin it uses. Each is null without a rate.
/// </summary>
public sealed record PositionValue(
    string AccountId,
    string GroupId,
    string PositionId,
    string Symbol,
    Side Side,
    decimal Volume,
    decimal? Value,
    decimal? Profit,
    decimal? Margin);
