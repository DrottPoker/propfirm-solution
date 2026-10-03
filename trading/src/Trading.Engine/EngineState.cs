using Trading.Engine.Inputs;

namespace Trading.Engine;

/// <summary>
/// The complete engine state, for snapshots. An engine restored from it behaves exactly like the
/// engine it was exported from: the same inputs give the same events.
/// </summary>
/// <param name="Clock">Timestamp of the last input. Later inputs may not be earlier.</param>
/// <param name="LatestQuotes">The latest raw price per symbol.</param>
/// <param name="Accounts">In creation order, which is the order accounts are processed in.</param>
/// <param name="Groups">
/// Groups created with <see cref="CreateGroup"/>, in creation order. Configured groups are not included.
/// Missing in snapshots taken before groups could be created.
/// </param>
public sealed record EngineState(
    DateTimeOffset Clock,
    IReadOnlyList<Quote> LatestQuotes,
    IReadOnlyList<AccountRecord> Accounts,
    IReadOnlyList<TradingGroup>? Groups = null);

/// <param name="UsedOperationIds">Missing in snapshots taken before balance operations existed.</param>
public sealed record AccountRecord(
    string AccountId,
    string GroupId,
    decimal Balance,
    AccountStatus Status,
    IReadOnlyList<PositionRecord> Positions,
    IReadOnlyList<OrderRecord> Orders,
    IReadOnlyList<FloorRecord> Floors,
    IReadOnlyList<string> UsedOrderIds,
    IReadOnlyList<string>? UsedOperationIds = null);

public sealed record PositionRecord(
    string PositionId,
    string Symbol,
    Side Side,
    decimal Volume,
    decimal OpenPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    DateTimeOffset OpenTime);

public sealed record OrderRecord(
    string OrderId,
    string Symbol,
    Side Side,
    OrderType Type,
    decimal Volume,
    decimal Price,
    decimal? StopLoss,
    decimal? TakeProfit,
    DateTimeOffset PlacedTime);

/// <summary><paramref name="Anchor"/> is set for an <see cref="AnchoredFloor"/> only.</summary>
public sealed record FloorRecord(string FloorId, EquityFloorRule Rule, decimal HighWaterMark, decimal? Anchor = null);
