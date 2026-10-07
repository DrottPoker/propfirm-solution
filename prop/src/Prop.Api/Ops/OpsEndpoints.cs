using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Prop.Api.Api;
using Prop.Api.Billing;
using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Firms;
using Prop.Api.Incidents;
using Prop.Api.Portal;
using Prop.Api.Review;
using Prop.Rules;

namespace Prop.Api.Ops;

/// <summary>
/// Our own admin view (ADRs 0021 and 0024), on its own host: our staff log in, see what waits for them across the firms,
/// review the firms' applications, and suspend firms. Under /api/portal so the portal passes it on, but only on
/// <see cref="PlatformOptions.OpsUrl"/>.
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
        ops.MapGet(
            "",
            (IOptions<PlatformOptions> platform) => TypedResults.Ok(new OpsSiteResponse(platform.Value.Name, platform.Value.Url is { } url ? new Uri(url, "signup") : null)));
        ops.MapPost("/login", LoginAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        ops.MapPost("/password-reset", RequestPasswordResetAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        ops.MapPost("/password-reset/check", CheckPasswordResetAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        ops.MapPost("/password-reset/confirm", ConfirmPasswordResetAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        ops.MapPost("/logout", async (HttpContext context) =>
        {
            await StaffAuth.SignOutAsync(context);
            return TypedResults.NoContent();
        });

        var staff = ops.MapGroup("").RequireAuthorization(StaffAuth.Policy);
        staff.MapGet("/me", (ClaimsPrincipal principal) => TypedResults.Ok(new OpsMeResponse(PortalAuth.UserIdOf(principal), StaffAuth.EmailOf(principal))));
        staff.MapOpsPanel();
        staff.MapOpsIncidents();
        staff.MapGet("/firms/{firmId}", GetFirmAsync);
        staff.MapPut("/firms/{firmId}/checks/{item}", SetCheckAsync);
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

    /// <summary>Emails one of our staff a link to choose a new password. Always 202, so the answer never tells who works here.</summary>
    private static async Task<Accepted> RequestPasswordResetAsync(
        PasswordResetRequest request,
        StaffUsers staff,
        PasswordResets resets,
        IOptions<PlatformOptions> platform,
        Npgsql.NpgsqlDataSource dataSource,
        Common.Postgres.DatabaseSchema schema,
        WorkSignals signals,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Email) && await staff.FindByEmailAsync(request.Email, cancellationToken) is { } user && platform.Value.OpsUrl is { } opsUrl)
        {
            var now = time.GetUtcNow();
            var token = await resets.CreateAsync(PasswordResetKinds.Staff, user.Id, now, cancellationToken);
            var message = Email.PlatformEmails.ResetStaffPassword(platform.Value.Name, user.Email, new Uri(opsUrl, $"ops/reset-password?token={token}"), PasswordResets.Lifetime);
            await Email.EmailOutbox.AddAsync(dataSource, schema, signals, message, "password_reset", null, now, cancellationToken);
        }

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Ok<LinkCheckResponse>, ProblemHttpResult>> CheckPasswordResetAsync(
        LinkCheckRequest request,
        StaffUsers staff,
        PasswordResets resets,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        !string.IsNullOrEmpty(request.Token)
        && await resets.FindAsync(PasswordResetKinds.Staff, request.Token, time.GetUtcNow(), cancellationToken) is { } link
        && await staff.FindByIdAsync(link.UserId, cancellationToken) is { } user
            ? TypedResults.Ok(new LinkCheckResponse(link.Status, user.Email, HasPassword: true))
            : PasswordResetEndpoints.UnknownLink();

    /// <summary>Sets the new password with the link from the email, logs the staff member in and ends their older sessions.</summary>
    private static async Task<Results<Ok<OpsMeResponse>, ProblemHttpResult>> ConfirmPasswordResetAsync(
        AcceptInviteRequest request,
        HttpContext context,
        StaffUsers staff,
        PasswordResets resets,
        IPasswordHasher<StaffUser> hasher,
        IOptions<LoginOptions> login,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (PasswordResetEndpoints.PasswordProblem(request.Password, login.Value) is { } problem)
        {
            return problem;
        }

        var now = time.GetUtcNow();
        if (string.IsNullOrEmpty(request.Token)
            || await resets.UseAsync(PasswordResetKinds.Staff, request.Token, now, cancellationToken) is not { } staffId
            || await staff.FindByIdAsync(staffId, cancellationToken) is not { } user)
        {
            return AccountActions.Problem(StatusCodes.Status401Unauthorized, "The link has expired or was already used. Ask for a new one.");
        }

        await staff.SetPasswordAsync(user.Id, hasher.HashPassword(user, request.Password!), now, cancellationToken);
        var changed = (await staff.FindByIdAsync(user.Id, cancellationToken))!;
        await StaffAuth.SignInAsync(context, changed);
        return TypedResults.Ok(new OpsMeResponse(changed.Id, changed.Email));
    }

    private static async Task<Results<Ok<OpsFirmResponse>, ProblemHttpResult>> GetFirmAsync(string firmId, OpsFirms view, CancellationToken cancellationToken) =>
        await view.GetAsync(firmId, cancellationToken) is { } firm ? TypedResults.Ok(firm) : UnknownFirm();

    /// <summary>Ticks or unticks one of our checks in the firm's review. Answers with every check of the review.</summary>
    private static async Task<Results<Ok<IReadOnlyList<ReviewCheckResponse>>, ProblemHttpResult>> SetCheckAsync(
        string firmId,
        string item,
        ReviewCheckRequest request,
        ClaimsPrincipal principal,
        ReviewService reviews,
        CancellationToken cancellationToken)
    {
        var (result, checks) = await reviews.SetCheckAsync(firmId, item, request.Done, StaffAuth.EmailOf(principal), cancellationToken);
        return result is ReviewResult.Refused refused ? ReviewEndpoints.ProblemOf(refused) : TypedResults.Ok(ReviewCheckResponse.From(checks));
    }

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

/// <summary>
/// A firm as our staff see it: who it is, its application and our review with our checks, what it tried in the sandbox,
/// how it pays us, how it is doing and pays its traders, its suspension and its events.
/// </summary>
internal sealed class OpsFirms(
    FirmCatalog firms,
    ReviewService reviews,
    ReviewStore store,
    Identity.IdentityStore identities,
    BillingService billing,
    BillingStore billingStore,
    SlotService slots,
    FirmAdmins admins,
    OpsFigures figures,
    AdminFigures adminFigures,
    ChallengeQueries accounts,
    PayoutQueries payouts,
    IOptions<BillingOptions> billingOptions,
    TimeProvider time)
{
    /// <summary>How many payouts traders wait for are listed, the oldest first.</summary>
    public const int WaitingPayoutsShown = 50;

    /// <summary>The firm, or null when there is no such firm.</summary>
    public async Task<OpsFirmResponse?> GetAsync(string firmId, CancellationToken cancellationToken)
    {
        if (firms.ById(firmId) is not { } firm)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var (createdAt, configured) = await store.FirmInfoAsync(firm.Id, cancellationToken);
        var review = await reviews.GetAsync(firm, cancellationToken);
        var documents = await store.ListDocumentsAsync(firm.Id, cancellationToken);
        var checks = await store.ListChecksAsync(firm.Id, cancellationToken);
        var events = await store.ListEventsAsync(firm.Id, OpsEndpoints.EventsShown, cancellationToken);
        var view = await BillingEndpoints.ViewAsync(firm, billing, billingStore, slots, billingOptions.Value, time, cancellationToken);
        DateTimeOffset? activatedAt;
        int paused;
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            activatedAt = (await BillingStore.GetBillingAsync(connection, firm.Id, forUpdate: false, cancellationToken))?.ActivatedAt;
            paused = (await BillingStore.OpenAccountsAsync(connection, firm.Id, cancellationToken)).Count(a => a.Paused);
        }

        return new OpsFirmResponse(
            firm.Id,
            firm.Name,
            firm.Status,
            configured,
            createdAt,
            firm.Portal.Url,
            [.. (await admins.ListAsync(firm.Id, cancellationToken)).Select(a => new OpsAdminResponse(a.Email, a.CreatedAt))],
            configured ? null : review.Status,
            review.Application,
            [.. documents.Select(DocumentResponse.From)],
            review.Message,
            review.SubmittedAt,
            review.DecidedAt,
            review.DecidedBy,
            ReviewCheckResponse.From(checks),
            await SandboxUseAsync(firm.Id, cancellationToken),
            OpsIdentityResponse.From(await identities.GetSettingsAsync(firm.Id, cancellationToken)),
            new OpsBillingResponse(
                view.Plan,
                view.Slots,
                paused,
                view.DepositPaid,
                view.Prices.Currency,
                activatedAt,
                view.UnpaidSince,
                view.NextCharge,
                view.Card,
                view.AutoExpandStep,
                view.Charges),
            await FiguresAsync(firm.Id, now, cancellationToken),
            firm.Suspension is { } suspension ? new SuspensionResponse(suspension.At, suspension.Reason) : null,
            [.. events.Select(OpsEventResponse.From)]);
    }

    private async Task<OpsSandboxUseResponse> SandboxUseAsync(string firmId, CancellationToken cancellationToken)
    {
        var use = await figures.SandboxUseAsync(firmId, cancellationToken);
        return new OpsSandboxUseResponse(use.Challenges, use.Purchases, use.Payouts, use.OwnChallenges);
    }

    /// <summary>The firm's accounts, sales, pass rate and payouts, as its own overview has them, and its payouts beside every firm's.</summary>
    private async Task<OpsFirmFiguresResponse> FiguresAsync(string firmId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var counts = await accounts.CountAsync(firmId, new AccountSearch(null, null, AccountGroup.All), cancellationToken);
        var sales = await adminFigures.SalesAsync(firmId, now - AdminPanelEndpoints.RecentWindow, cancellationToken);
        var challenges = await adminFigures.ChallengesAsync(firmId, now - AdminPanelEndpoints.RecentWindow, now - AdminPanelEndpoints.PassRateWindow, cancellationToken);
        var summary = await payouts.SummaryAsync(firmId, now - AdminPanelEndpoints.RecentWindow, cancellationToken);
        var record = await figures.PayoutRecordAsync(firmId, now - AdminPanelEndpoints.PassRateWindow, cancellationToken);
        var platform = await figures.PayoutRecordAsync(null, now - AdminPanelEndpoints.PassRateWindow, cancellationToken);
        var platformPaid = await figures.PayoutRecordAsync(null, now - AdminPanelEndpoints.RecentWindow, cancellationToken);
        var waiting = await payouts.ListAsync(firmId, [PayoutStatus.Pending, PayoutStatus.Approved], WaitingPayoutsShown, cancellationToken);
        return new OpsFirmFiguresResponse(
            AccountCountsResponse.From(counts),
            new SalesResponse(sales.Orders, MoneyTotalResponse.From(sales.Totals)),
            new PassRateResponse(challenges.Sum(c => c.Passed), challenges.Sum(c => c.Ended)),
            new OpsPayoutsResponse(
                PayoutSummaryResponse.From(summary),
                platformPaid.AverageTimeToPay is { } average ? Math.Round(average.TotalDays, 1) : null,
                record.Rejected,
                record.Decided,
                platform.Rejected,
                platform.Decided,
                (int)OpsPanelEndpoints.LatePayoutAge.TotalDays,
                [
                    .. waiting
                        .OrderBy(p => p.RequestedAt)
                        .Select(p => new OpsWaitingPayoutResponse(p.AccountNumber, p.Status, p.RequestedAt, p.ApprovedAt, p.Amount, p.Currency)),
                ]));
    }
}
