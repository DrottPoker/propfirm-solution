namespace Trading.Engine.Internal;

/// <summary>Profit, margin and equity in account currency. Amounts are rounded to the currency's decimals.</summary>
internal sealed class Valuation(PriceBook prices, IReadOnlyDictionary<string, int> currencyDecimals)
{
    private const int DefaultCurrencyDecimals = 2;

    public decimal Round(decimal amount, string currency) =>
        Math.Round(
            amount,
            currencyDecimals.TryGetValue(currency, out var decimals) ? decimals : DefaultCurrencyDecimals,
            MidpointRounding.AwayFromZero);

    public bool IsRounded(decimal amount, string currency) => Round(amount, currency) == amount;

    /// <summary>True if both profit (quote currency) and margin (base currency) can be converted to the account currency.</summary>
    public bool CanValue(Instrument instrument, string currency) =>
        prices.TryGetRate(instrument.QuoteCurrency, currency, out _)
        && prices.TryGetRate(instrument.BaseCurrency, currency, out _);

    public ClientPrice CurrentPrice(Instrument instrument, SymbolConditions conditions) =>
        prices.TryGetLatest(instrument.Symbol, out var quote)
            ? PriceBook.ToClientPrice(instrument, conditions, quote)
            : throw new InvalidOperationException($"No price for {instrument.Symbol}.");

    public decimal Profit(PositionState position, decimal closePrice, string currency)
    {
        var difference = position.Side == Side.Buy ? closePrice - position.OpenPrice : position.OpenPrice - closePrice;
        var inQuoteCurrency = difference * position.Volume * position.Instrument.ContractSize;
        return Round(inQuoteCurrency * Rate(position.Instrument.QuoteCurrency, currency), currency);
    }

    public decimal Margin(Instrument instrument, SymbolConditions conditions, decimal volume, string currency) =>
        Round(volume * instrument.ContractSize * Rate(instrument.BaseCurrency, currency) / conditions.Leverage, currency);

    public decimal Commission(SymbolConditions conditions, decimal volume, string currency) =>
        Round(volume * conditions.CommissionPerLotPerSide, currency);

    public AccountFigures Measure(AccountState account)
    {
        var currency = account.Group.Currency;
        var floatingProfit = 0m;
        var usedMargin = 0m;
        foreach (var position in account.Positions)
        {
            var closePrice = CurrentPrice(position.Instrument, position.Conditions).ClosePrice(position.Side);
            floatingProfit += Profit(position, closePrice, currency);
            usedMargin += Margin(position.Instrument, position.Conditions, position.Volume, currency);
        }

        return new AccountFigures(account.Balance, account.Balance + floatingProfit, usedMargin);
    }

    // Rates exist for every open position: they were checked when it opened, and prices are never removed.
    private decimal Rate(string from, string to) =>
        prices.TryGetRate(from, to, out var rate)
            ? rate
            : throw new InvalidOperationException($"No conversion rate from {from} to {to}.");
}

internal readonly record struct AccountFigures(decimal Balance, decimal Equity, decimal UsedMargin)
{
    public decimal FreeMargin => Equity - UsedMargin;

    public decimal? MarginLevelPercent => UsedMargin == 0m ? null : Equity / UsedMargin * 100m;
}
