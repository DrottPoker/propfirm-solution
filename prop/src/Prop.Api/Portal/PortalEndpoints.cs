using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Prop.Api.Api;
using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Firms;
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
    public const int MaxAccountsPerRequest = 500;

    // Verified when the email is unknown, so a wrong email takes as long as a wrong password.
    private static readonly Lazy<string> UnknownUserHash = new(() => new PasswordHasher<PortalUser>().HashPassword(null!, Guid.NewGuid().ToString()));

    public static IEndpointRouteBuilder MapPortalApi(this IEndpointRouteBuilder app)
    {
        var portal = app.MapGroup("/api/portal").WithTags("Portal").AddEndpointFilter<PortalFirmFilter>();
        portal.MapGet("/branding", (HttpContext context) => TypedResults.Ok(PortalFirmFilter.FirmOf(context).Portal.Branding));
        portal.MapPost(
                "/login",
                (PortalLoginRequest request, HttpContext context, PortalUsers users, IPasswordHasher<PortalUser> hasher, CancellationToken cancellationToken) =>
                    LoginAsync(PortalRoles.Trader, request, context, users, hasher, cancellationToken))
            .RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/invites/accept", AcceptInviteAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/logout", (Func<HttpContext, Task<NoContent>>)(context => LogoutAsync(context, PortalRoles.Trader)));
        portal.MapGet("/me", MeAsync).RequireAuthorization(PortalAuth.TraderPolicy);

        var trader = portal.MapGroup("/accounts").RequireAuthorization(PortalAuth.TraderPolicy);
        trader.MapGet("", ListMyAccountsAsync);
        trader.MapGet("/{accountId:guid}", GetMyAccountAsync);
        trader.MapPost("/{accountId:guid}/terminal-link", CreateTerminalLinkAsync);
        trader.MapPost("/{accountId:guid}/payouts", RequestPayoutAsync);

        portal.MapPost(
                "/admin/login",
                (PortalLoginRequest request, HttpContext context, PortalUsers users, IPasswordHasher<PortalUser> hasher, CancellationToken cancellationToken) =>
                    LoginAsync(PortalRoles.Admin, request, context, users, hasher, cancellationToken))
            .RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/admin/logout", (Func<HttpContext, Task<NoContent>>)(context => LogoutAsync(context, PortalRoles.Admin)));

        var admin = portal.MapGroup("/admin").RequireAuthorization(PortalAuth.AdminPolicy);
        admin.MapGet("/me", MeAsync);
        admin.MapGet("/challenges", ListChallengesAsync);
        admin.MapGet("/accounts", ListAccountsAsync);
        admin.MapPost("/accounts", StartAccountAsync);
        admin.MapGet("/accounts/{accountId:guid}", GetAccountAsync);
        admin.MapGet("/accounts/{accountId:guid}/history", GetHistoryAsync);
        admin.MapPost("/accounts/{accountId:guid}/approve-funding", ApproveFundingAsync);
        admin.MapPost("/accounts/{accountId:guid}/cancel", CancelAsync);
        admin.MapPost("/accounts/{accountId:guid}/invite", InviteAsync);
        admin.MapGet("/payouts", ListPayoutsAsync);
        admin.MapPost("/payouts/{payoutId:guid}/approve", ApprovePayoutAsync);
        admin.MapPost("/payouts/{payoutId:guid}/mark-paid", MarkPayoutPaidAsync);
        admin.MapPost("/payouts/{payoutId:guid}/reject", RejectPayoutAsync);
        return app;
    }

    // A trader without a password has not accepted an invitation yet, and cannot log in.
    private static async Task<Results<Ok<PortalMeResponse>, ProblemHttpResult>> LoginAsync(
        string role,
        PortalLoginRequest request,
        HttpContext context,
        PortalUsers users,
        IPasswordHasher<PortalUser> hasher,
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

        await PortalAuth.SignInAsync(context, user);
        return TypedResults.Ok(new PortalMeResponse(user.Id, user.Email, user.Role, firm.Name));
    }

    /// <summary>The trader chooses a password with an invitation from the firm, and is logged in.</summary>
    private static async Task<Results<Ok<PortalMeResponse>, ProblemHttpResult>> AcceptInviteAsync(
        AcceptInviteRequest request,
        HttpContext context,
        PortalUsers users,
        IPasswordHasher<PortalUser> hasher,
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
        var traderId = string.IsNullOrEmpty(request.Token) ? null : await users.UseInviteAsync(firm.Id, request.Token, time.GetUtcNow(), cancellationToken);
        if (traderId is null || await users.FindByIdAsync(traderId.Value, PortalRoles.Trader, cancellationToken) is not { } trader)
        {
            return AccountActions.Problem(StatusCodes.Status401Unauthorized, "The invitation has expired or was already used.");
        }

        await users.SetTraderPasswordAsync(trader.Id, hasher.HashPassword(trader, request.Password), cancellationToken);
        await PortalAuth.SignInAsync(context, trader);
        return TypedResults.Ok(new PortalMeResponse(trader.Id, trader.Email, trader.Role, firm.Name));
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
            ? TypedResults.Ok(new PortalMeResponse(user.Id, user.Email, user.Role, PortalFirmFilter.FirmOf(context).Name))
            : TypedResults.Unauthorized();
    }

    private static async Task<Ok<List<AccountResponse>>> ListMyAccountsAsync(
        ClaimsPrincipal principal,
        HttpContext context,
        ChallengeQueries queries,
        CancellationToken cancellationToken)
    {
        var views = await queries.ListByTraderAsync(PortalFirmFilter.FirmOf(context).Id, PortalAuth.UserIdOf(principal), cancellationToken);
        return TypedResults.Ok(views.Select(AccountResponse.From).ToList());
    }

    private static Task<Results<Ok<AccountDetailsResponse>, ProblemHttpResult>> GetMyAccountAsync(
        Guid accountId,
        ClaimsPrincipal principal,
        HttpContext context,
        ChallengeQueries queries,
        PayoutQueries payouts,
        ITradingPlatform trading,
        CancellationToken cancellationToken) =>
        AccountActions.DetailsAsync(PortalFirmFilter.FirmOf(context), accountId, PortalAuth.UserIdOf(principal), queries, payouts, trading, cancellationToken);

    /// <summary>The trader asks for a payout of the funded account's profit. 409 with the reason when one cannot be had now.</summary>
    private static Task<Results<Created<PayoutResponse>, ProblemHttpResult>> RequestPayoutAsync(
        Guid accountId,
        ClaimsPrincipal principal,
        HttpContext context,
        ChallengeService challenges,
        ChallengeQueries accounts,
        PayoutQueries payouts,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        PayoutActions.RequestAsync(
            PortalFirmFilter.FirmOf(context),
            accountId,
            PortalAuth.UserIdOf(principal),
            _ => $"/api/portal/accounts/{accountId}",
            challenges,
            accounts,
            payouts,
            time,
            cancellationToken);

    private static Task<Results<Ok<LoginLinkResponse>, ProblemHttpResult>> CreateTerminalLinkAsync(
        Guid accountId,
        ClaimsPrincipal principal,
        HttpContext context,
        ChallengeQueries queries,
        ITradingPlatform trading,
        CancellationToken cancellationToken) =>
        AccountActions.TerminalLinkAsync(PortalFirmFilter.FirmOf(context), accountId, PortalAuth.UserIdOf(principal), queries, trading, cancellationToken);

    private static async Task<Ok<IReadOnlyList<ChallengeDefinition>>> ListChallengesAsync(
        HttpContext context,
        ChallengeCatalog catalog,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await catalog.ListAsync(PortalFirmFilter.FirmOf(context).Id, cancellationToken));

    /// <summary>The firm's newest accounts, optionally one trader's or those with a status.</summary>
    private static async Task<Results<Ok<List<AccountResponse>>, ProblemHttpResult>> ListAccountsAsync(
        HttpContext context,
        ChallengeQueries queries,
        CancellationToken cancellationToken,
        string? email = null,
        ChallengeStatus? status = null,
        int limit = 100)
    {
        if (limit is < 1 or > MaxAccountsPerRequest)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, $"limit must be 1 to {MaxAccountsPerRequest}.");
        }

        var views = await queries.ListAsync(PortalFirmFilter.FirmOf(context).Id, string.IsNullOrWhiteSpace(email) ? null : email, status, limit, cancellationToken);
        return TypedResults.Ok(views.Select(AccountResponse.From).ToList());
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
        PayoutQueries payouts,
        ITradingPlatform trading,
        CancellationToken cancellationToken) =>
        AccountActions.DetailsAsync(PortalFirmFilter.FirmOf(context), accountId, null, queries, payouts, trading, cancellationToken);

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
        TimeProvider time,
        CancellationToken cancellationToken) =>
        AccountActions.ApplyAsync(PortalFirmFilter.FirmOf(context), accountId, new ApproveFunding(time.GetUtcNow()), challenges, queries, cancellationToken);

    private static Task<Results<Ok<AccountResponse>, ProblemHttpResult>> CancelAsync(
        Guid accountId,
        CancelAccountRequest request,
        HttpContext context,
        ChallengeService challenges,
        ChallengeQueries queries,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        AccountActions.ApplyAsync(PortalFirmFilter.FirmOf(context), accountId, FirmEndpoints.Cancel(request, time), challenges, queries, cancellationToken);

    /// <summary>The firm's newest payouts, optionally only those with the given statuses.</summary>
    private static Task<Results<Ok<List<PayoutResponse>>, ProblemHttpResult>> ListPayoutsAsync(
        HttpContext context,
        PayoutQueries payouts,
        CancellationToken cancellationToken,
        PayoutStatus[]? status = null,
        int limit = 100) =>
        PayoutActions.ListAsync(PortalFirmFilter.FirmOf(context), status, limit, payouts, cancellationToken);

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
        TimeProvider time,
        CancellationToken cancellationToken) =>
        AccountActions.InviteAsync(PortalFirmFilter.FirmOf(context), accountId, queries, users, time, cancellationToken);
}
