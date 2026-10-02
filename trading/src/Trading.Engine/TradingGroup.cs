namespace Trading.Engine;

/// <summary>Trading conditions shared by a group of accounts, set by the firm.</summary>
/// <param name="Currency">Account currency for all accounts in the group.</param>
/// <param name="StopOutLevelPercent">Positions are closed when the margin level falls below this. 0 disables stop out.</param>
/// <param name="Symbols">The symbols the group may trade, with their conditions.</param>
public sealed record TradingGroup(
    string Id,
    string Currency,
    decimal StopOutLevelPercent,
    IReadOnlyList<SymbolConditions> Symbols);

/// <summary>Conditions for one symbol within a group.</summary>
/// <param name="Leverage">Margin is the position value divided by this.</param>
/// <param name="SpreadMarkupPoints">Points added to the raw spread. The bid is lowered by half, rounded down, and the ask raised by the rest.</param>
/// <param name="CommissionPerLotPerSide">Commission in account currency, charged both on open and on close.</param>
public sealed record SymbolConditions(
    string Symbol,
    int Leverage,
    int SpreadMarkupPoints,
    decimal CommissionPerLotPerSide)
{
    /// <summary>Points the bid is lowered by.</summary>
    public int BidMarkupPoints => SpreadMarkupPoints / 2;

    /// <summary>Points the ask is raised by.</summary>
    public int AskMarkupPoints => SpreadMarkupPoints - BidMarkupPoints;
}
