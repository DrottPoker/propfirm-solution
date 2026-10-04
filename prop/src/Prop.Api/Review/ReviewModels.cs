using System.Text.Json;

using Prop.Api.Billing;
using Prop.Api.Firms;

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
/// it was not approved. <paramref name="SubmitProblem"/> says why the application cannot be sent now.
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
    int MaxDocumentBytes);

/// <summary>Where the firm pays the deposit, or null when the application was sent at once.</summary>
public sealed record SubmitResponse(Uri? CheckoutUrl);

/// <summary>Our admin view's address: the platform's name.</summary>
public sealed record OpsSiteResponse(string Name);

/// <summary>The staff member who is logged in.</summary>
public sealed record OpsMeResponse(Guid UserId, string Email);

/// <summary>A firm in our staff's list, with its review and whether we suspended it.</summary>
public sealed record OpsFirmSummaryResponse(
    string Id,
    string Name,
    FirmStatus Status,
    bool Configured,
    DateTimeOffset CreatedAt,
    ReviewStatus? Review,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? SuspendedAt)
{
    internal static OpsFirmSummaryResponse From(ReviewedFirm firm) =>
        new(firm.Id, firm.Name, firm.Status, firm.Configured, firm.CreatedAt, firm.Review, firm.SubmittedAt, firm.SuspendedAt);
}

/// <summary>How the firm pays us, in short.</summary>
public sealed record OpsBillingResponse(
    BillingPlan? Plan,
    int? Slots,
    int OpenChallenges,
    decimal DepositPaid,
    string Currency,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? UnpaidSince);

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

/// <summary>A firm for our staff: who it is, its application and documents, our review, its billing, its suspension and its events.</summary>
public sealed record OpsFirmResponse(
    string Id,
    string Name,
    FirmStatus Status,
    bool Configured,
    DateTimeOffset CreatedAt,
    Uri PortalUrl,
    IReadOnlyList<string> Admins,
    ReviewStatus? Review,
    FirmApplication Application,
    IReadOnlyList<DocumentResponse> Documents,
    string? Message,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DecidedAt,
    string? DecidedBy,
    OpsBillingResponse Billing,
    SuspensionResponse? Suspension,
    IReadOnlyList<OpsEventResponse> Events);

/// <summary>Our word to the firm with a decision. Required when we ask for changes or reject.</summary>
public sealed record DecisionRequest(string? Message);

/// <summary>Why we suspend the firm. Its administrators see it.</summary>
public sealed record SuspendRequest(string? Reason);
