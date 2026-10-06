namespace Trading.Engine.Internal;

internal sealed class AccountState(string id, GroupState group, decimal balance)
{
    public string Id { get; } = id;

    public GroupState Group { get; } = group;

    public decimal Balance { get; set; } = balance;

    public AccountStatus Status { get; set; } = AccountStatus.Active;

    // Lists keep insertion order, which keeps processing deterministic.
    public List<PositionState> Positions { get; } = [];

    public List<OrderState> Orders { get; } = [];

    public SortedDictionary<string, FloorState> Floors { get; } = new(StringComparer.Ordinal);

    // Order ids used by this account. Ids are never reused.
    public HashSet<string> UsedOrderIds { get; } = new(StringComparer.Ordinal);

    // Balance operation ids applied to this account, so a retried operation is never applied twice.
    public HashSet<string> UsedOperationIds { get; } = new(StringComparer.Ordinal);

    public bool HasExposure => Positions.Count > 0 || Orders.Count > 0;
}

internal sealed class PositionState(
    string id,
    Instrument instrument,
    SymbolConditions conditions,
    Side side,
    decimal volume,
    decimal openPrice,
    decimal? stopLoss,
    decimal? takeProfit,
    DateTimeOffset openTime,
    decimal? trailingDistance)
{
    public string Id { get; } = id;

    public Instrument Instrument { get; } = instrument;

    // Changes when the firm changes its group's conditions.
    public SymbolConditions Conditions { get; set; } = conditions;

    public Side Side { get; } = side;

    // Smaller after a partial close.
    public decimal Volume { get; set; } = volume;

    public decimal OpenPrice { get; } = openPrice;

    public decimal? StopLoss { get; set; } = stopLoss;

    public decimal? TakeProfit { get; set; } = takeProfit;

    public DateTimeOffset OpenTime { get; } = openTime;

    // Set when the stop loss trails the price at this distance.
    public decimal? TrailingDistance { get; set; } = trailingDistance;
}

internal sealed class OrderState(
    string id,
    Instrument instrument,
    SymbolConditions conditions,
    Side side,
    OrderType type,
    decimal volume,
    decimal price,
    decimal? stopLoss,
    decimal? takeProfit,
    DateTimeOffset placedTime,
    decimal? trailingDistance)
{
    public string Id { get; } = id;

    public Instrument Instrument { get; } = instrument;

    // Changes when the firm changes its group's conditions.
    public SymbolConditions Conditions { get; set; } = conditions;

    public Side Side { get; } = side;

    public OrderType Type { get; } = type;

    public decimal Volume { get; } = volume;

    // The price, stops and trailing stop change when the trader modifies the order.
    public decimal Price { get; set; } = price;

    public decimal? StopLoss { get; set; } = stopLoss;

    public decimal? TakeProfit { get; set; } = takeProfit;

    public DateTimeOffset PlacedTime { get; } = placedTime;

    // The distance the stop loss of the position it opens will trail at.
    public decimal? TrailingDistance { get; set; } = trailingDistance;
}

/// <summary><paramref name="anchor"/> is the starting point of an <see cref="AnchoredFloor"/>, taken when it was set.</summary>
internal sealed class FloorState(string id, EquityFloorRule rule, decimal highWaterMark, decimal? anchor)
{
    public string Id { get; } = id;

    public EquityFloorRule Rule { get; } = rule;

    public decimal HighWaterMark { get; private set; } = highWaterMark;

    public decimal? Anchor { get; private set; } = anchor;

    public decimal Level => LevelShiftedBy(0m);

    /// <summary>
    /// The level after <see cref="Shift"/> by <paramref name="amount"/>. Levels measured from the account move
    /// with it; a fixed level and a trailing floor's lock level stay where they are.
    /// </summary>
    public decimal LevelShiftedBy(decimal amount) => Rule switch
    {
        FixedFloor fixedFloor => fixedFloor.Level,
        TrailingFloor { LockLevel: { } lockLevel } trailing => Math.Min(HighWaterMark + amount - trailing.Distance, lockLevel),
        TrailingFloor trailing => HighWaterMark + amount - trailing.Distance,
        AnchoredFloor anchored => Anchor!.Value + amount - anchored.Distance,
        _ => throw new InvalidOperationException($"Unknown floor rule {Rule.GetType().Name}."),
    };

    public void Observe(decimal equity)
    {
        if (equity > HighWaterMark)
        {
            HighWaterMark = equity;
        }
    }

    /// <summary>A deposit or withdrawal is not a trading result, so the highest equity and the anchor move with the balance.</summary>
    public void Shift(decimal amount)
    {
        HighWaterMark += amount;
        if (Anchor is { } anchor)
        {
            Anchor = anchor + amount;
        }
    }
}
