using Trading.Engine;
using Trading.Service.Configuration;
using Trading.Service.Engine;
using Trading.Service.Identity;

namespace Trading.Service.Api;

/// <summary>
/// Places an order. The client creates the order id, so a retry can never place the order twice. With
/// <paramref name="TrailingStop"/> the stop loss follows the price at the distance it is set at.
/// </summary>
public sealed record PlaceOrderRequest(
    string OrderId,
    string Symbol,
    Side Side,
    OrderType Type,
    decimal Volume,
    decimal? Price = null,
    decimal? StopLoss = null,
    decimal? TakeProfit = null,
    bool TrailingStop = false);

/// <summary>A position's stops. With <paramref name="TrailingStop"/> the stop loss follows the price at the distance it is set at.</summary>
public sealed record ModifyStopsRequest(decimal? StopLoss, decimal? TakeProfit, bool TrailingStop = false);

/// <summary>A pending order's new price and stops.</summary>
public sealed record ModifyOrderRequest(decimal Price, decimal? StopLoss, decimal? TakeProfit, bool TrailingStop = false);

/// <summary>Closes only <paramref name="Volume"/> of the position. Without it, the whole position.</summary>
public sealed record ClosePositionRequest(decimal? Volume = null);

/// <summary>Closes the positions in <paramref name="Symbol"/>. Without it, every position.</summary>
public sealed record CloseAllPositionsRequest(string? Symbol = null);

/// <summary>Creates a trading account owned by a user of the same firm.</summary>
public sealed record CreateAccountRequest(string AccountId, string GroupId, decimal InitialBalance, Guid OwnerUserId);

public sealed record CreateUserRequest(string? Email, string? Password);

public sealed record UserResponse(Guid UserId, string Email);

public sealed record SetFloorRequest(EquityFloorRule Rule);

/// <summary>
/// Deposits a positive <paramref name="Amount"/> or withdraws a negative one. The caller chooses
/// <paramref name="OperationId"/>, unique per account, so a retry is answered 409 instead of being applied
/// twice. A withdrawal must leave at least <paramref name="MinBalance"/>.
/// </summary>
public sealed record BalanceOperationRequest(string? OperationId, decimal Amount, decimal? MinBalance = null);

public sealed record SetPasswordRequest(string? Password);

/// <summary>The balance a disabled account opens again with (ADR 0053).</summary>
public sealed record ReopenAccountRequest(decimal Balance);

/// <summary>
/// The account's rules as the firm's system sees them now, which the terminal shows and warns about (ADR 0052). Every
/// field is replaced, and one left out is cleared. See <see cref="AccountRules"/>.
/// </summary>
public sealed record AccountRulesRequest(
    bool Funded = false,
    int? TradingDaysRequired = null,
    int? TradingDaysCounted = null,
    DateTimeOffset? PassBy = null,
    DateTimeOffset? OpenPositionBy = null,
    decimal? ConsistencyPercent = null,
    decimal? BestDayPercent = null)
{
    /// <summary>What is wrong with the rules, or null.</summary>
    public string? Problem() =>
        TradingDaysRequired is < 1 or > 1_000 ? "The trading days required must be 1 to 1000, or empty for no minimum."
        : TradingDaysCounted is < 0 ? "The trading days counted cannot be negative."
        : ConsistencyPercent is <= 0 or > 100 ? "The consistency rule must be above 0 and at most 100 percent, or empty for no rule."
        : BestDayPercent is < 0 ? "The best day's share cannot be negative."
        : null;

    public AccountRules ToRules() =>
        new(Funded, TradingDaysRequired, TradingDaysCounted, PassBy?.ToUniversalTime(), OpenPositionBy?.ToUniversalTime(), ConsistencyPercent, BestDayPercent);
}

/// <summary>
/// What the terminal shows about an account: <paramref name="Label"/> names it for the trader, <paramref name="ProfitTarget"/>
/// is the balance that passes it, <paramref name="TimeZone"/> its trading day's (IANA) and <paramref name="DetailsUrl"/> where
/// the trader sees more about it. Empty for none.
/// </summary>
public sealed record AccountDetailsRequest(string? Label, decimal? ProfitTarget, string? TimeZone, string? DetailsUrl)
{
    public const int MaxLabelLength = 100;
}

/// <summary>
/// A notice for the firm's terminals (ADR 0053): a short title, the text, how serious it is and where the trader
/// reads more, such as the firm's status page.
/// </summary>
public sealed record TerminalNoticeRequest(string? Title, string? Text, NoticeLevel Level = NoticeLevel.Info, string? Url = null)
{
    public const int MaxTitleLength = 120;
    public const int MaxTextLength = 1_000;

    /// <summary>What is wrong with the notice, or null.</summary>
    public string? Problem() =>
        string.IsNullOrWhiteSpace(Title) || Title.Length > MaxTitleLength ? $"The title must have 1 to {MaxTitleLength} characters."
        : string.IsNullOrWhiteSpace(Text) || Text.Length > MaxTextLength ? $"The text must have 1 to {MaxTextLength} characters."
        : Url is not null && !(Uri.TryCreate(Url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)) ? "The address must be an absolute http or https address."
        : null;

    public TerminalNotice ToNotice(DateTimeOffset now) => new(Title!.Trim(), Text!.Trim(), Level, Url is null ? null : new Uri(Url), now);
}

/// <summary>The firm's notice for its terminals, or null when there is none.</summary>
public sealed record NoticeResponse(TerminalNotice? Notice);

/// <summary>A login link for the user. With an account id, the terminal opens that account.</summary>
public sealed record CreateLoginLinkRequest(string? AccountId);

/// <summary>Open <paramref name="Url"/> once before <paramref name="ExpiresAt"/> to be logged in to the terminal.</summary>
public sealed record LoginLinkResponse(Uri Url, DateTimeOffset ExpiresAt);

/// <summary>The firm's events in order. Ask again with <c>after</c> set to <paramref name="Cursor"/> for the next ones.</summary>
public sealed record FirmEventsResponse(IReadOnlyList<EventEnvelope> Events, long Cursor);

/// <summary>The events a command caused, in order.</summary>
public sealed record CommandResponse(IReadOnlyList<EventEnvelope> Events);

/// <summary>An instrument on the platform. <paramref name="ContractSize"/> is the units of the base currency in one lot.</summary>
public sealed record PlatformInstrument(
    string Symbol,
    InstrumentCategory Category,
    string BaseCurrency,
    string QuoteCurrency,
    decimal ContractSize,
    int Digits,
    decimal VolumeMin,
    decimal VolumeStep,
    decimal VolumeMax)
{
    public static PlatformInstrument From(Instrument instrument, InstrumentCategory category) =>
        new(
            instrument.Symbol,
            category,
            instrument.BaseCurrency,
            instrument.QuoteCurrency,
            instrument.ContractSize,
            instrument.Digits,
            instrument.VolumeMin,
            instrument.VolumeStep,
            instrument.VolumeMax);
}

/// <summary>
/// A symbol a group trades. Margin is the position's value divided by <paramref name="Leverage"/>. <paramref name="SpreadMarkupPoints"/>
/// are added to the price feed's spread, and <paramref name="CommissionPerLotPerSide"/> is charged in the account currency both on open
/// and on close.
/// </summary>
public sealed record GroupSymbol(string? Symbol, int Leverage, int SpreadMarkupPoints, decimal CommissionPerLotPerSide);

/// <summary>One of the firm's trading groups. Only a <paramref name="Changeable"/> group, created for the firm, can get other symbols.</summary>
public sealed record FirmGroupResponse(string Id, string Currency, decimal StopOutLevelPercent, bool Changeable, IReadOnlyList<GroupSymbol> Symbols)
{
    public static FirmGroupResponse From(TradingGroup group, bool changeable) =>
        new(
            group.Id,
            group.Currency,
            group.StopOutLevelPercent,
            changeable,
            [.. group.Symbols.Select(s => new GroupSymbol(s.Symbol, s.Leverage, s.SpreadMarkupPoints, s.CommissionPerLotPerSide))]);
}

/// <summary>Every symbol the group should trade, with its conditions. Symbols left out are removed.</summary>
public sealed record ChangeGroupSymbolsRequest(IReadOnlyList<GroupSymbol>? Symbols);
