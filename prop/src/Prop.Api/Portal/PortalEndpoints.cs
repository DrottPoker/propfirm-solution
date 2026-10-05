using System.Security.Claims;

using Common.Postgres;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Npgsql;

using Prop.Api.Api;
using Prop.Api.Billing;
using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Email;
using Prop.Api.Firms;
using Prop.Api.History;
using Prop.Api.Identity;
using Prop.Api.Payments;
using Prop.Api.Review;
using Prop.Api.Support;
using Prop.Api.Trading;
using Prop.Rules;

namespace Prop.Api.Portal;

/// <summary>
/// The firm's white label portal: its look, login for traders and administrators, the trader's own accounts
/// and the admin panel. The firm is known from the portal's host, and a session works only on its own firm's
/// portal. Traders and administrators have separate sessions, with their own me and logout.
/// </summary>
internal static class PortalEndpoints
{
    // Verified when the email is unknown, so a wrong email takes as long as a wrong password.
    private static readonly Lazy<string> UnknownUserHash = new(() => new PasswordHasher<PortalUser>().HashPassword(null!, Guid.NewGuid().ToString()));

    public static IEndpointRouteBuilder MapPortalApi(this IEndpointRouteBuilder app)
    {
        var portal = app.MapGroup("/api/portal").WithTags("Portal").AddEndpointFilter<PortalFirmFilter>();
        portal.MapGet("/branding", (HttpContext context) => TypedResults.Ok(BrandingResponse.From(PortalFirmFilter.FirmOf(context))));
        portal.MapGet("/logo/{sha256}", GetLogoAsync);
        portal.MapPost(
                "/login",
                (PortalLoginRequest request, HttpContext context, PortalUsers users, IPasswordHasher<PortalUser> hasher, FirmApproval approval, CancellationToken cancellationToken) =>
                    LoginAsync(PortalRoles.Trader, request, context, users, hasher, approval, cancellationToken))
            .RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/invites/accept", AcceptInviteAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/invites/confirm", ConfirmInviteAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/me/confirm-email", SendEmailConfirmationAsync).RequireAuthorization(PortalAuth.TraderPolicy).RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPasswordResets();
        portal.MapPost("/logout", (Func<HttpContext, Task<NoContent>>)(context => LogoutAsync(context, PortalRoles.Trader)));
        portal.MapGet("/me", MeAsync).RequireAuthorization(PortalAuth.TraderPolicy);
        portal.MapShop();

        var trader = portal.MapGroup("/accounts").RequireAuthorization(PortalAuth.TraderPolicy);
        trader.MapGet("", ListMyAccountsAsync);
        trader.MapGet("/{accountId:guid}", GetMyAccountAsync);
        trader.MapGet("/{accountId:guid}/performance", GetMyPerformanceAsync);
        trader.MapGet("/{accountId:guid}/trades", ListMyTradesAsync);
        trader.MapGet("/{accountId:guid}/trades.csv", DownloadMyTradesAsync);
        trader.MapPost("/{accountId:guid}/terminal-link", CreateTerminalLinkAsync);
        trader.MapPost("/{accountId:guid}/payouts", RequestPayoutAsync);
        portal.MapGet("/payouts", ListMyPayoutsAsync).RequireAuthorization(PortalAuth.TraderPolicy);
        portal.MapGet("/payout-method", GetMyPayoutMethodAsync).RequireAuthorization(PortalAuth.TraderPolicy);
        portal.MapPut("/payout-method", SaveMyPayoutMethodAsync).RequireAuthorization(PortalAuth.TraderPolicy);
        portal.MapTraderSupport();
        portal.MapTraderIdentity();

        portal.MapPost(
                "/admin/login",
                (PortalLoginRequest request, HttpContext context, PortalUsers users, IPasswordHasher<PortalUser> hasher, FirmApproval approval, CancellationToken cancellationToken) =>
                    LoginAsync(PortalRoles.Admin, request, context, users, hasher, approval, cancellationToken))
            .RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/admin/logout", (Func<HttpContext, Task<NoContent>>)(context => LogoutAsync(context, PortalRoles.Admin)));
        portal.MapAdminWaysIn();

        var admin = portal.MapGroup("/admin").RequireAuthorization(PortalAuth.AdminPolicy);
        admin.MapGet("/me", MeAsync);
        admin.MapGet("/challenges", ListChallengesAsync);
        admin.MapPost("/accounts", StartAccountAsync);
        admin.MapGet("/accounts/{accountId:guid}", GetAccountAsync);
        admin.MapGet("/accounts/{accountId:guid}/history", GetHistoryAsync);
        admin.MapPost("/accounts/{accountId:guid}/approve-funding", ApproveFundingAsync);
        admin.MapPost("/accounts/{accountId:guid}/cancel", CancelAsync);
        admin.MapPost("/accounts/{accountId:guid}/invite", InviteAsync);
        admin.MapPost("/payouts/{payoutId:guid}/approve", ApprovePayoutAsync);
        admin.MapPost("/payouts/{payoutId:guid}/mark-paid", MarkPayoutPaidAsync);
        admin.MapPost("/payouts/{payoutId:guid}/reject", RejectPayoutAsync);
        admin.MapAdminPanel();
        admin.MapAdminSettings();
        admin.MapTradingConditions();
        admin.MapAdminOrders();
        admin.MapAdminDiscounts();
        admin.MapAdminDomain();
        admin.MapAdminBilling();
        admin.MapAdminVerification();
        admin.MapAdminSupport();
        admin.MapAdminIdentity();
        return app;
    }

    // A trader without a password has not accepted an invitation yet, and cannot log in. Until we approve the firm, only
    // its administrators log in as its traders (ADR 0043), which is checked after the password, so it tells nothing to others.
    private static async Task<Results<Ok<PortalMeResponse>, ProblemHttpResult>> LoginAsync(
        string role,
        PortalLoginRequest request,
        HttpContext context,
        PortalUsers users,
        IPasswordHasher<PortalUser> hasher,
        FirmApproval approval,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var email = request.Email ?? "";
        var user = role == PortalRoles.Admin
            ? await users.FindAdminAsync(firm.Id, email, cancellationToken)
            : await users.FindTraderAsync(firm.Id, email, cancellationToken);
        var verified = hasher.VerifyHashedPassword(user!, user?.PasswordHash ?? UnknownUserHash.Value, request.Password ?? "");
        if (user?.PasswordHash is null || verified == PasswordVerificationResult.Failed)
        {
            return AccountActions.Problem(StatusCodes.Status401Unauthorized, "Wrong email or password.");
        }

        if (role == PortalRoles.Trader && !await approval.MayReachAsync(firm.Id, user.Email, cancellationToken))
        {
            return AccountActions.Problem(StatusCodes.Status403Forbidden, FirmApproval.LoginClosedProblem(firm));
        }

        await PortalAuth.SignInAsync(context, user);
        return TypedResults.Ok(PortalMeResponse.Of(user, firm.Name));
    }

    /// <summary>The trader chooses a password with an invitation from the firm, and is logged in.</summary>
    private static async Task<Results<Ok<PortalMeResponse>, ProblemHttpResult>> AcceptInviteAsync(
        AcceptInviteRequest request,
        HttpContext context,
        PortalUsers users,
        IPasswordHasher<PortalUser> hasher,
        FirmApproval approval,
        IOptions<LoginOptions> login,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        // Checked first, so a too short password does not use up the invitation.
        var minimumLength = login.Value.MinimumPasswordLength;
        if (request.Password is null || request.Password.Length < minimumLength)
        {
            return AccountActions.Problem(
                StatusCodes.Status422UnprocessableEntity,
                minimumLength == 1 ? "Choose a password." : $"The password needs at least {minimumLength} characters.");
        }

        var firm = PortalFirmFilter.FirmOf(context);

        // Checked before the invitation is used up, so it still works once we have approved the firm.
        if (!string.IsNullOrEmpty(request.Token)
            && await users.FindInviteAsync(firm.Id, request.Token, time.GetUtcNow(), cancellationToken) is { Status: LinkStatus.Valid } invite
            && await users.FindByIdAsync(invite.UserId, PortalRoles.Trader, cancellationToken) is { } invited
            && !await approval.MayReachAsync(firm.Id, invited.Email, cancellationToken))
        {
            return AccountActions.Problem(StatusCodes.Status403Forbidden, FirmApproval.LoginClosedProblem(firm));
        }

        var traderId = string.IsNullOrEmpty(request.Token) ? null : await users.UseInviteAsync(firm.Id, request.Token, time.GetUtcNow(), cancellationToken);
        if (traderId is null || await users.FindByIdAsync(traderId.Value, PortalRoles.Trader, cancellationToken) is not { } trader)
        {
            return AccountActions.Problem(StatusCodes.Status401Unauthorized, "The invitation has expired or was already used.");
        }

        await users.SetTraderPasswordAsync(trader.Id, hasher.HashPassword(trader, request.Password), time.GetUtcNow(), cancellationToken);
        await users.ConfirmTraderEmailAsync(trader.Id, time.GetUtcNow(), cancellationToken);

        // Read again, so the session carries the password's time as it is stored.
        var changed = (await users.FindByIdAsync(trader.Id, PortalRoles.Trader, cancellationToken))!;
        await PortalAuth.SignInAsync(context, changed);
        return TypedResults.Ok(PortalMeResponse.Of(changed, firm.Name));
    }

    /// <summary>
    /// A trader who already chose a password, on an order's page, opens the link from the email: it confirms the email and
    /// logs the trader in. A trader without a password chooses one with the link instead.
    /// </summary>
    private static async Task<Results<Ok<PortalMeResponse>, ProblemHttpResult>> ConfirmInviteAsync(
        LinkCheckRequest request,
        HttpContext context,
        PortalUsers users,
        FirmApproval approval,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var now = time.GetUtcNow();
        if (string.IsNullOrEmpty(request.Token)
            || await users.FindInviteAsync(firm.Id, request.Token, now, cancellationToken) is not { Status: LinkStatus.Valid } invite
            || await users.FindByIdAsync(invite.UserId, PortalRoles.Trader, cancellationToken) is not { } trader)
        {
            return AccountActions.Problem(StatusCodes.Status401Unauthorized, "The invitation has expired or was already used.");
        }

        if (trader.PasswordHash is null)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "Choose a password.");
        }

        if (!await approval.MayReachAsync(firm.Id, trader.Email, cancellationToken))
        {
            return AccountActions.Problem(StatusCodes.Status403Forbidden, FirmApproval.LoginClosedProblem(firm));
        }

        if (await users.UseInviteAsync(firm.Id, request.Token, now, cancellationToken) is null)
        {
            return AccountActions.Problem(StatusCodes.Status401Unauthorized, "The invitation has expired or was already used.");
        }

        await users.ConfirmTraderEmailAsync(trader.Id, now, cancellationToken);
        var confirmed = (await users.FindByIdAsync(trader.Id, PortalRoles.Trader, cancellationToken))!;
        await PortalAuth.SignInAsync(context, confirmed);
        return TypedResults.Ok(PortalMeResponse.Of(confirmed, firm.Name));
    }

    /// <summary>Emails the logged-in trader a new link that confirms the email. 409 when it is confirmed already.</summary>
    private static async Task<Results<Accepted, ProblemHttpResult>> SendEmailConfirmationAsync(
        ClaimsPrincipal principal,
        HttpContext context,
        PortalUsers users,
        WorkSignals signals,
        NpgsqlDataSource dataSource,
        DatabaseSchema schema,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        if (await users.FindByIdAsync(PortalAuth.UserIdOf(principal), PortalRoles.Trader, cancellationToken) is not { } trader)
        {
            return AccountActions.Problem(StatusCodes.Status401Unauthorized, "Log in again.");
        }

        if (trader.EmailConfirmedAt is not null)
        {
            return AccountActions.Problem(StatusCodes.Status409Conflict, "Your email is confirmed already.");
        }

        var now = time.GetUtcNow();
        var invite = await users.CreateInviteAsync(trader.Id, now, cancellationToken);
        await EmailOutbox.AddAsync(
            dataSource,
            schema,
            signals,
            TraderEmails.ConfirmEmail(firm, trader.Email, new Uri(firm.Portal.Url, $"invite?token={invite.Token}"), PortalUsers.InviteLifetime),
            "email_confirmation",
            firm.Id,
            now,
            cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<NoContent> LogoutAsync(HttpContext context, string role)
    {
        await PortalAuth.SignOutAsync(context, role);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<PortalMeResponse>, UnauthorizedHttpResult>> MeAsync(
        ClaimsPrincipal principal,
        HttpContext context,
        PortalUsers users,
        CancellationToken cancellationToken)
    {
        var role = principal.FindFirstValue(ClaimTypes.Role) ?? "";
        return await users.FindByIdAsync(PortalAuth.UserIdOf(principal), role, cancellationToken) is { } user
            ? TypedResults.Ok(PortalMeResponse.Of(user, PortalFirmFilter.FirmOf(context).Name))
            : TypedResults.Unauthorized();
    }

    /// <summary>The trader's accounts as the dashboard shows them, oldest first, with their trading accounts valued right now.</summary>
    private static async Task<Ok<List<AccountDetailsResponse>>> ListMyAccountsAsync(
        ClaimsPrincipal principal,
        HttpContext context,
        ChallengeQueries queries,
        AccountDetailsBuilder details,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var views = await queries.ListByTraderAsync(firm.Id, PortalAuth.UserIdOf(principal), cancellationToken);
        return TypedResults.Ok(await details.BuildAsync(firm, views, cancellationToken));
    }

    private static Task<Results<Ok<AccountDetailsResponse>, ProblemHttpResult>> GetMyAccountAsync(
        Guid accountId,
        ClaimsPrincipal principal,
        HttpContext context,
        ChallengeQueries queries,
        AccountDetailsBuilder details,
        CancellationToken cancellationToken) =>
        AccountActions.DetailsAsync(PortalFirmFilter.FirmOf(context), accountId, PortalAuth.UserIdOf(principal), queries, details, cancellationToken);

    /// <summary>How a stage has gone: the balance, the loss limits, each day and statistics. Without a stage, the latest that has started.</summary>
    private static Task<Results<Ok<PerformanceResponse>, ProblemHttpResult>> GetMyPerformanceAsync(
        Guid accountId,
        ClaimsPrincipal principal,
        HttpContext context,
        ChallengeQueries queries,
        TradingHistoryQueries history,
        CancellationToken cancellationToken,
        int? stage = null) =>
        HistoryActions.PerformanceAsync(PortalFirmFilter.FirmOf(context), accountId, PortalAuth.UserIdOf(principal), stage, queries, history, cancellationToken);

    /// <summary>A stage's closed positions, newest first, a page at a time.</summary>
    private static Task<Results<Ok<TradesResponse>, ProblemHttpResult>> ListMyTradesAsync(
        Guid accountId,
        ClaimsPrincipal principal,
        HttpContext context,
        ChallengeQueries queries,
        TradingHistoryQueries history,
        CancellationToken cancellationToken,
        int? stage = null,
        long? before = null,
        int limit = 50) =>
        HistoryActions.TradesAsync(PortalFirmFilter.FirmOf(context), accountId, PortalAuth.UserIdOf(principal), stage, before, limit, queries, history, cancellationToken);

    /// <summary>A stage's closed positions as a CSV file.</summary>
    private static Task<Results<FileContentHttpResult, ProblemHttpResult>> DownloadMyTradesAsync(
        Guid accountId,
        ClaimsPrincipal principal,
        HttpContext context,
        ChallengeQueries queries,
        TradingHistoryQueries history,
        CancellationToken cancellationToken,
        int? stage = null) =>
        HistoryActions.TradesCsvAsync(PortalFirmFilter.FirmOf(context), accountId, PortalAuth.UserIdOf(principal), stage, queries, history, cancellationToken);

    private static Task<Ok<TraderPayoutsResponse>> ListMyPayoutsAsync(
        ClaimsPrincipal principal,
        HttpContext context,
        ChallengeQueries accounts,
        PayoutQueries payouts,
        CancellationToken cancellationToken) =>
        PayoutActions.ListForTraderAsync(PortalFirmFilter.FirmOf(context), PortalAuth.UserIdOf(principal), accounts, payouts, cancellationToken);

    /// <summary>
    /// The trader asks for a payout of the funded account's profit. 409 with the reason when one cannot be had now, for
    /// example before the trader has said how to be paid, or verified their identity when the firm wants that first.
    /// </summary>
    private static async Task<Results<Created<PayoutResponse>, ProblemHttpResult>> RequestPayoutAsync(
        Guid accountId,
        ClaimsPrincipal principal,
        HttpContext context,
        ChallengeService challenges,
        ChallengeQueries accounts,
        PayoutQueries payouts,
        PayoutMethods methods,
        PortalUsers users,
        IdentityService identity,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var traderId = PortalAuth.UserIdOf(principal);
        if (await users.FindByIdAsync(traderId, PortalRoles.Trader, cancellationToken) is { EmailConfirmedAt: null })
        {
            return AccountActions.Problem(StatusCodes.Status409Conflict, "Confirm your email first, with the link we emailed you. You can ask for a new link in the portal.");
        }

        if (await methods.GetAsync(traderId, cancellationToken) is null)
        {
            return AccountActions.Problem(StatusCodes.Status409Conflict, "Add how you want to be paid under Payouts first.");
        }

        if (await identity.ProblemAsync(PortalFirmFilter.FirmOf(context), traderId, IdentityRequirement.FirstPayout, cancellationToken) is { } unverified)
        {
            return AccountActions.Problem(StatusCodes.Status409Conflict, unverified);
        }

        return await PayoutActions.RequestAsync(
            PortalFirmFilter.FirmOf(context),
            accountId,
            traderId,
            _ => $"/api/portal/accounts/{accountId}",
            challenges,
            accounts,
            payouts,
            time,
            cancellationToken);
    }

    private static async Task<Ok<PayoutMethodResponse>> GetMyPayoutMethodAsync(ClaimsPrincipal principal, PayoutMethods methods, CancellationToken cancellationToken) =>
        TypedResults.Ok(new PayoutMethodResponse(await methods.GetAsync(PortalAuth.UserIdOf(principal), cancellationToken)));

    /// <summary>How the trader wants to be paid. Payouts asked for from now on are paid there; those on their way keep theirs.</summary>
    private static async Task<Results<Ok<PayoutMethodResponse>, ProblemHttpResult>> SaveMyPayoutMethodAsync(
        PayoutMethod request,
        ClaimsPrincipal principal,
        PayoutMethods methods,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var method = request.Normalized();
        if (method.Problem() is { } problem)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, problem);
        }

        await methods.SaveAsync(PortalAuth.UserIdOf(principal), method, time.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(new PayoutMethodResponse(method));
    }

    private static Task<Results<Ok<LoginLinkResponse>, ProblemHttpResult>> CreateTerminalLinkAsync(
        Guid accountId,
        ClaimsPrincipal principal,
        HttpContext context,
        ChallengeQueries queries,
        ITradingPlatform trading,
        FirmApproval approval,
        CancellationToken cancellationToken) =>
        AccountActions.TerminalLinkAsync(PortalFirmFilter.FirmOf(context), accountId, PortalAuth.UserIdOf(principal), queries, trading, approval, cancellationToken);

    private static async Task<Ok<IReadOnlyList<ChallengeDefinition>>> ListChallengesAsync(
        HttpContext context,
        ChallengeCatalog catalog,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await catalog.ListAsync(PortalFirmFilter.FirmOf(context).Id, cancellationToken));

    /// <summary>
    /// The logo the firm uploaded, at the address its hash is part of. It never changes there, so it is cached for good.
    /// An SVG opened on its own could otherwise run scripts in the portal's origin, so it is sandboxed.
    /// </summary>
    private static async Task<Results<FileContentHttpResult, NotFound>> GetLogoAsync(
        string sha256,
        HttpContext context,
        FirmStore store,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        if (await store.GetLogoAsync(firm.Id, cancellationToken) is not { } logo || Convert.ToHexStringLower(logo.Sha256) != sha256)
        {
            return TypedResults.NotFound();
        }

        var headers = context.Response.Headers;
        headers.CacheControl = "public, max-age=31536000, immutable";
        headers.XContentTypeOptions = "nosniff";
        headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        return TypedResults.File(logo.Content, logo.ContentType);
    }

    private static Task<Results<Created<AccountResponse>, Ok<AccountResponse>, ProblemHttpResult>> StartAccountAsync(
        StartAccountRequest request,
        HttpContext context,
        ChallengeService challenges,
        ChallengeQueries queries,
        CancellationToken cancellationToken) =>
        AccountActions.StartAsync(PortalFirmFilter.FirmOf(context), request, "/api/portal/admin/accounts", challenges, queries, cancellationToken);

    private static Task<Results<Ok<AccountDetailsResponse>, ProblemHttpResult>> GetAccountAsync(
        Guid accountId,
        HttpContext context,
        ChallengeQueries queries,
        AccountDetailsBuilder details,
        CancellationToken cancellationToken) =>
        AccountActions.DetailsAsync(PortalFirmFilter.FirmOf(context), accountId, null, queries, details, cancellationToken);

    private static Task<Results<Ok<List<StepResponse>>, ProblemHttpResult>> GetHistoryAsync(
        Guid accountId,
        HttpContext context,
        ChallengeQueries queries,
        CancellationToken cancellationToken) =>
        FirmEndpoints.HistoryAsync(PortalFirmFilter.FirmOf(context), accountId, queries, cancellationToken);

    private static Task<Results<Ok<AccountResponse>, ProblemHttpResult>> ApproveFundingAsync(
        Guid accountId,
        HttpContext context,
        ChallengeService challenges,
        ChallengeQueries queries,
        IdentityService identity,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        AccountActions.ApproveFundingAsync(PortalFirmFilter.FirmOf(context), accountId, challenges, queries, identity, time, cancellationToken);

    private static Task<Results<Ok<AccountResponse>, ProblemHttpResult>> CancelAsync(
        Guid accountId,
        CancelAccountRequest request,
        HttpContext context,
        ChallengeService challenges,
        ChallengeQueries queries,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        AccountActions.ApplyAsync(PortalFirmFilter.FirmOf(context), accountId, FirmEndpoints.Cancel(request, time), challenges, queries, cancellationToken);

    private static Task<Results<Ok<PayoutResponse>, ProblemHttpResult>> ApprovePayoutAsync(
        Guid payoutId,
        HttpContext context,
        ChallengeService challenges,
        PayoutQueries payouts,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        PayoutActions.DecideAsync(PortalFirmFilter.FirmOf(context), payoutId, PayoutActions.Approve(time), challenges, payouts, cancellationToken);

    private static Task<Results<Ok<PayoutResponse>, ProblemHttpResult>> MarkPayoutPaidAsync(
        Guid payoutId,
        MarkPayoutPaidRequest request,
        HttpContext context,
        ChallengeService challenges,
        PayoutQueries payouts,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        PayoutActions.DecideAsync(PortalFirmFilter.FirmOf(context), payoutId, PayoutActions.MarkPaid(request, time), challenges, payouts, cancellationToken);

    private static Task<Results<Ok<PayoutResponse>, ProblemHttpResult>> RejectPayoutAsync(
        Guid payoutId,
        RejectPayoutRequest request,
        HttpContext context,
        ChallengeService challenges,
        PayoutQueries payouts,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        PayoutActions.DecideAsync(PortalFirmFilter.FirmOf(context), payoutId, PayoutActions.Reject(request, time), challenges, payouts, cancellationToken);

    private static Task<Results<Ok<InviteResponse>, ProblemHttpResult>> InviteAsync(
        Guid accountId,
        HttpContext context,
        ChallengeQueries queries,
        PortalUsers users,
        FirmApproval approval,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        AccountActions.InviteAsync(PortalFirmFilter.FirmOf(context), accountId, queries, users, approval, time, cancellationToken);
}
