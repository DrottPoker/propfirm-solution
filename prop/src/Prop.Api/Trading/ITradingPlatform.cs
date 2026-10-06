using Prop.Api.Firms;
using Prop.Rules;

namespace Prop.Api.Trading;

/// <summary>
/// The trading platform as the prop platform uses it, through its public admin API (ADR 0012). One
/// implementation per platform: ours now, others such as Match-Trader later. Every call acts for one firm.
/// </summary>
internal interface ITradingPlatform
{
    /// <summary>The trader's user on the firm's server. Created with the password if there is none yet.</summary>
    Task<Guid> EnsureUserAsync(FirmTrading firm, string email, string password, CancellationToken cancellationToken);

    /// <summary>Opens the account in the firm's group. Done if the firm already has it, so a retry is safe.</summary>
    Task OpenAccountAsync(FirmTrading firm, string accountId, decimal initialBalance, Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>What the terminal shows about the account. Replaces what it showed before.</summary>
    Task DescribeAccountAsync(FirmTrading firm, string accountId, TradingAccountDetails details, CancellationToken cancellationToken);

    /// <summary>Tells the terminal the account's rules as they stand now (ADR 0052).</summary>
    Task DescribeRulesAsync(FirmTrading firm, string accountId, TradingAccountRules rules, CancellationToken cancellationToken);

    /// <summary>Sets or replaces the floor. Done if the account is already disabled, since there is nothing left to protect.</summary>
    Task SetFloorAsync(FirmTrading firm, string accountId, string floorId, FloorSpec floor, CancellationToken cancellationToken);

    /// <summary>Closes the account. Done if it is already disabled.</summary>
    Task CloseAccountAsync(FirmTrading firm, string accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Stops new positions on the account; the trader can still close the open ones. Done if it is already
    /// suspended or disabled.
    /// </summary>
    Task SuspendAccountAsync(FirmTrading firm, string accountId, CancellationToken cancellationToken);

    /// <summary>Lets the trader open positions again. Done if the account is not suspended or is disabled.</summary>
    Task ResumeAccountAsync(FirmTrading firm, string accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Withdraws <paramref name="amount"/> (a positive number), keeping at least <paramref name="minBalance"/>.
    /// Done if the operation was already applied, so a retry is safe. Refused if too little is left.
    /// </summary>
    Task WithdrawAsync(FirmTrading firm, string accountId, string operationId, decimal amount, decimal minBalance, CancellationToken cancellationToken);

    /// <summary>Deposits the amount once: the same operation id again changes nothing.</summary>
    Task DepositAsync(FirmTrading firm, string accountId, string operationId, decimal amount, CancellationToken cancellationToken);

    /// <summary>The account valued at the latest prices, as the trader sees it. Null if the firm has no such account.</summary>
    Task<TradingAccountSnapshot?> GetAccountAsync(FirmTrading firm, string accountId, CancellationToken cancellationToken);

    /// <summary>The firm's events after the cursor, oldest first, waiting up to <paramref name="waitSeconds"/> for new ones.</summary>
    Task<TradingEventPage> ReadEventsAsync(FirmTrading firm, long after, int limit, int waitSeconds, CancellationToken cancellationToken);

    /// <summary>A one-time link that logs the user in to the terminal, optionally on one account.</summary>
    Task<TradingLoginLink> CreateLoginLinkAsync(FirmTrading firm, Guid userId, string? accountId, CancellationToken cancellationToken);

    /// <summary>Every instrument on the platform, which the firm's group can trade.</summary>
    Task<IReadOnlyList<TradingInstrument>> GetInstrumentsAsync(FirmTrading firm, CancellationToken cancellationToken);

    /// <summary>The firm's group with the symbols it trades and their conditions. Null if the firm has no such group.</summary>
    Task<TradingGroupConditions?> GetGroupAsync(FirmTrading firm, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the symbols the firm's group trades, at once also for open positions. Refused with the platform's reason, for
    /// example SymbolInUse for a symbol with open positions, or GroupNotChangeable for a group from the platform's configuration.
    /// </summary>
    Task SetGroupSymbolsAsync(FirmTrading firm, IReadOnlyList<TradingSymbolConditions> symbols, CancellationToken cancellationToken);
}

/// <summary>An instrument on the trading platform. <paramref name="ContractSize"/> is units of the base currency in one lot.</summary>
internal sealed record TradingInstrument(string Symbol, string BaseCurrency, string QuoteCurrency, decimal ContractSize, int Digits);

/// <summary>A symbol the group trades: its leverage, the points added to the spread and the commission per lot on each side.</summary>
internal sealed record TradingSymbolConditions(string Symbol, int Leverage, int SpreadMarkupPoints, decimal CommissionPerLotPerSide);

/// <summary>The firm's group. Only a <paramref name="Changeable"/> group, created for the firm, can get other symbols.</summary>
internal sealed record TradingGroupConditions(string Id, string Currency, bool Changeable, IReadOnlyList<TradingSymbolConditions> Symbols);

internal sealed record TradingEventPage(IReadOnlyList<TradingEvent> Events, long Cursor);

internal sealed record TradingAccountSnapshot(decimal Balance, decimal Equity, IReadOnlyList<TradingFloorSnapshot> Floors);

/// <summary>A floor and how far equity can fall before it is breached.</summary>
internal sealed record TradingFloorSnapshot(string FloorId, decimal Level, decimal Headroom);

internal sealed record TradingLoginLink(Uri Url, DateTimeOffset ExpiresAt);

/// <summary>An event from the trading platform. <paramref name="Raw"/> is the event as the platform sent it, kept as evidence.</summary>
internal abstract record TradingEvent(long Sequence, DateTimeOffset Time, string AccountId, string Raw);

internal sealed record TradingAccountCreated(long Sequence, DateTimeOffset Time, string AccountId, string Raw, decimal Balance)
    : TradingEvent(Sequence, Time, AccountId, Raw);

/// <summary>A position was opened. <paramref name="Commission"/> was charged for the opening, and is in the balance after.</summary>
internal sealed record TradingPositionOpened(
    long Sequence,
    DateTimeOffset Time,
    string AccountId,
    string Raw,
    string PositionId,
    string Symbol,
    TradeSide Side,
    decimal Volume,
    decimal OpenPrice,
    decimal Commission,
    decimal BalanceAfter)
    : TradingEvent(Sequence, Time, AccountId, Raw);

/// <summary>
/// A position was closed. <paramref name="Profit"/> is before commission, and <paramref name="Commission"/> was charged
/// for the closing. Both are in the balance after. <paramref name="Reason"/> is the platform's, such as Manual or StopLoss.
/// </summary>
internal sealed record TradingPositionClosed(
    long Sequence,
    DateTimeOffset Time,
    string AccountId,
    string Raw,
    string PositionId,
    string Symbol,
    TradeSide Side,
    decimal Volume,
    decimal OpenPrice,
    decimal ClosePrice,
    decimal Profit,
    decimal Commission,
    string Reason,
    decimal BalanceAfter)
    : TradingEvent(Sequence, Time, AccountId, Raw);

/// <summary>
/// Part of a position was closed, and <paramref name="RemainingVolume"/> stays open with the same id. <paramref name="Profit"/>
/// is the part's before commission, and <paramref name="Commission"/> was charged for closing the part. Both are in the
/// balance after.
/// </summary>
internal sealed record TradingPositionPartiallyClosed(
    long Sequence,
    DateTimeOffset Time,
    string AccountId,
    string Raw,
    string PositionId,
    decimal Volume,
    decimal RemainingVolume,
    decimal ClosePrice,
    decimal Profit,
    decimal Commission,
    decimal BalanceAfter)
    : TradingEvent(Sequence, Time, AccountId, Raw);

internal sealed record TradingFloorSet(long Sequence, DateTimeOffset Time, string AccountId, string Raw, string FloorId, decimal Level)
    : TradingEvent(Sequence, Time, AccountId, Raw);

internal sealed record TradingFloorBreached(long Sequence, DateTimeOffset Time, string AccountId, string Raw, string FloorId, decimal Level, decimal Equity)
    : TradingEvent(Sequence, Time, AccountId, Raw);

internal sealed record TradingAccountDisabled(long Sequence, DateTimeOffset Time, string AccountId, string Raw)
    : TradingEvent(Sequence, Time, AccountId, Raw);

/// <summary>A deposit (positive amount) or withdrawal (negative amount), for example a payout's.</summary>
internal sealed record TradingBalanceAdjusted(long Sequence, DateTimeOffset Time, string AccountId, string Raw, string OperationId, decimal Amount, decimal BalanceAfter)
    : TradingEvent(Sequence, Time, AccountId, Raw);

/// <summary>An event the prop platform does not act on. It only moves the cursor.</summary>
internal sealed record TradingOtherEvent(long Sequence, DateTimeOffset Time, string AccountId, string Raw)
    : TradingEvent(Sequence, Time, AccountId, Raw);

/// <summary>Whether a position was a buy or a sell.</summary>
public enum TradeSide
{
    Buy,
    Sell,
}

/// <summary>The trading platform could not be reached or failed. Worth trying again later.</summary>
internal sealed class TradingPlatformUnavailableException : Exception
{
    public TradingPlatformUnavailableException()
    {
    }

    public TradingPlatformUnavailableException(string message)
        : base(message)
    {
    }

    public TradingPlatformUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The trading platform refused the request. Trying again gives the same answer.</summary>
internal sealed class TradingPlatformRejectedException : Exception
{
    public TradingPlatformRejectedException()
    {
    }

    public TradingPlatformRejectedException(string message)
        : base(message)
    {
    }

    public TradingPlatformRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public TradingPlatformRejectedException(string message, string? reason)
        : base(message)
    {
        Reason = reason;
    }

    /// <summary>The platform's reason, such as InsufficientFunds, when it gave one.</summary>
    public string? Reason { get; }
}

/// <summary>
/// What the terminal shows about an account: <paramref name="Label"/> names it for the trader, <paramref name="ProfitTarget"/>
/// is the balance that passes the stage, <paramref name="TimeZone"/> is its trading day's and <paramref name="DetailsUrl"/>
/// its page in the firm's portal.
/// </summary>
internal sealed record TradingAccountDetails(string Label, decimal? ProfitTarget, string TimeZone, Uri DetailsUrl);

/// <summary>
/// The account's rules as they stand now, for the terminal (ADR 0052). <paramref name="Funded"/> counts the trading days
/// toward a payout. <paramref name="PassBy"/> is when the stage fails unless passed, and <paramref name="OpenPositionBy"/>
/// when the challenge ends unless a position is opened. <paramref name="BestDayPercent"/> is the best trading day's share
/// of the profit, which the consistency rule allows up to <paramref name="ConsistencyPercent"/>. Null for none.
/// </summary>
internal sealed record TradingAccountRules(
    bool Funded,
    int? TradingDaysRequired,
    int TradingDaysCounted,
    DateTimeOffset? PassBy,
    DateTimeOffset? OpenPositionBy,
    decimal? ConsistencyPercent,
    decimal? BestDayPercent);
