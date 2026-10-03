namespace Trading.Engine.Inputs;

/// <summary>
/// Input to the engine. The timestamp is set by the service when the input arrives
/// and must never be earlier than the previous input.
/// </summary>
public abstract record EngineInput(DateTimeOffset Timestamp);

/// <summary>A command that acts on one account.</summary>
public interface IAccountCommand
{
    string AccountId { get; }
}

/// <summary>Raw price from the feed, before spread markup.</summary>
public sealed record Quote(DateTimeOffset Timestamp, string Symbol, decimal Bid, decimal Ask)
    : EngineInput(Timestamp);

public sealed record CreateAccount(DateTimeOffset Timestamp, string AccountId, string GroupId, decimal InitialBalance)
    : EngineInput(Timestamp), IAccountCommand;

/// <summary>Places an order. <paramref name="Price"/> is required for limit and stop orders and must be empty for market orders.</summary>
public sealed record PlaceOrder(
    DateTimeOffset Timestamp,
    string AccountId,
    string OrderId,
    string Symbol,
    Side Side,
    OrderType Type,
    decimal Volume,
    decimal? Price = null,
    decimal? StopLoss = null,
    decimal? TakeProfit = null)
    : EngineInput(Timestamp), IAccountCommand;

public sealed record CancelOrder(DateTimeOffset Timestamp, string AccountId, string OrderId)
    : EngineInput(Timestamp), IAccountCommand;

public sealed record ClosePosition(DateTimeOffset Timestamp, string AccountId, string PositionId)
    : EngineInput(Timestamp), IAccountCommand;

/// <summary>Sets stop loss and take profit. An empty value removes it.</summary>
public sealed record ModifyPosition(
    DateTimeOffset Timestamp,
    string AccountId,
    string PositionId,
    decimal? StopLoss,
    decimal? TakeProfit)
    : EngineInput(Timestamp), IAccountCommand;

/// <summary>Adds or replaces a named equity floor on the account.</summary>
public sealed record SetEquityFloor(DateTimeOffset Timestamp, string AccountId, string FloorId, EquityFloorRule Rule)
    : EngineInput(Timestamp), IAccountCommand;

public sealed record RemoveEquityFloor(DateTimeOffset Timestamp, string AccountId, string FloorId)
    : EngineInput(Timestamp), IAccountCommand;

/// <summary>Closes all positions at the latest prices, cancels all orders and disables the account.</summary>
public sealed record CloseAccount(DateTimeOffset Timestamp, string AccountId)
    : EngineInput(Timestamp), IAccountCommand;

/// <summary>
/// Deposits a positive <paramref name="Amount"/> or withdraws a negative one. The caller chooses
/// <paramref name="OperationId"/>, which is never reused on the account, so a retry cannot apply it twice.
/// A withdrawal must leave the balance at or above <paramref name="MinBalance"/>, fit in the free margin
/// and keep equity above every floor.
/// </summary>
public sealed record AdjustBalance(DateTimeOffset Timestamp, string AccountId, string OperationId, decimal Amount, decimal? MinBalance = null)
    : EngineInput(Timestamp), IAccountCommand;
