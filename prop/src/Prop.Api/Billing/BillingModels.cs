using Prop.Api.Firms;
using Prop.Api.Review;

namespace Prop.Api.Billing;

/// <summary>
/// The firm's slots: <paramref name="Used"/> by challenges that have not ended and <paramref name="Reserved"/> by
/// orders waiting for payment. <paramref name="Slots"/> and <paramref name="Free"/> are null when there is no
/// limit. No challenge can start when nothing is free, <paramref name="Paid"/> is false or the firm is
/// <paramref name="Suspended"/>. <paramref name="Warning"/> is set when most slots are taken.
/// </summary>
public sealed record SlotsResponse(SlotLimit Limit, int? Slots, int Used, int Reserved, int? Free, bool Paid, bool Suspended, bool Warning)
{
    internal static SlotsResponse From(SlotUsage usage, int warningPercent) =>
        new(usage.Limit, usage.Slots, usage.Used, usage.Reserved, usage.Free, usage.Paid, usage.Suspended, usage.IsNearlyFull(warningPercent));
}

/// <summary>The saved card. Only its brand, last digits and expiry are known.</summary>
public sealed record CardResponse(string Brand, string Last4, int ExpMonth, int ExpYear);

/// <summary>
/// A charge. <paramref name="CanPay"/> is set for an unpaid monthly charge, which the firm can try again or pay
/// on a checkout page. <paramref name="Failure"/> is why the card was declined last.
/// </summary>
public sealed record ChargeResponse(
    Guid Id,
    long Number,
    ChargeKind Kind,
    ChargeStatus Status,
    DateOnly Month,
    int Slots,
    IReadOnlyList<ChargeLine> Lines,
    decimal Amount,
    string Currency,
    string? Failure,
    int Attempts,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    bool CanPay)
{
    internal static ChargeResponse From(Charge charge) =>
        new(
            charge.Id,
            charge.Number,
            charge.Kind,
            charge.Status,
            charge.Month,
            charge.Slots,
            charge.Lines,
            charge.Amount,
            charge.Currency,
            charge.Failure,
            charge.Attempts,
            charge.NextAttemptAt,
            charge.CreatedAt,
            charge.PaidAt,
            charge is { IsOpen: true, Kind: ChargeKind.Renewal });
}

/// <summary>The month charged next: when, for how many slots and how much.</summary>
public sealed record NextChargeResponse(DateOnly Month, DateTimeOffset ChargeAt, int Slots, decimal Amount);

/// <summary>Each slot's monthly price from slot number <paramref name="From"/> on.</summary>
public sealed record SlotPriceResponse(int From, decimal Price);

/// <summary>
/// What firms pay: the startup fee, the deposit for our review that is taken off it, the monthly package with the
/// slots it includes, which are the fewest a firm can have, the prices of slots beyond it and the rules for slots.
/// </summary>
public sealed record PricesResponse(
    string Currency,
    decimal StartupFee,
    decimal ReviewDeposit,
    decimal PackagePrice,
    int PackageSlots,
    IReadOnlyList<SlotPriceResponse> SlotPrices,
    int MaxSlots,
    int ChargeDaysBeforeMonth,
    int WarningPercent);

/// <summary>We suspended the firm, for a reason its administrators see.</summary>
public sealed record SuspensionResponse(DateTimeOffset At, string Reason);

/// <summary>
/// The firm's billing for its admin panel. <paramref name="Plan"/> is null until the firm has started paying.
/// <paramref name="NextMonthSlots"/> is what a paying firm is charged for from the next unpaid month.
/// <paramref name="GoLiveProblem"/> says why a firm in the sandbox cannot go live by paying now, and
/// <paramref name="Review"/> where our review of it is. <paramref name="DepositPaid"/> is taken off the startup fee.
/// <paramref name="Suspension"/> is set while we have suspended the firm.
/// </summary>
public sealed record BillingResponse(
    FirmStatus Status,
    BillingPlan? Plan,
    BillingProvider Provider,
    SlotsResponse Slots,
    int? NextMonthSlots,
    int? AutoExpandStep,
    CardResponse? Card,
    DateTimeOffset? UnpaidSince,
    NextChargeResponse? NextCharge,
    IReadOnlyList<ChargeResponse> Charges,
    PricesResponse Prices,
    string? GoLiveProblem,
    ReviewStatus? Review,
    decimal DepositPaid,
    SuspensionResponse? Suspension);

public enum QuoteKind
{
    /// <summary>Going live: the startup fee and the first month.</summary>
    Activation,

    /// <summary>More slots, paid now.</summary>
    MoreSlots,

    /// <summary>Fewer slots, from the next unpaid month. Nothing is paid now.</summary>
    FewerSlots,

    Unchanged,
}

/// <summary>
/// What choosing a number of slots would cost: <paramref name="Lines"/> paid now, and <paramref name="MonthlyPrice"/>
/// for each month from <paramref name="From"/>. <paramref name="Problem"/> says why it cannot be chosen.
/// </summary>
public sealed record QuoteResponse(
    QuoteKind Kind,
    int Slots,
    IReadOnlyList<ChargeLine> Lines,
    decimal Amount,
    decimal MonthlyPrice,
    DateOnly? From,
    string Currency,
    string? Problem);

/// <summary>Going live with this many slots, buying <paramref name="AutoExpandStep"/> more when the last is taken, or none for no automatic expansion.</summary>
public sealed record ActivateRequest(int Slots, int? AutoExpandStep);

public sealed record SlotsRequest(int Slots);

/// <summary>How many slots to buy when the last free one is taken, or null to turn it off.</summary>
public sealed record AutoExpandRequest(int? Step);

/// <summary>Where the firm goes to pay or to save its card.</summary>
public sealed record CheckoutResponse(Uri CheckoutUrl);

/// <summary>
/// A test checkout page in the portal: what it is for and, for a payment, what is paid. <paramref name="ReturnPath"/>
/// is the page in the admin panel the firm comes back to.
/// </summary>
public sealed record TestCheckoutResponse(
    string Id,
    CheckoutPurpose Purpose,
    CheckoutStatus Status,
    IReadOnlyList<ChargeLine> Lines,
    decimal Amount,
    string Currency,
    DateTimeOffset ExpiresAt,
    string ReturnPath);

/// <summary>Completes a test checkout page with a test card that pays, or one that declines.</summary>
public sealed record TestCheckoutRequest(bool Declines);
