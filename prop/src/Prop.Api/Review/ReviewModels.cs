using System.Text.Json;

using Prop.Api.Billing;
using Prop.Api.Firms;
using Prop.Api.Portal;
using Prop.Rules;

namespace Prop.Api.Review;

/// <summary>A document added to the application. Its content is fetched on its own.</summary>
public sealed record DocumentResponse(Guid Id, string FileName, string ContentType, int Size, DateTimeOffset UploadedAt)
{
    internal static DocumentResponse From(FirmDocument document) =>
        new(document.Id, document.FileName, document.ContentType, document.Size, document.UploadedAt);
}

/// <summary>The deposit for our review: what the firm paid, or else what it pays when it sends its application. Taken off the startup fee.</summary>
public sealed record DepositResponse(decimal Amount, string Currency, bool Paid);

/// <summary>
/// The firm's review for its admin panel. <paramref name="Message"/> is our latest word: the changes we need, or why
/// it was not approved. <paramref name="SubmitProblem"/> says why the application cannot be sent now, and
/// <paramref name="Problems"/> what is missing or wrong in each field of the saved application, while it can be changed.
/// <paramref name="EuCountries"/> are where the application gives a VAT number or says the company has none.
/// </summary>
public sealed record VerificationResponse(
    ReviewStatus Status,
    FirmApplication Application,
    IReadOnlyList<DocumentResponse> Documents,
    string? Message,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DecidedAt,
    DepositResponse Deposit,
    bool CanEdit,
    string? SubmitProblem,
    int MaxDocuments,
    int MaxDocumentBytes,
    IReadOnlyList<string> EuCountries,
    IReadOnlyList<FieldProblem> Problems);

/// <summary>Where the firm pays the deposit, or null when the application was sent at once.</summary>
public sealed record SubmitResponse(Uri? CheckoutUrl);

/// <summary>Our admin view's address: the platform's name, and where firms sign up when the platform has an address.</summary>
public sealed record OpsSiteResponse(string Name, Uri? SignupUrl);

/// <summary>The staff member who is logged in.</summary>
public sealed record OpsMeResponse(Guid UserId, string Email);

/// <summary>
/// How the firm pays us: its plan once it pays, its slots and those taken, the challenges paused, the deposit it paid,
/// when it went live, since when this month is unpaid, its next charge, its card, how many slots are added when the last
/// is taken, and its newest charges.
/// </summary>
public sealed record OpsBillingResponse(
    BillingPlan? Plan,
    SlotsResponse Slots,
    int PausedChallenges,
    decimal DepositPaid,
    string Currency,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? UnpaidSince,
    NextChargeResponse? NextCharge,
    CardResponse? Card,
    int? AutoExpandStep,
    IReadOnlyList<ChargeResponse> Charges);

/// <summary>One of the firm's administrators, and since when.</summary>
public sealed record OpsAdminResponse(string Email, DateTimeOffset Since);

/// <summary>One of our checks in the firm's review: whether it is ticked, by whom and when.</summary>
public sealed record ReviewCheckResponse(string Item, bool Done, string? DoneBy, DateTimeOffset? DoneAt)
{
    /// <summary>Every check of the review in its order, with those ticked.</summary>
    internal static IReadOnlyList<ReviewCheckResponse> From(IReadOnlyList<ReviewCheck> ticked) =>
    [
        .. ReviewChecks.All.Select(item => ticked.FirstOrDefault(c => c.Item == item) is { } check
            ? new ReviewCheckResponse(item, true, check.DoneBy, check.DoneAt)
            : new ReviewCheckResponse(item, false, null, null)),
    ];
}

/// <summary>Ticks or unticks one of our checks.</summary>
public sealed record ReviewCheckRequest(bool Done);

/// <summary>What the firm has tried: challenges started, purchases in its portal, payouts and challenges it has.</summary>
public sealed record OpsSandboxUseResponse(int Challenges, int Purchases, int Payouts, int OwnChallenges);

/// <summary>A payout the trader waits for, without who the trader is.</summary>
public sealed record OpsWaitingPayoutResponse(long AccountNumber, PayoutStatus Status, DateTimeOffset RequestedAt, DateTimeOffset? ApprovedAt, decimal Amount, string Currency);

/// <summary>
/// How the firm pays its traders: what waits for it to approve and to pay, what it paid in the last 30 days and how long
/// that took, and how many of the payouts decided in the last 90 days it rejected, beside the same for every firm.
/// <paramref name="Waiting"/> are the payouts traders wait for, the oldest first. One asked for more than
/// <paramref name="LateAfterDays"/> days ago is late.
/// </summary>
public sealed record OpsPayoutsResponse(
    PayoutSummaryResponse Summary,
    double? PlatformAverageDaysToPay,
    int RejectedLast90Days,
    int DecidedLast90Days,
    int PlatformRejectedLast90Days,
    int PlatformDecidedLast90Days,
    int LateAfterDays,
    IReadOnlyList<OpsWaitingPayoutResponse> Waiting);

/// <summary>How the firm is doing: its accounts in each group, its sales in the last 30 days, its pass rate in the last 90, and its payouts.</summary>
public sealed record OpsFirmFiguresResponse(AccountCountsResponse Accounts, SalesResponse Sales, PassRateResponse PassRate, OpsPayoutsResponse Payouts);

/// <summary>
/// Something that happened in the firm's review or with its suspension. <paramref name="Detail"/> has the message
/// or reason, and the application as it was sent.
/// </summary>
public sealed record OpsEventResponse(long Id, string Type, DateTimeOffset RecordedAt, string Actor, JsonElement? Detail)
{
    internal static OpsEventResponse From(FirmEvent firmEvent) =>
        new(
            firmEvent.Id,
            firmEvent.Type,
            firmEvent.RecordedAt,
            firmEvent.Actor,
            firmEvent.Detail is null ? null : JsonSerializer.Deserialize<JsonElement>(firmEvent.Detail));
}

/// <summary>
/// A firm for our staff: who it is and its administrators, its application and documents, our review and checks, what it
/// tried in the sandbox, how it pays us, how it is doing and pays its traders, its suspension and its events.
/// </summary>
public sealed record OpsFirmResponse(
    string Id,
    string Name,
    FirmStatus Status,
    bool Configured,
    DateTimeOffset CreatedAt,
    Uri PortalUrl,
    IReadOnlyList<OpsAdminResponse> Admins,
    ReviewStatus? Review,
    FirmApplication Application,
    IReadOnlyList<DocumentResponse> Documents,
    string? Message,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DecidedAt,
    string? DecidedBy,
    IReadOnlyList<ReviewCheckResponse> Checks,
    OpsSandboxUseResponse SandboxUse,
    OpsBillingResponse Billing,
    OpsFirmFiguresResponse Figures,
    SuspensionResponse? Suspension,
    IReadOnlyList<OpsEventResponse> Events);

/// <summary>Our word to the firm with a decision. Required when we ask for changes or reject.</summary>
public sealed record DecisionRequest(string? Message);

/// <summary>Why we suspend the firm. Its administrators see it.</summary>
public sealed record SuspendRequest(string? Reason);
