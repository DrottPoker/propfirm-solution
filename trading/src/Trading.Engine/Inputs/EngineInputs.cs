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

/// <summary>
/// Creates a trading group, for example for a firm that signed up. Its currency and stop out level never change
/// afterwards, but its symbols can (see <see cref="ChangeGroupSymbols"/>).
/// </summary>
public sealed record CreateGroup(DateTimeOffset Timestamp, TradingGroup Group)
    : EngineInput(Timestamp);

/// <summary>
/// Replaces the symbols and their conditions of a group created with <see cref="CreateGroup"/>, for example when
/// the firm changes its leverage or commission. Configured groups are changed in the configuration. Open positions
/// and pending orders take the new conditions at once. A symbol can only be removed while no account in the group
/// has a position or an order in it.
/// </summary>
public sealed record ChangeGroupSymbols(DateTimeOffset Timestamp, string GroupId, IReadOnlyList<SymbolConditions> Symbols)
    : EngineInput(Timestamp);

public sealed record CreateAccount(DateTimeOffset Timestamp, string AccountId, string GroupId, decimal InitialBalance)
    : EngineInput(Timestamp), IAccountCommand;

/// <summary>
/// Places an order. <paramref name="Price"/> is required for limit and stop orders and must be empty for market orders.
/// With <paramref name="TrailingStop"/> the stop loss follows the price at the distance it is set at: from the price the
/// position closes at now for a market order, and from the order price for a limit or stop order.
/// </summary>
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
    decimal? TakeProfit = null,
    bool TrailingStop = false)
    : EngineInput(Timestamp), IAccountCommand;

/// <summary>Moves a pending order to a new price, and sets its stop loss, take profit and trailing stop as for <see cref="PlaceOrder"/>.</summary>
public sealed record ModifyOrder(
    DateTimeOffset Timestamp,
    string AccountId,
    string OrderId,
    decimal Price,
    decimal? StopLoss,
    decimal? TakeProfit,
    bool TrailingStop = false)
    : EngineInput(Timestamp), IAccountCommand;

public sealed record CancelOrder(DateTimeOffset Timestamp, string AccountId, string OrderId)
    : EngineInput(Timestamp), IAccountCommand;

/// <summary>Closes a position, or only <paramref name="Volume"/> of it. The rest stays open with the same id.</summary>
public sealed record ClosePosition(DateTimeOffset Timestamp, string AccountId, string PositionId, decimal? Volume = null)
    : EngineInput(Timestamp), IAccountCommand;

/// <summary>
/// Closes every position on the account, or only those in <paramref name="Symbol"/>, at the same moment. Positions whose
/// market is closed or has no fresh price stay open. Pending orders stay.
/// </summary>
public sealed record CloseAllPositions(DateTimeOffset Timestamp, string AccountId, string? Symbol = null)
    : EngineInput(Timestamp), IAccountCommand;

/// <summary>
/// Sets stop loss and take profit. An empty value removes it. With <paramref name="TrailingStop"/> the stop loss
/// follows the price at the distance it is set at from the price the position closes at now.
/// </summary>
public sealed record ModifyPosition(
    DateTimeOffset Timestamp,
    string AccountId,
    string PositionId,
    decimal? StopLoss,
    decimal? TakeProfit,
    bool TrailingStop = false)
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
/// Stops new positions on the account, for example while the firm's bill is unpaid. Pending orders are
/// cancelled. Open positions stay: the owner can close them and change their stops, and stops and floors still hold.
/// </summary>
public sealed record SuspendAccount(DateTimeOffset Timestamp, string AccountId)
    : EngineInput(Timestamp), IAccountCommand;

/// <summary>Lets a suspended account trade again.</summary>
public sealed record ResumeAccount(DateTimeOffset Timestamp, string AccountId)
    : EngineInput(Timestamp), IAccountCommand;

/// <summary>
/// Opens a disabled account again with <paramref name="Balance"/>, for example when an outage broke a loss limit and the
/// firm reinstates the trader (ADR 0053). Its floors are removed, so the firm sets them again. Its positions and orders
/// were closed when it was disabled.
/// </summary>
public sealed record ReopenAccount(DateTimeOffset Timestamp, string AccountId, decimal Balance)
    : EngineInput(Timestamp), IAccountCommand;

/// <summary>
/// Deposits a positive <paramref name="Amount"/> or withdraws a negative one. The caller chooses
/// <paramref name="OperationId"/>, which is never reused on the account, so a retry cannot apply it twice.
/// A withdrawal must leave the balance at or above <paramref name="MinBalance"/>, fit in the free margin
/// and keep equity above every floor.
/// </summary>
public sealed record AdjustBalance(DateTimeOffset Timestamp, string AccountId, string OperationId, decimal Amount, decimal? MinBalance = null)
    : EngineInput(Timestamp), IAccountCommand;
