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

    /// <summary>Closed for good: nothing can be done on the account.</summary>
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
}

public enum CancelReason
{
    Manual,
    InsufficientMargin,
    EquityFloor,
    AccountClosed,
    AccountSuspended,
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
    SymbolNotTradable,
    InvalidOrder,
    InvalidVolume,
    InvalidPrice,
    InvalidStopLoss,
    InvalidTakeProfit,
    NoPrice,
    StalePrice,
    NoConversionRate,
    InsufficientMargin,
    UnknownOrder,
    UnknownPosition,
    InvalidFloor,
    UnknownFloor,

    /// <summary>A withdrawal would go below the minimum balance, exceed the free margin or breach a floor.</summary>
    InsufficientFunds,
}
