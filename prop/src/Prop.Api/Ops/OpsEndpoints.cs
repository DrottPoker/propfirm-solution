using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Prop.Api.Api;
using Prop.Api.Billing;
using Prop.Api.Configuration;
using Prop.Api.Firms;
using Prop.Api.Portal;
using Prop.Api.Review;

namespace Prop.Api.Ops;

/// <summary>
/// Our own admin view (ADR 0021), on its own host: our staff log in, review the firms' applications, and suspend
/// firms. Under /api/portal so the portal passes it on, but only on <see cref="PlatformOptions.OpsUrl"/>.
/// </summary>
internal static class OpsEndpoints
{
    public const int EventsShown = 100;

    // Verified when the email is unknown, so a wrong email takes as long as a wrong password.
    private static readonly Lazy<string> UnknownStaffHash = new(() => new PasswordHasher<StaffUser>().HashPassword(null!, Guid.NewGuid().ToString()));

    public static IEndpointRouteBuilder MapOpsApi(this IEndpointRouteBuilder app)
    {
        // Only on our admin view's host, which OpsHost checks before anything else.
        var ops = app.MapGroup(OpsHost.PathPrefix).WithTags("Ops");
        ops.MapGet("", (IOptions<PlatformOptions> platform) => TypedResults.Ok(new OpsSiteResponse(platform.Value.Name)));
        ops.MapPost("/login", LoginAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        ops.MapPost("/logout", async (HttpContext context) =>
        {
            await StaffAuth.SignOutAsync(context);
            return TypedResults.NoContent();
        });

        var staff = ops.MapGroup("").RequireAuthorization(StaffAuth.Policy);
        staff.MapGet("/me", (ClaimsPrincipal principal) => TypedResults.Ok(new OpsMeResponse(PortalAuth.UserIdOf(principal), StaffAuth.EmailOf(principal))));
        staff.MapGet("/firms", ListFirmsAsync);
        staff.MapGet("/firms/{firmId}", GetFirmAsync);
        staff.MapGet("/firms/{firmId}/documents/{documentId:guid}", GetDocumentAsync);
        staff.MapPost(
            "/firms/{firmId}/approve",
            (string firmId, DecisionRequest request, ClaimsPrincipal principal, ReviewService reviews, OpsFirms view, CancellationToken cancellationToken) =>
                ActAsync(firmId, view, reviews.ApproveAsync(firmId, StaffAuth.EmailOf(principal), request.Message, cancellationToken), cancellationToken));
        staff.MapPost(
            "/firms/{firmId}/request-changes",
            (string firmId, DecisionRequest request, ClaimsPrincipal principal, ReviewService reviews, OpsFirms view, CancellationToken cancellationToken) =>
                ActAsync(firmId, view, reviews.RequestChangesAsync(firmId, StaffAuth.EmailOf(principal), request.Message, cancellationToken), cancellationToken));
        staff.MapPost(
            "/firms/{firmId}/reject",
            (string firmId, DecisionRequest request, ClaimsPrincipal principal, ReviewService reviews, OpsFirms view, CancellationToken cancellationToken) =>
                ActAsync(firmId, view, reviews.RejectAsync(firmId, StaffAuth.EmailOf(principal), request.Message, cancellationToken), cancellationToken));
        staff.MapPost(
            "/firms/{firmId}/suspend",
            (string firmId, SuspendRequest request, ClaimsPrincipal principal, ReviewService reviews, OpsFirms view, CancellationToken cancellationToken) =>
                ActAsync(firmId, view, reviews.SuspendAsync(firmId, StaffAuth.EmailOf(principal), request.Reason, cancellationToken), cancellationToken));
        staff.MapPost(
            "/firms/{firmId}/unsuspend",
            (string firmId, ClaimsPrincipal principal, ReviewService reviews, OpsFirms view, CancellationToken cancellationToken) =>
                ActAsync(firmId, view, reviews.UnsuspendAsync(firmId, StaffAuth.EmailOf(principal), cancellationToken), cancellationToken));
        return app;
    }

    private static async Task<Results<Ok<OpsMeResponse>, ProblemHttpResult>> LoginAsync(
        PortalLoginRequest request,
        HttpContext context,
        StaffUsers staff,
        IPasswordHasher<StaffUser> hasher,
        CancellationToken cancellationToken)
    {
        var user = await staff.FindByEmailAsync(request.Email ?? "", cancellationToken);
        var verified = hasher.VerifyHashedPassword(user!, user?.PasswordHash ?? UnknownStaffHash.Value, request.Password ?? "");
        if (user is null || verified == PasswordVerificationResult.Failed)
        {
            return AccountActions.Problem(StatusCodes.Status401Unauthorized, "Wrong email or password.");
        }

        await StaffAuth.SignInAsync(context, user);
        return TypedResults.Ok(new OpsMeResponse(user.Id, user.Email));
    }

    /// <summary>The firms that wait for us (the default), the suspended ones, or all.</summary>
    private static async Task<Ok<List<OpsFirmSummaryResponse>>> ListFirmsAsync(FirmFilter? filter, ReviewStore store, CancellationToken cancellationToken) =>
        TypedResults.Ok((await store.ListFirmsAsync(filter ?? FirmFilter.ToReview, cancellationToken)).Select(OpsFirmSummaryResponse.From).ToList());

    private static async Task<Results<Ok<OpsFirmResponse>, ProblemHttpResult>> GetFirmAsync(string firmId, OpsFirms view, CancellationToken cancellationToken) =>
        await view.GetAsync(firmId, cancellationToken) is { } firm ? TypedResults.Ok(firm) : UnknownFirm();

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetDocumentAsync(
        string firmId,
        Guid documentId,
        HttpContext context,
        ReviewService reviews,
        CancellationToken cancellationToken) =>
        await ReviewEndpoints.DocumentAsync(context, firmId, documentId, reviews, cancellationToken);

    // A decision or a suspension, answered with the firm as it is afterwards.
    private static async Task<Results<Ok<OpsFirmResponse>, ProblemHttpResult>> ActAsync(string firmId, OpsFirms view, Task<ReviewResult> action, CancellationToken cancellationToken) =>
        await action is ReviewResult.Refused refused
            ? ReviewEndpoints.ProblemOf(refused)
            : TypedResults.Ok((await view.GetAsync(firmId, cancellationToken))!);

    private static ProblemHttpResult UnknownFirm() => AccountActions.Problem(StatusCodes.Status404NotFound, "There is no such firm.");
}

/// <summary>A firm as our staff see it: who it is, its application, our review, its billing, its suspension and its events.</summary>
internal sealed class OpsFirms(
    FirmCatalog firms,
    ReviewService reviews,
    ReviewStore store,
    BillingService billing,
    SlotService slots,
    FirmAdmins admins)
{
    /// <summary>The firm, or null when there is no such firm.</summary>
    public async Task<OpsFirmResponse?> GetAsync(string firmId, CancellationToken cancellationToken)
    {
        if (firms.ById(firmId) is not { } firm)
        {
            return null;
        }

        var (createdAt, configured) = await store.FirmInfoAsync(firm.Id, cancellationToken);
        var review = await reviews.GetAsync(firm, cancellationToken);
        var documents = await store.ListDocumentsAsync(firm.Id, cancellationToken);
        var events = await store.ListEventsAsync(firm.Id, OpsEndpoints.EventsShown, cancellationToken);
        var usage = await slots.UsageAsync(firm, cancellationToken);
        FirmBilling? plan;
        decimal depositPaid;
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            plan = await BillingStore.GetBillingAsync(connection, firm.Id, forUpdate: false, cancellationToken);
            depositPaid = await BillingStore.DepositPaidAsync(connection, firm.Id, billing.Terms.Currency, cancellationToken);
        }

        return new OpsFirmResponse(
            firm.Id,
            firm.Name,
            firm.Status,
            configured,
            createdAt,
            firm.Portal.Url,
            [.. (await admins.ListAsync(firm.Id, cancellationToken)).Select(a => a.Email)],
            configured ? null : review.Status,
            review.Application,
            [.. documents.Select(DocumentResponse.From)],
            review.Message,
            review.SubmittedAt,
            review.DecidedAt,
            review.DecidedBy,
            new OpsBillingResponse(
                plan is { ActivatedAt: not null } or { Plan: BillingPlan.Complimentary } ? plan.Plan : null,
                usage.Slots,
                usage.Used,
                depositPaid,
                billing.Terms.Currency,
                plan?.ActivatedAt,
                plan?.UnpaidSince),
            firm.Suspension is { } suspension ? new SuspensionResponse(suspension.At, suspension.Reason) : null,
            [.. events.Select(OpsEventResponse.From)]);
    }
}
