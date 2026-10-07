using System.Text.Json;

namespace Prop.Api.Trading;

// What the trading platform keeps as proof of a trade, a broken loss limit and an outage (ADR 0053), as the portal
// shows them. Sides, reasons and kinds are the platform's names, such as "Buy" and "StopLoss".

/// <summary>
/// What a trade was and the prices behind it: the limit or stop order it came from, its opening and each close.
/// <paramref name="Profit"/> is the closes' profit before commission, and <paramref name="Result"/> the profit after all
/// commission once the position is closed. Amounts are in <paramref name="Currency"/>.
/// </summary>
public sealed record TradeReceipt(
    string AccountId,
    string PositionId,
    string Symbol,
    string Side,
    int Digits,
    string Currency,
    ReceiptOrder? Order,
    ReceiptFill Opened,
    IReadOnlyList<ReceiptClose> Closes,
    decimal Commission,
    decimal? Profit,
    decimal? Result);

public sealed record ReceiptOrder(DateTimeOffset PlacedAt, string Type, decimal Price);

/// <summary>
/// One fill, with the raw price the platform had from the feed and the markup between them on the filled side, or null
/// when it is not known, and the feed's prices a minute before and after.
/// </summary>
public sealed record ReceiptFill(DateTimeOffset At, decimal Volume, decimal Price, decimal Commission, FeedPrice? Feed, int? MarkupPoints, IReadOnlyList<PricePoint> Prices);

/// <summary>A part closed or the close, why, and the stop loss or take profit level it closed at when known.</summary>
public sealed record ReceiptClose(ReceiptFill Fill, decimal Profit, string Reason, decimal? Level);

/// <summary>A raw price from the feed, with its entry in the platform's journal.</summary>
public sealed record FeedPrice(long InputSequence, string? Feed, decimal Bid, decimal Ask, DateTimeOffset ReceivedAt);

public sealed record PricePoint(DateTimeOffset Time, decimal Bid, decimal Ask);

/// <summary>
/// Why a loss limit was broken: the limit, the equity and the open positions at that moment, the prices that broke it with
/// the feed's prices behind them, equity price by price before it, the periods without prices, and what happened step by
/// step. <paramref name="Balance"/> is the balance before the positions closed and <paramref name="BalanceAfter"/> after.
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
    IReadOnlyList<BreachPosition> Positions,
    IReadOnlyList<EquityPoint> EquityCurve,
    IReadOnlyList<PriceGap> Gaps,
    IReadOnlyList<BreachStep> Steps);

public sealed record BreachPrice(string Symbol, decimal Bid, decimal Ask, DateTimeOffset ReceivedAt, FeedPrice? Feed, int? BidMarkupPoints, int? AskMarkupPoints);

public sealed record BreachPosition(string PositionId, string Symbol, string Side, decimal Volume, decimal OpenPrice, DateTimeOffset OpenTime, decimal CurrentPrice, decimal Profit);

public sealed record EquityPoint(DateTimeOffset Time, decimal Equity);

public sealed record PriceGap(DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// Something that happened on the account before and at the breach: a position opened, closed or partly closed, an order
/// placed, a request refused, a loss limit set or broken, money moved, or trading ended. <paramref name="Amount"/> is the
/// profit of a close, the equity at a breach or the amount moved; <paramref name="Reason"/> why a position closed, a
/// request was refused or trading ended, or the type of an order. <paramref name="Request"/> is what a refused request
/// was, such as "PlaceOrder" or "ClosePosition".
/// </summary>
public sealed record BreachStep(
    DateTimeOffset At,
    string Kind,
    string? Symbol,
    string? Side,
    decimal? Volume,
    decimal? Price,
    decimal? Amount,
    string? Reason,
    string? FloorId,
    decimal? Level,
    string? Request = null);

/// <summary>What a period such as an outage did to the firm's trading accounts.</summary>
internal sealed record TradingImpact(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<TradingAccountImpact> Accounts);

/// <summary>
/// A trading account with positions open when the period began, requests refused for lack of a fresh price, or a loss
/// limit broken during it or soon after, with its equity when the period began when known, and how it is now.
/// </summary>
internal sealed record TradingAccountImpact(
    string AccountId,
    int OpenPositions,
    decimal BalanceAtStart,
    decimal? EquityAtStart,
    int OrdersRefused,
    int ClosesRefused,
    int ChangesRefused,
    TradingImpactBreach? Breach,
    string Status,
    decimal Balance,
    decimal Equity);

internal sealed record TradingImpactBreach(DateTimeOffset At, string FloorId, decimal Level, decimal Equity);

/// <summary>A notice at the top of the firm's terminals. <paramref name="Url"/> is where traders read more.</summary>
internal sealed record TradingNotice(string Title, string Text, bool Warning, Uri? Url);

/// <summary>When the platform's last price came, and for each symbol when its last price came and whether its market is open.</summary>
internal sealed record PriceFeedStatus(string Feed, DateTimeOffset Now, DateTimeOffset? LastPriceAt, IReadOnlyList<SymbolFeedStatus> Symbols)
{
    /// <summary>Whether any market is open now, so prices are expected.</summary>
    public bool AnyMarketOpen => Symbols.Any(s => s.MarketOpen);
}

internal sealed record SymbolFeedStatus(string Symbol, DateTimeOffset? LastPriceAt, bool MarketOpen);

/// <summary>Reads the platform's reports into the records above.</summary>
internal static class TradingReportJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static T Read<T>(JsonElement json) => json.Deserialize<T>(Json) ?? throw new JsonException($"The trading platform sent an empty {typeof(T).Name}.");

    /// <summary>The breach report, with the platform's events read into steps. Events the portal does not show are left out.</summary>
    public static BreachReport ReadBreachReport(JsonElement json) =>
        new(
            json.GetProperty("accountId").GetString()!,
            json.GetProperty("currency").GetString()!,
            json.GetProperty("at").GetDateTimeOffset(),
            json.GetProperty("floorId").GetString()!,
            json.GetProperty("level").GetDecimal(),
            json.GetProperty("equity").GetDecimal(),
            json.GetProperty("balance").GetDecimal(),
            json.GetProperty("balanceAfter").GetDecimal(),
            Read<List<BreachPrice>>(json.GetProperty("prices")),
            Read<List<BreachPosition>>(json.GetProperty("positions")),
            Read<List<EquityPoint>>(json.GetProperty("equityCurve")),
            Read<List<PriceGap>>(json.GetProperty("gaps")),
            [.. json.GetProperty("events").EnumerateArray().Select(e => StepOf(e.GetProperty("event"))).OfType<BreachStep>()]);

    private static BreachStep? StepOf(JsonElement e)
    {
        var at = e.GetProperty("timestamp").GetDateTimeOffset();
        var kind = e.GetProperty("kind").GetString()!;
        string? Text(string name) => e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        decimal? Number(string name) => e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : null;
        return kind switch
        {
            "PositionOpened" => new BreachStep(at, kind, Text("symbol"), Text("side"), Number("volume"), Number("openPrice"), null, null, null, null),
            "PositionClosed" or "PositionPartiallyClosed" => new BreachStep(at, kind, Text("symbol"), Text("side"), Number("volume"), Number("closePrice"), Number("profit"), Text("reason"), null, null),
            "OrderPlaced" => new BreachStep(at, kind, Text("symbol"), Text("side"), Number("volume"), Number("price"), null, Text("type"), null, null),
            "InputRejected" => new BreachStep(
                at,
                kind,
                e.GetProperty("input").TryGetProperty("symbol", out var symbol) ? symbol.GetString() : null,
                null,
                null,
                null,
                null,
                Text("reason"),
                null,
                null,
                e.GetProperty("input").GetProperty("kind").GetString()),
            "BalanceAdjusted" => new BreachStep(at, kind, null, null, null, null, Number("amount"), null, null, null),
            "EquityFloorSet" => new BreachStep(at, kind, null, null, null, null, null, null, Text("floorId"), Number("level")),
            "EquityFloorBreached" => new BreachStep(at, kind, null, null, null, null, Number("equity"), null, Text("floorId"), Number("level")),
            "StopOutTriggered" => new BreachStep(at, kind, null, null, null, null, Number("equity"), null, null, null),
            "AccountDisabled" => new BreachStep(at, kind, null, null, null, null, null, Text("reason"), null, null),
            _ => null,
        };
    }
}
