using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Prop.Api.Api;
using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Email;
using Prop.Api.Firms;
using Prop.Rules;

namespace Prop.Api.Portal;

/// <summary>
/// The admin panel's settings for a firm that runs itself (ADR 0017): its look, its challenges, its
/// administrators and its integration through the firm API and webhooks. Also the two ways in for an
/// administrator without a password login: the link after signing up and an invitation.
/// </summary>
internal static class AdminSettingsEndpoints
{
    /// <summary>Maps the ways in on the portal, where no one is logged in yet.</summary>
    public static RouteGroupBuilder MapAdminWaysIn(this RouteGroupBuilder portal)
    {
        portal.MapPost("/admin/welcome", WelcomeAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/admin/invites/accept", AcceptInviteAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        return portal;
    }

    /// <summary>Maps the settings on the admin group, where an administrator is logged in.</summary>
    public static RouteGroupBuilder MapAdminSettings(this RouteGroupBuilder admin)
    {
        admin.MapGet("/firm", GetFirm);
        admin.MapPut("/firm/branding", SetBrandingAsync);
        admin.MapPost("/firm/api-key", CreateApiKeyAsync);
        admin.MapPut("/firm/webhook", SetWebhookAsync);
        admin.MapPost("/firm/webhook/secret", CreateWebhookSecretAsync);
        admin.MapGet("/challenge-templates", GetChallengeTemplates);
        admin.MapPut("/challenges/{challengeId}", SaveChallengeAsync);
        admin.MapGet("/admins", ListAdminsAsync);
        admin.MapPost("/admins/invites", InviteAdminAsync);
        admin.MapDelete("/admins/{adminId:guid}", RemoveAdminAsync);
        return admin;
    }

    /// <summary>Logs the administrator in with the one-time link from signing up.</summary>
    private static async Task<Results<Ok<PortalMeResponse>, ProblemHttpResult>> WelcomeAsync(
        WelcomeRequest request,
        HttpContext context,
        FirmAdmins admins,
        PortalUsers users,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var adminId = string.IsNullOrEmpty(request.Token) ? null : await admins.UseLoginLinkAsync(firm.Id, request.Token, time.GetUtcNow(), cancellationToken);
        if (adminId is null || await users.FindByIdAsync(adminId.Value, PortalRoles.Admin, cancellationToken) is not { } admin)
        {
            return AccountActions.Problem(StatusCodes.Status401Unauthorized, "The link has expired or was already used. Log in with your email and password.");
        }

        await PortalAuth.SignInAsync(context, admin);
        return TypedResults.Ok(new PortalMeResponse(admin.Id, admin.Email, admin.Role, firm.Name));
    }

    /// <summary>An invited administrator chooses a password and is logged in.</summary>
    private static async Task<Results<Ok<PortalMeResponse>, ProblemHttpResult>> AcceptInviteAsync(
        AcceptInviteRequest request,
        HttpContext context,
        FirmAdmins admins,
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
        var (outcome, adminId, _) = string.IsNullOrEmpty(request.Token)
            ? (AdminInviteOutcome.Invalid, Guid.Empty, "")
            : await admins.AcceptInviteAsync(firm.Id, request.Token, hasher.HashPassword(null!, request.Password), time.GetUtcNow(), cancellationToken);
        switch (outcome)
        {
            case AdminInviteOutcome.Invalid:
                return AccountActions.Problem(StatusCodes.Status401Unauthorized, "The invitation has expired or was already used.");
            case AdminInviteOutcome.AlreadyAdmin:
                return AccountActions.Problem(StatusCodes.Status409Conflict, "You are already an administrator. Log in with your email and password.");
            default:
                var admin = (await users.FindByIdAsync(adminId, PortalRoles.Admin, cancellationToken))!;
                await PortalAuth.SignInAsync(context, admin);
                return TypedResults.Ok(new PortalMeResponse(admin.Id, admin.Email, admin.Role, firm.Name));
        }
    }

    private static Ok<FirmSettingsResponse> GetFirm(HttpContext context, IOptions<SandboxOptions> sandbox) =>
        TypedResults.Ok(FirmSettingsResponse.From(PortalFirmFilter.FirmOf(context), sandbox.Value));

    private static async Task<Results<Ok<FirmSettingsResponse>, ProblemHttpResult>> SetBrandingAsync(
        BrandingRequest request,
        HttpContext context,
        FirmStore store,
        FirmCatalog firms,
        IOptions<SandboxOptions> sandbox,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var logoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();
        var colors = request.Colors ?? new Dictionary<string, string>();
        if (!FirmRules.IsValidLogoUrl(logoUrl))
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "The logo must be an https address.");
        }

        if (FirmRules.ColorProblem(colors) is { } problem)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, problem);
        }

        var firm = PortalFirmFilter.FirmOf(context);
        await store.SetBrandingAsync(firm.Id, logoUrl, colors, time.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(FirmSettingsResponse.From(await ReloadAsync(firm, store, firms, cancellationToken), sandbox.Value));
    }

    /// <summary>A new key for the firm API. Shown only now; the old key stops working.</summary>
    private static async Task<Ok<ApiKeyResponse>> CreateApiKeyAsync(
        HttpContext context,
        FirmStore store,
        FirmCatalog firms,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var apiKey = NewSecret();
        await store.SetApiKeyHashAsync(firm.Id, FirmCatalog.HashApiKeyBytes(apiKey), time.GetUtcNow(), cancellationToken);
        await ReloadAsync(firm, store, firms, cancellationToken);
        return TypedResults.Ok(new ApiKeyResponse(apiKey));
    }

    /// <summary>Sets where webhooks go, or turns them off. The first time, a secret is made and shown, only now.</summary>
    private static async Task<Results<Ok<WebhookResponse>, ProblemHttpResult>> SetWebhookAsync(
        WebhookRequest request,
        HttpContext context,
        FirmStore store,
        FirmCatalog firms,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        Uri? url = null;
        if (!string.IsNullOrWhiteSpace(request.Url)
            && (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out url) || url.Scheme != Uri.UriSchemeHttps))
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "The webhook address must be an https address.");
        }

        var firm = PortalFirmFilter.FirmOf(context);
        var secret = url is not null && !await store.HasWebhookSecretAsync(firm.Id, cancellationToken) ? NewSecret() : null;
        await store.SetWebhookAsync(firm.Id, url, secret, time.GetUtcNow(), cancellationToken);
        await ReloadAsync(firm, store, firms, cancellationToken);
        return TypedResults.Ok(new WebhookResponse(url, secret));
    }

    /// <summary>A new secret that signs the webhooks. Shown only now; webhooks are signed with it from now on.</summary>
    private static async Task<Ok<WebhookSecretResponse>> CreateWebhookSecretAsync(
        HttpContext context,
        FirmStore store,
        FirmCatalog firms,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var secret = NewSecret();
        await store.SetWebhookSecretAsync(firm.Id, secret, time.GetUtcNow(), cancellationToken);
        await ReloadAsync(firm, store, firms, cancellationToken);
        return TypedResults.Ok(new WebhookSecretResponse(secret));
    }

    /// <summary>The ready-made challenges a firm starts from, in the currency of its accounts.</summary>
    private static Ok<List<ChallengeTemplateResponse>> GetChallengeTemplates(HttpContext context)
    {
        var currency = PortalFirmFilter.FirmOf(context).Trading?.Currency ?? "USD";
        return TypedResults.Ok<List<ChallengeTemplateResponse>>(
        [
            new(
                "two-step",
                "Two-step",
                "Profit targets of 10 and 5 percent, 5 percent daily loss, 10 percent max loss and an 80 percent profit split.",
                ChallengeTemplates.TwoStep("two-step-100k", 100_000m, currency)),
        ]);
    }

    private static Task<Results<Ok<ChallengeDefinition>, ProblemHttpResult>> SaveChallengeAsync(
        string challengeId,
        ChallengeDefinition definition,
        HttpContext context,
        ChallengeCatalog catalog,
        CancellationToken cancellationToken) =>
        FirmEndpoints.SaveChallengeOfAsync(PortalFirmFilter.FirmOf(context), challengeId, definition, catalog, cancellationToken);

    private static async Task<Ok<AdminsResponse>> ListAdminsAsync(
        HttpContext context,
        ClaimsPrincipal principal,
        FirmAdmins admins,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var me = PortalAuth.UserIdOf(principal);
        var all = await admins.ListAsync(firm.Id, cancellationToken);
        var invites = await admins.ListInvitesAsync(firm.Id, time.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(new AdminsResponse(
            [.. all.Select(a => new AdminResponse(a.Id, a.Email, a.CreatedAt, a.Id == me))],
            [.. invites.Select(i => new AdminInviteResponse(i.Email, i.ExpiresAt))]));
    }

    /// <summary>Emails an invitation to administer the firm. It replaces earlier ones to the same address.</summary>
    private static async Task<Results<Created<AdminInviteResponse>, ProblemHttpResult>> InviteAdminAsync(
        AdminInviteRequest request,
        HttpContext context,
        ClaimsPrincipal principal,
        FirmAdmins admins,
        IEmailSender email,
        IOptions<PlatformOptions> platform,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@', StringComparison.Ordinal))
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "A valid email address is required.");
        }

        var firm = PortalFirmFilter.FirmOf(context);
        var address = request.Email.Trim();
        if (await admins.IsAdminAsync(firm.Id, address, cancellationToken))
        {
            return AccountActions.Problem(StatusCodes.Status409Conflict, "That person is already an administrator.");
        }

        var (token, expiresAt) = await admins.CreateInviteAsync(firm.Id, address, time.GetUtcNow(), cancellationToken);
        try
        {
            var invitedBy = principal.FindFirstValue(ClaimTypes.Email) ?? firm.Name;
            var link = new Uri(firm.Portal.Url, $"admin/invite?token={token}");
            await email.SendAsync(PlatformEmails.InviteAdmin(platform.Value.Name, firm.Name, invitedBy, address, link, FirmAdmins.InviteLifetime), cancellationToken);
        }
        catch (EmailNotSentException)
        {
            await admins.WithdrawInviteAsync(token, CancellationToken.None);
            return AccountActions.Problem(StatusCodes.Status503ServiceUnavailable, "The invitation could not be sent. Try again shortly.");
        }

        return TypedResults.Created((string?)null, new AdminInviteResponse(address, expiresAt));
    }

    /// <summary>Removes an administrator, whose session stops working at once. An administrator cannot remove themselves.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveAdminAsync(
        Guid adminId,
        HttpContext context,
        ClaimsPrincipal principal,
        FirmAdmins admins,
        CancellationToken cancellationToken)
    {
        if (adminId == PortalAuth.UserIdOf(principal))
        {
            return AccountActions.Problem(StatusCodes.Status409Conflict, "You cannot remove yourself. Ask another administrator.");
        }

        return await admins.RemoveAsync(PortalFirmFilter.FirmOf(context).Id, adminId, cancellationToken)
            ? TypedResults.NoContent()
            : AccountActions.Problem(StatusCodes.Status404NotFound, "The firm has no such administrator.");
    }

    private static async Task<Firm> ReloadAsync(Firm firm, FirmStore store, FirmCatalog firms, CancellationToken cancellationToken)
    {
        var reloaded = await store.GetAsync(firm.Id, cancellationToken) ?? throw new InvalidOperationException($"Firm {firm.Id} disappeared.");
        firms.Put(reloaded);
        return reloaded;
    }

    /// <summary>256 random bits, safe in a header.</summary>
    private static string NewSecret() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}

public sealed record WelcomeRequest(string? Token);

/// <summary>The firm's settings for its admin panel. <paramref name="SandboxMaxOpenAccounts"/> is set while the firm is in the sandbox.</summary>
public sealed record FirmSettingsResponse(
    string Id,
    string Name,
    FirmStatus Status,
    Uri PortalUrl,
    string? LogoUrl,
    IReadOnlyDictionary<string, string> Colors,
    string? TradingServer,
    string? Currency,
    bool HasApiKey,
    Uri? WebhookUrl,
    int? SandboxMaxOpenAccounts)
{
    internal static FirmSettingsResponse From(Firm firm, SandboxOptions sandbox) =>
        new(
            firm.Id,
            firm.Name,
            firm.Status,
            firm.Portal.Url,
            firm.Portal.Branding.LogoUrl,
            firm.Portal.Branding.Colors,
            firm.Trading?.Server,
            firm.Trading?.Currency,
            firm.ApiKeyHash is not null,
            firm.Webhook?.Url,
            firm.Status == FirmStatus.Live ? null : sandbox.MaxOpenAccounts);
}

/// <summary>The logo as an https address, or empty for none, and the portal's colors to override, as #rrggbb.</summary>
public sealed record BrandingRequest(string? LogoUrl, IReadOnlyDictionary<string, string>? Colors);

/// <summary>The key for the firm API. It is shown only once.</summary>
public sealed record ApiKeyResponse(string ApiKey);

/// <summary>An https address for webhooks, or empty to turn them off.</summary>
public sealed record WebhookRequest(string? Url);

/// <summary><paramref name="Secret"/> is set only when it was just made, and is shown only once.</summary>
public sealed record WebhookResponse(Uri? Url, string? Secret);

public sealed record WebhookSecretResponse(string Secret);

/// <summary>A ready-made challenge with its default values, to adjust and save as the firm's own.</summary>
public sealed record ChallengeTemplateResponse(string Id, string Name, string Description, ChallengeDefinition Definition);

/// <summary><paramref name="IsYou"/> marks the administrator who asked.</summary>
public sealed record AdminResponse(Guid Id, string Email, DateTimeOffset CreatedAt, bool IsYou);

public sealed record AdminInviteRequest(string? Email);

public sealed record AdminInviteResponse(string Email, DateTimeOffset ExpiresAt);

/// <summary>The firm's administrators, and the invitations that wait for a password.</summary>
public sealed record AdminsResponse(IReadOnlyList<AdminResponse> Admins, IReadOnlyList<AdminInviteResponse> Invites);
