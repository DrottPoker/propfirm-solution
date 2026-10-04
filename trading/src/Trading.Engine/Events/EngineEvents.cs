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

/// <summary>A limit or stop order was accepted and waits for its price.</summary>
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
    decimal? TakeProfit)
    : EngineEvent(Timestamp), IAccountEvent;

public sealed record OrderCancelled(DateTimeOffset Timestamp, string AccountId, string OrderId, CancelReason Reason)
    : EngineEvent(Timestamp), IAccountEvent;

/// <summary>A position was opened. The position id is the id of the order that opened it.</summary>
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
    decimal BalanceAfter)
    : EngineEvent(Timestamp), IAccountEvent;

public sealed record PositionModified(DateTimeOffset Timestamp, string AccountId, string PositionId, decimal? StopLoss, decimal? TakeProfit)
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

/// <summary>Money was deposited (positive amount) or withdrawn (negative amount). Not a trading result.</summary>
public sealed record BalanceAdjusted(DateTimeOffset Timestamp, string AccountId, string OperationId, decimal Amount, decimal BalanceAfter)
    : EngineEvent(Timestamp), IAccountEvent;
