using Trading.Engine;
using Trading.Service.Configuration;
using Trading.Service.Engine;

namespace Trading.Service.Api;

/// <summary>Places an order. The client creates the order id, so a retry can never place the order twice.</summary>
public sealed record PlaceOrderRequest(
    string OrderId,
    string Symbol,
    Side Side,
    OrderType Type,
    decimal Volume,
    decimal? Price = null,
    decimal? StopLoss = null,
    decimal? TakeProfit = null);

public sealed record ModifyStopsRequest(decimal? StopLoss, decimal? TakeProfit);

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

/// <summary>
/// What the terminal shows about an account: <paramref name="Label"/> names it for the trader, <paramref name="ProfitTarget"/>
/// is the balance that passes it, <paramref name="TimeZone"/> its trading day's (IANA) and <paramref name="DetailsUrl"/> where
/// the trader sees more about it. Empty for none.
/// </summary>
public sealed record AccountDetailsRequest(string? Label, decimal? ProfitTarget, string? TimeZone, string? DetailsUrl)
{
    public const int MaxLabelLength = 100;
}

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
