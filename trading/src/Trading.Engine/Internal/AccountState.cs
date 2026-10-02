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
    DateTimeOffset openTime)
{
    public string Id { get; } = id;

    public Instrument Instrument { get; } = instrument;

    public SymbolConditions Conditions { get; } = conditions;

    public Side Side { get; } = side;

    public decimal Volume { get; } = volume;

    public decimal OpenPrice { get; } = openPrice;

    public decimal? StopLoss { get; set; } = stopLoss;

    public decimal? TakeProfit { get; set; } = takeProfit;

    public DateTimeOffset OpenTime { get; } = openTime;
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
    DateTimeOffset placedTime)
{
    public string Id { get; } = id;

    public Instrument Instrument { get; } = instrument;

    public SymbolConditions Conditions { get; } = conditions;

    public Side Side { get; } = side;

    public OrderType Type { get; } = type;

    public decimal Volume { get; } = volume;

    public decimal Price { get; } = price;

    public decimal? StopLoss { get; } = stopLoss;

    public decimal? TakeProfit { get; } = takeProfit;

    public DateTimeOffset PlacedTime { get; } = placedTime;
}

internal sealed class FloorState(string id, EquityFloorRule rule, decimal highWaterMark)
{
    public string Id { get; } = id;

    public EquityFloorRule Rule { get; } = rule;

    public decimal HighWaterMark { get; private set; } = highWaterMark;

    public decimal Level => Rule switch
    {
        FixedFloor fixedFloor => fixedFloor.Level,
        TrailingFloor { LockLevel: { } lockLevel } trailing => Math.Min(HighWaterMark - trailing.Distance, lockLevel),
        TrailingFloor trailing => HighWaterMark - trailing.Distance,
        _ => throw new InvalidOperationException($"Unknown floor rule {Rule.GetType().Name}."),
    };

    public void Observe(decimal equity)
    {
        if (equity > HighWaterMark)
        {
            HighWaterMark = equity;
        }
    }
}
