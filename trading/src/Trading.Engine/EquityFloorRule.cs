namespace Trading.Engine;

/// <summary>
/// Rule for an equity floor. When equity falls below the floor, all positions are closed,
/// all pending orders are cancelled and the account is disabled.
/// </summary>
public abstract record EquityFloorRule;

/// <summary>A fixed floor in account currency.</summary>
public sealed record FixedFloor(decimal Level) : EquityFloorRule;

/// <summary>A floor that trails the highest observed equity by <paramref name="Distance"/> and stops rising at <paramref name="LockLevel"/>.</summary>
public sealed record TrailingFloor(decimal Distance, decimal? LockLevel = null) : EquityFloorRule;

/// <summary>
/// A floor <paramref name="Distance"/> below the account as it is when the floor is set: its balance, or
/// the higher of balance and equity. The level then stays fixed. Setting it again at the start of every
/// trading day gives a daily loss limit, with the level taken in the same step as the prices.
/// </summary>
public sealed record AnchoredFloor(decimal Distance, FloorAnchor Anchor) : EquityFloorRule;

/// <summary>What an <see cref="AnchoredFloor"/> is measured from when it is set.</summary>
public enum FloorAnchor
{
    /// <summary>The balance. Open losses count against the new level.</summary>
    Balance,

    /// <summary>The higher of balance and equity.</summary>
    HigherOfBalanceAndEquity,
}
