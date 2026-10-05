using Common.Postgres;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Npgsql;

using Prop.Api.Api;
using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Email;
using Prop.Api.Review;

namespace Prop.Api.Portal;

/// <summary>
/// A forgotten password on the firm's portal, for traders and administrators alike: an email with a one-time link, and
/// the new password with it. The answer to asking for a link never tells whether the email is known. Also the checks of
/// invitation links when they are opened, so a used or expired one says so before anyone types a password.
/// </summary>
internal static class PasswordResetEndpoints
{
    public static RouteGroupBuilder MapPasswordResets(this RouteGroupBuilder portal)
    {
        portal.MapPost("/password-reset", (PasswordResetRequest request, HttpContext context, [AsParameters] ResetServices services, CancellationToken cancellationToken) =>
                RequestAsync(PortalRoles.Trader, request, context, services, cancellationToken))
            .RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/password-reset/check", (LinkCheckRequest request, HttpContext context, [AsParameters] ResetServices services, CancellationToken cancellationToken) =>
                CheckAsync(PortalRoles.Trader, request, context, services, cancellationToken))
            .RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/password-reset/confirm", (AcceptInviteRequest request, HttpContext context, [AsParameters] ResetServices services, CancellationToken cancellationToken) =>
                ConfirmAsync(PortalRoles.Trader, request, context, services, cancellationToken))
            .RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/admin/password-reset", (PasswordResetRequest request, HttpContext context, [AsParameters] ResetServices services, CancellationToken cancellationToken) =>
                RequestAsync(PortalRoles.Admin, request, context, services, cancellationToken))
            .RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/admin/password-reset/check", (LinkCheckRequest request, HttpContext context, [AsParameters] ResetServices services, CancellationToken cancellationToken) =>
                CheckAsync(PortalRoles.Admin, request, context, services, cancellationToken))
            .RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/admin/password-reset/confirm", (AcceptInviteRequest request, HttpContext context, [AsParameters] ResetServices services, CancellationToken cancellationToken) =>
                ConfirmAsync(PortalRoles.Admin, request, context, services, cancellationToken))
            .RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/invites/check", CheckTraderInviteAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/admin/invites/check", CheckAdminInviteAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        return portal;
    }

    /// <summary>
    /// Emails a link to choose a new password, when the email belongs to a trader or administrator of the firm. Always 202,
    /// so the answer never tells who has an account. A trader who never chose a password gets one this way too.
    /// </summary>
    private static async Task<Accepted> RequestAsync(string role, PasswordResetRequest request, HttpContext context, ResetServices services, CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var email = request.Email?.Trim() ?? "";
        var user = email.Length == 0
            ? null
            : role == PortalRoles.Admin
                ? await services.Users.FindAdminAsync(firm.Id, email, cancellationToken)
                : await services.Users.FindTraderAsync(firm.Id, email, cancellationToken);
        if (user is not null)
        {
            var now = services.Time.GetUtcNow();
            var token = await services.Resets.CreateAsync(role == PortalRoles.Admin ? PasswordResetKinds.Admin : PasswordResetKinds.Trader, user.Id, now, cancellationToken);
            var path = role == PortalRoles.Admin ? "admin/reset-password" : "reset-password";
            var link = new Uri(firm.Portal.Url, $"{path}?token={token}");
            var message = role == PortalRoles.Admin
                ? PlatformEmails.ResetAdminPassword(services.Platform.Value.Name, firm.Name, user.Email, link, PasswordResets.Lifetime)
                : TraderEmails.ResetPassword(firm, user.Email, link, PasswordResets.Lifetime);
            await EmailOutbox.AddAsync(services.DataSource, services.Schema, services.Signals, message, "password_reset", firm.Id, now, cancellationToken);
        }

        return TypedResults.Accepted((string?)null);
    }

    /// <summary>Whether the link from the email still works, and whose it is, without using it. 404 for a link that does not exist.</summary>
    private static async Task<Results<Ok<LinkCheckResponse>, ProblemHttpResult>> CheckAsync(
        string role,
        LinkCheckRequest request,
        HttpContext context,
        ResetServices services,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var kind = role == PortalRoles.Admin ? PasswordResetKinds.Admin : PasswordResetKinds.Trader;
        if (string.IsNullOrEmpty(request.Token)
            || await services.Resets.FindAsync(kind, request.Token, services.Time.GetUtcNow(), cancellationToken) is not { } link
            || await services.Users.FindByIdAsync(link.UserId, role, cancellationToken) is not { } user
            || user.FirmId != firm.Id)
        {
            return UnknownLink();
        }

        return TypedResults.Ok(new LinkCheckResponse(link.Status, user.Email, user.PasswordHash is not null));
    }

    /// <summary>Sets the new password with the link from the email, logs the person in and ends their older sessions.</summary>
    private static async Task<Results<Ok<PortalMeResponse>, ProblemHttpResult>> ConfirmAsync(
        string role,
        AcceptInviteRequest request,
        HttpContext context,
        ResetServices services,
        CancellationToken cancellationToken)
    {
        // Checked first, so a too short password does not use up the link.
        if (PasswordProblem(request.Password, services.Login.Value) is { } problem)
        {
            return problem;
        }

        var firm = PortalFirmFilter.FirmOf(context);
        var kind = role == PortalRoles.Admin ? PasswordResetKinds.Admin : PasswordResetKinds.Trader;
        var now = services.Time.GetUtcNow();

        // The link is only used up when it is the firm's, so a link from another portal stays good for its own.
        if (string.IsNullOrEmpty(request.Token)
            || await services.Resets.FindAsync(kind, request.Token, now, cancellationToken) is not { Status: LinkStatus.Valid } link
            || await services.Users.FindByIdAsync(link.UserId, role, cancellationToken) is not { } user
            || user.FirmId != firm.Id)
        {
            return AccountActions.Problem(StatusCodes.Status401Unauthorized, "The link has expired or was already used. Ask for a new one.");
        }

        // Until we approve the firm, only its administrators log in as its traders (ADR 0043). The link stays good until then.
        if (role == PortalRoles.Trader && !await services.Approval.MayReachAsync(firm.Id, user.Email, cancellationToken))
        {
            return AccountActions.Problem(StatusCodes.Status403Forbidden, FirmApproval.LoginClosedProblem(firm));
        }

        if (await services.Resets.UseAsync(kind, request.Token, now, cancellationToken) is null)
        {
            return AccountActions.Problem(StatusCodes.Status401Unauthorized, "The link has expired or was already used. Ask for a new one.");
        }

        var hash = services.Hasher.HashPassword(user, request.Password!);
        if (role == PortalRoles.Admin)
        {
            await services.Users.SetAdminPasswordAsync(user.Id, hash, now, cancellationToken);
        }
        else
        {
            await services.Users.SetTraderPasswordAsync(user.Id, hash, now, cancellationToken);
            await services.Users.ConfirmTraderEmailAsync(user.Id, now, cancellationToken);
        }

        // Read again, so the session carries the password's time as it is stored.
        var changed = (await services.Users.FindByIdAsync(user.Id, role, cancellationToken))!;
        await PortalAuth.SignInAsync(context, changed);
        return TypedResults.Ok(PortalMeResponse.Of(changed, firm.Name));
    }

    /// <summary>Whether the trader's invitation still works, without using it. 404 for one that does not exist.</summary>
    private static async Task<Results<Ok<LinkCheckResponse>, ProblemHttpResult>> CheckTraderInviteAsync(
        LinkCheckRequest request,
        HttpContext context,
        PortalUsers users,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        if (string.IsNullOrEmpty(request.Token)
            || await users.FindInviteAsync(firm.Id, request.Token, time.GetUtcNow(), cancellationToken) is not { } invite
            || await users.FindByIdAsync(invite.UserId, PortalRoles.Trader, cancellationToken) is not { } trader)
        {
            return UnknownLink();
        }

        return TypedResults.Ok(new LinkCheckResponse(invite.Status, trader.Email, trader.PasswordHash is not null));
    }

    /// <summary>Whether the invitation to administer the firm still works, without using it. 404 for one that does not exist.</summary>
    private static async Task<Results<Ok<LinkCheckResponse>, ProblemHttpResult>> CheckAdminInviteAsync(
        LinkCheckRequest request,
        HttpContext context,
        FirmAdmins admins,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        if (string.IsNullOrEmpty(request.Token) || await admins.FindInviteAsync(firm.Id, request.Token, time.GetUtcNow(), cancellationToken) is not { } invite)
        {
            return UnknownLink();
        }

        return TypedResults.Ok(new LinkCheckResponse(invite.Status, invite.Email, await admins.IsAdminAsync(firm.Id, invite.Email, cancellationToken)));
    }

    internal static ProblemHttpResult? PasswordProblem(string? password, LoginOptions login) =>
        password is null || password.Length < login.MinimumPasswordLength
            ? AccountActions.Problem(
                StatusCodes.Status422UnprocessableEntity,
                login.MinimumPasswordLength == 1 ? "Choose a password." : $"The password needs at least {login.MinimumPasswordLength} characters.")
            : null;

    internal static ProblemHttpResult UnknownLink() => AccountActions.Problem(StatusCodes.Status404NotFound, "The link does not work. Check that it is the whole link from the email.");
}

/// <summary>What the reset endpoints need, so each handler takes one parameter for it.</summary>
internal sealed record ResetServices(
    PortalUsers Users,
    PasswordResets Resets,
    FirmApproval Approval,
    IPasswordHasher<PortalUser> Hasher,
    IOptions<LoginOptions> Login,
    IOptions<PlatformOptions> Platform,
    NpgsqlDataSource DataSource,
    DatabaseSchema Schema,
    WorkSignals Signals,
    TimeProvider Time);

/// <summary>The email of the person who forgot the password.</summary>
public sealed record PasswordResetRequest(string? Email);

/// <summary>The token from a link in an email.</summary>
public sealed record LinkCheckRequest(string? Token);

/// <summary>
/// Whether a link from an email still works and whose it is. <paramref name="HasPassword"/> tells whether the person can
/// already log in, for example after using the invitation once.
/// </summary>
public sealed record LinkCheckResponse(LinkStatus Status, string Email, bool HasPassword);
