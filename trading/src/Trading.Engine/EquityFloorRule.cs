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
