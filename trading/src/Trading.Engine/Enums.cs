namespace Trading.Engine;

public enum Side
{
    Buy,
    Sell,
}

public enum OrderType
{
    Market,
    Limit,
    Stop,
}

public enum AccountStatus
{
    Active,

    /// <summary>No new positions or orders. Open positions can still be closed, and stops and floors still hold.</summary>
    Suspended,

    /// <summary>
    /// Closed: nothing can be done on the account, until the firm's system reopens it, for example after an outage broke a
    /// loss limit (ADR 0053).
    /// </summary>
    Disabled,
}

public enum CloseReason
{
    Manual,
    StopLoss,
    TakeProfit,
    StopOut,
    EquityFloor,
    AccountClosed,

    /// <summary>The trader's own daily loss limit or profit target was reached, or they locked the day and closed (ADR 0054).</summary>
    OwnLimit,
}

public enum CancelReason
{
    Manual,
    InsufficientMargin,
    EquityFloor,
    AccountClosed,
    AccountSuspended,

    /// <summary>New orders are locked until the next trading day (ADR 0054).</summary>
    TradingLocked,

    /// <summary>The order would open more positions today than the trader's own limit allows.</summary>
    TradeLimit,
}

public enum DisableReason
{
    EquityFloor,
    Closed,
}

public enum RejectReason
{
    /// <summary>The input timestamp is earlier than the previous input.</summary>
    OutOfOrder,
    InvalidId,
    DuplicateId,
    UnknownSymbol,
    InvalidQuote,
    UnknownGroup,

    /// <summary>A new group breaks a rule, for example an unknown symbol or a leverage of zero.</summary>
    InvalidGroup,

    /// <summary>The group comes from the configuration, so only the configuration changes it.</summary>
    GroupNotChangeable,

    /// <summary>A symbol cannot be removed from a group while an account in it has a position or an order in the symbol.</summary>
    SymbolInUse,
    InvalidAmount,
    UnknownAccount,
    AccountDisabled,

    /// <summary>The account is suspended: no new orders, and it cannot be suspended again.</summary>
    AccountSuspended,

    /// <summary>The account is not suspended, so there is nothing to resume.</summary>
    AccountNotSuspended,

    /// <summary>The account is not disabled, so there is nothing to reopen.</summary>
    AccountNotDisabled,

    /// <summary>The trader's own limit or lock stops new orders until the next trading day (ADR 0054).</summary>
    AccountLocked,

    /// <summary>The trader has opened as many positions today as their own limit allows.</summary>
    TradeLimitReached,

    /// <summary>The trader's own limits are not valid, for example a negative amount.</summary>
    InvalidLimits,

    /// <summary>The trading day's time zone is unknown.</summary>
    InvalidTradingDay,
    SymbolNotTradable,
    InvalidOrder,
    InvalidVolume,
    InvalidPrice,
    InvalidStopLoss,
    InvalidTakeProfit,
    NoPrice,
    StalePrice,

    /// <summary>The instrument's market is closed (see <see cref="TradingHours"/>).</summary>
    MarketClosed,
    NoConversionRate,
    InsufficientMargin,
    UnknownOrder,
    UnknownPosition,

    /// <summary>A trailing stop follows a stop loss, so it needs one.</summary>
    NoStopLoss,
    InvalidFloor,
    UnknownFloor,

    /// <summary>A withdrawal would go below the minimum balance, exceed the free margin or breach a floor.</summary>
    InsufficientFunds,
}
