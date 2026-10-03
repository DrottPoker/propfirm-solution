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
    InvalidAmount,
    UnknownAccount,
    AccountDisabled,
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
