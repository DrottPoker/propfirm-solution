using Trading.Engine.Inputs;

namespace Trading.Engine.Events;

/// <summary>Output from the engine. The timestamp is the timestamp of the input that caused it.</summary>
public abstract record EngineEvent(DateTimeOffset Timestamp);

/// <summary>An event that belongs to one account.</summary>
public interface IAccountEvent
{
    string AccountId { get; }
}

public sealed record InputRejected(DateTimeOffset Timestamp, EngineInput Input, RejectReason Reason)
    : EngineEvent(Timestamp);

/// <summary>A trading group was created. Its symbols are in symbol order.</summary>
public sealed record GroupCreated(DateTimeOffset Timestamp, TradingGroup Group)
    : EngineEvent(Timestamp);

/// <summary>A created group's symbols changed. Holds the whole group as it is now, with its symbols in symbol order.</summary>
public sealed record GroupSymbolsChanged(DateTimeOffset Timestamp, TradingGroup Group)
    : EngineEvent(Timestamp);

public sealed record AccountCreated(DateTimeOffset Timestamp, string AccountId, string GroupId, string Currency, decimal Balance)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>A limit or stop order was accepted and waits for its price. <paramref name="TrailingDistance"/> is set for a trailing stop.</summary>
public sealed record OrderPlaced(
    DateTimeOffset Timestamp,
    string AccountId,
    string OrderId,
    string Symbol,
    Side Side,
    OrderType Type,
    decimal Volume,
    decimal Price,
    decimal? StopLoss,
    decimal? TakeProfit,
    decimal? TrailingDistance = null)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>A pending order got a new price, stop loss, take profit or trailing stop.</summary>
public sealed record OrderModified(
    DateTimeOffset Timestamp,
    string AccountId,
    string OrderId,
    string Symbol,
    decimal Price,
    decimal? StopLoss,
    decimal? TakeProfit,
    decimal? TrailingDistance)
    : EngineEvent(Timestamp), IAccountEvent;

public sealed record OrderCancelled(DateTimeOffset Timestamp, string AccountId, string OrderId, CancelReason Reason)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>
/// A position was opened. The position id is the id of the order that opened it. <paramref name="TrailingDistance"/> is
/// set for a trailing stop.
/// </summary>
public sealed record PositionOpened(
    DateTimeOffset Timestamp,
    string AccountId,
    string PositionId,
    string Symbol,
    Side Side,
    decimal Volume,
    decimal OpenPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    decimal Commission,
    decimal BalanceAfter,
    decimal? TrailingDistance = null)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>
/// The trader set a position's stops. The moves a trailing stop makes on its own give no event: like a trailing floor's,
/// they follow from the prices, and the account shows where the stop loss is.
/// </summary>
public sealed record PositionModified(
    DateTimeOffset Timestamp,
    string AccountId,
    string PositionId,
    decimal? StopLoss,
    decimal? TakeProfit,
    decimal? TrailingDistance = null)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>
/// Part of a position was closed. <paramref name="Volume"/> is the part closed, and <paramref name="RemainingVolume"/>
/// stays open with the same id. Profit and commission are for the part, in account currency.
/// </summary>
public sealed record PositionPartiallyClosed(
    DateTimeOffset Timestamp,
    string AccountId,
    string PositionId,
    string Symbol,
    Side Side,
    decimal Volume,
    decimal RemainingVolume,
    decimal OpenPrice,
    decimal ClosePrice,
    decimal Profit,
    decimal Commission,
    CloseReason Reason,
    decimal BalanceAfter)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>A position was closed. Profit and commission are in account currency.</summary>
public sealed record PositionClosed(
    DateTimeOffset Timestamp,
    string AccountId,
    string PositionId,
    string Symbol,
    Side Side,
    decimal Volume,
    decimal OpenPrice,
    decimal ClosePrice,
    decimal Profit,
    decimal Commission,
    CloseReason Reason,
    decimal BalanceAfter)
    : EngineEvent(Timestamp), IAccountEvent;

public sealed record EquityFloorSet(DateTimeOffset Timestamp, string AccountId, string FloorId, EquityFloorRule Rule, decimal Level)
    : EngineEvent(Timestamp), IAccountEvent;

public sealed record EquityFloorRemoved(DateTimeOffset Timestamp, string AccountId, string FloorId)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>Equity fell below a floor. Holds the evidence at the moment of the breach, before positions are closed.</summary>
public sealed record EquityFloorBreached(
    DateTimeOffset Timestamp,
    string AccountId,
    string FloorId,
    decimal Level,
    decimal Equity,
    IReadOnlyList<SymbolPrice> Prices,
    IReadOnlyList<PositionSnapshot> Positions)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>The margin level fell below the stop out level. Positions are closed, largest loss first.</summary>
public sealed record StopOutTriggered(DateTimeOffset Timestamp, string AccountId, decimal Equity, decimal UsedMargin, decimal MarginLevelPercent)
    : EngineEvent(Timestamp), IAccountEvent;

public sealed record AccountDisabled(DateTimeOffset Timestamp, string AccountId, DisableReason Reason)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>The account takes no new positions or orders until it is resumed. Its pending orders were cancelled just before.</summary>
public sealed record AccountSuspended(DateTimeOffset Timestamp, string AccountId)
    : EngineEvent(Timestamp), IAccountEvent;

public sealed record AccountResumed(DateTimeOffset Timestamp, string AccountId)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>A disabled account is open again with the balance, without floors (ADR 0053).</summary>
public sealed record AccountReopened(DateTimeOffset Timestamp, string AccountId, decimal Balance)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>Money was deposited (positive amount) or withdrawn (negative amount). Not a trading result.</summary>
public sealed record BalanceAdjusted(DateTimeOffset Timestamp, string AccountId, string OperationId, decimal Amount, decimal BalanceAfter)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>The account's trading day now starts as told, the next time at <paramref name="NextDayStart"/> (ADR 0054).</summary>
public sealed record TradingDaySet(DateTimeOffset Timestamp, string AccountId, TradingDay Day, DateTimeOffset NextDayStart)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>
/// A trading day started on an account with own limits or a lock: the limits counted from <paramref name="DayStartBalance"/>,
/// <paramref name="Limits"/> are those for the day, and the day ends at <paramref name="NextDayStart"/>.
/// </summary>
public sealed record TradingDayStarted(DateTimeOffset Timestamp, string AccountId, decimal DayStartBalance, OwnLimits Limits, DateTimeOffset NextDayStart)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>The trader's own limits now, and <paramref name="Pending"/> from the next trading day when they loosened one (ADR 0054).</summary>
public sealed record OwnLimitsSet(DateTimeOffset Timestamp, string AccountId, OwnLimits Limits, OwnLimits? Pending)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>Equity reached the trader's own daily loss limit or profit target. Positions close and new orders lock right after.</summary>
public sealed record OwnLimitReached(DateTimeOffset Timestamp, string AccountId, LockReason Limit, decimal Level, decimal Equity)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>
/// New orders are locked until <paramref name="Until"/>, the start of the next trading day (ADR 0054). <paramref name="Limit"/>
/// is the own limit that was reached, null when the trader locked the day. <paramref name="DayResult"/> is equity at the
/// lock less the balance the day started with, and <paramref name="PositionsClosed"/> how many positions the lock closed.
/// </summary>
public sealed record TradingLocked(
    DateTimeOffset Timestamp,
    string AccountId,
    LockReason Reason,
    DateTimeOffset Until,
    decimal? Limit,
    decimal DayResult,
    int PositionsClosed)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>A new trading day started, or the firm reopened the account, so new orders are taken again.</summary>
public sealed record TradingUnlocked(DateTimeOffset Timestamp, string AccountId)
    : EngineEvent(Timestamp), IAccountEvent;
