namespace Trading.Engine;

/// <summary>Account state valued at the latest prices. Amounts are in account currency.</summary>
public sealed record AccountSnapshot(
    string AccountId,
    string GroupId,
    string Currency,
    AccountStatus Status,
    decimal Balance,
    decimal Equity,
    decimal UsedMargin,
    decimal FreeMargin,
    decimal? MarginLevelPercent,
    IReadOnlyList<PositionSnapshot> Positions,
    IReadOnlyList<OrderSnapshot> Orders,
    IReadOnlyList<FloorSnapshot> Floors);

/// <summary>An open position. CurrentPrice is the price it would close at now.</summary>
public sealed record PositionSnapshot(
    string PositionId,
    string Symbol,
    Side Side,
    decimal Volume,
    decimal OpenPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    DateTimeOffset OpenTime,
    decimal CurrentPrice,
    decimal Profit,
    decimal Margin);

public sealed record OrderSnapshot(
    string OrderId,
    string Symbol,
    Side Side,
    OrderType Type,
    decimal Volume,
    decimal Price,
    decimal? StopLoss,
    decimal? TakeProfit,
    DateTimeOffset PlacedTime);

/// <summary>An equity floor. Headroom is how far equity can fall before the floor is breached.</summary>
public sealed record FloorSnapshot(string FloorId, EquityFloorRule Rule, decimal Level, decimal HighWaterMark, decimal Headroom);

/// <summary>
/// What a one point move on one lot of the symbol is worth in the account currency, at the latest conversion rate.
/// Profit is points times volume times <paramref name="PerLot"/>, before rounding and commission.
/// </summary>
public sealed record PointValue(string Symbol, string Currency, decimal PerLot);

/// <summary>Price after the group's spread markup, with the time the raw price arrived.</summary>
public sealed record SymbolPrice(string Symbol, decimal Bid, decimal Ask, DateTimeOffset Timestamp);
