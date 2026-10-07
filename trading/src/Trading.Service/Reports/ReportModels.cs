using Trading.Engine;
using Trading.Service.Engine;

namespace Trading.Service.Reports;

/// <summary>
/// What a trade was and the prices behind it (ADR 0053): the limit or stop order it came from, its opening and each
/// close. <paramref name="Profit"/> is the closes' profit before commission so far, and <paramref name="Result"/> the
/// profit after all commission once the position is closed. Amounts are in <paramref name="Currency"/>.
/// </summary>
public sealed record TradeReceipt(
    string AccountId,
    string PositionId,
    string Symbol,
    Side Side,
    int Digits,
    string Currency,
    ReceiptOrder? Order,
    ReceiptFill Opened,
    IReadOnlyList<ReceiptClose> Closes,
    decimal Commission,
    decimal? Profit,
    decimal? Result);

/// <summary>The limit or stop order a position came from, as it was placed.</summary>
public sealed record ReceiptOrder(DateTimeOffset PlacedAt, OrderType Type, decimal Price);

/// <summary>
/// One fill: the opening, a part closed or the close. <paramref name="Price"/> is the price the account got, and
/// <paramref name="Feed"/> the raw price the engine had from the feed at that moment, with
/// <paramref name="MarkupPoints"/> between them on the filled side: the ask for a buy's opening and a sell's close, the
/// bid otherwise. Both are null when the price cannot be found. <paramref name="Prices"/> are the feed's prices a minute
/// before and after.
/// </summary>
public sealed record ReceiptFill(
    DateTimeOffset At,
    decimal Volume,
    decimal Price,
    decimal Commission,
    FeedPrice? Feed,
    int? MarkupPoints,
    IReadOnlyList<PricePoint> Prices);

/// <summary>
/// A part closed or the close: its fill, its profit before commission, why it closed and, for a stop loss or take
/// profit, the level it was set at when known.
/// </summary>
public sealed record ReceiptClose(ReceiptFill Fill, decimal Profit, CloseReason Reason, decimal? Level);

/// <summary>A raw price from the feed as the journal recorded it, with its entry in the journal.</summary>
public sealed record FeedPrice(long InputSequence, string? Feed, decimal Bid, decimal Ask, DateTimeOffset ReceivedAt);

/// <summary>A raw price from the feed at a moment.</summary>
public sealed record PricePoint(DateTimeOffset Time, decimal Bid, decimal Ask);

/// <summary>
/// Why a loss limit was broken (ADR 0053): the limit, the equity and the open positions at that moment, the prices
/// that broke it with the feed's prices behind them, equity price by price before it, the periods without prices and
/// the account's events, ending with the closes and the end of trading. <paramref name="Balance"/> is the balance
/// before the closes and <paramref name="BalanceAfter"/> after them.
/// </summary>
public sealed record BreachReport(
    string AccountId,
    string Currency,
    DateTimeOffset At,
    string FloorId,
    decimal Level,
    decimal Equity,
    decimal Balance,
    decimal BalanceAfter,
    IReadOnlyList<BreachPrice> Prices,
    IReadOnlyList<PositionSnapshot> Positions,
    IReadOnlyList<EquityPoint> EquityCurve,
    IReadOnlyList<PriceGap> Gaps,
    IReadOnlyList<EventEnvelope> Events);

/// <summary>
/// A price the account saw when the limit was broken, received at <paramref name="ReceivedAt"/>, with the feed's raw
/// price and the markup between them, or null when they cannot be found.
/// </summary>
public sealed record BreachPrice(
    string Symbol,
    decimal Bid,
    decimal Ask,
    DateTimeOffset ReceivedAt,
    FeedPrice? Feed,
    int? BidMarkupPoints,
    int? AskMarkupPoints);

/// <summary>The account's equity at a price.</summary>
public sealed record EquityPoint(DateTimeOffset Time, decimal Equity);

/// <summary>A period of at least a minute in which no price came for the symbols the account traded.</summary>
public sealed record PriceGap(DateTimeOffset From, DateTimeOffset To);

/// <summary>What happened to a firm's accounts in a period, such as an outage of the price feed (ADR 0053).</summary>
public sealed record IncidentImpact(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<AccountImpact> Accounts);

/// <summary>
/// An account with positions open when the period began, with commands refused during it for lack of a fresh price, or
/// with a loss limit broken during it or in the half hour after. <paramref name="EquityAtStart"/> is the equity when
/// the period began at the prices of that moment, or null when a price was missing. <paramref name="Status"/>,
/// <paramref name="Balance"/> and <paramref name="Equity"/> are as the account is now.
/// </summary>
public sealed record AccountImpact(
    string AccountId,
    string Currency,
    int OpenPositions,
    decimal BalanceAtStart,
    decimal? EquityAtStart,
    int OrdersRefused,
    int ClosesRefused,
    int ChangesRefused,
    ImpactBreach? Breach,
    AccountStatus Status,
    decimal Balance,
    decimal Equity);

/// <summary>A loss limit broken during the period or soon after.</summary>
public sealed record ImpactBreach(DateTimeOffset At, string FloorId, decimal Level, decimal Equity);

/// <summary>How the platform's price feed is doing (ADR 0053): when the last price came for each symbol, and whether its market is open.</summary>
public sealed record PriceFeedStatus(string Feed, DateTimeOffset Now, DateTimeOffset? LastPriceAt, IReadOnlyList<SymbolFeedStatus> Symbols);

public sealed record SymbolFeedStatus(string Symbol, DateTimeOffset? LastPriceAt, bool MarketOpen);
