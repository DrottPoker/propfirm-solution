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
using Prop.Api.Net;
using Prop.Api.Payments;
using Prop.Api.Review;
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
        admin.MapPut("/firm/email-settings", SetEmailSettingsAsync);
        admin.MapGet("/firm/email-settings/{kind}/preview", PreviewEmailAsync);
        admin.MapPut("/firm/support-email", SetSupportEmailAsync);
        admin.MapPut("/firm/shop-payouts", SetShopPayoutsAsync);
        admin.MapPut("/firm/logo", UploadLogoAsync).DisableAntiforgery();
        admin.MapDelete("/firm/logo", RemoveLogoAsync);
        admin.MapPost("/firm/api-key", CreateApiKeyAsync);
        admin.MapPut("/firm/webhook", SetWebhookAsync);
        admin.MapGet("/firm/webhook", GetWebhookAsync);
        admin.MapPost("/firm/webhook/test", SendTestWebhookAsync);
        admin.MapPost("/firm/webhook/secret", CreateWebhookSecretAsync);
        admin.MapGet("/challenge-templates", GetChallengeTemplates);
        admin.MapPut("/challenges/{challengeId}", SaveChallengeAsync);
        admin.MapGet("/admins", ListAdminsAsync);
        admin.MapPost("/admins/invites", InviteAdminAsync);
        admin.MapPost("/admins/invites/withdraw", WithdrawAdminInviteAsync);
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
        return TypedResults.Ok(PortalMeResponse.Of(admin, firm.Name));
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
                return TypedResults.Ok(PortalMeResponse.Of(admin, firm.Name));
        }
    }

    private static Ok<FirmSettingsResponse> GetFirm(HttpContext context, OrderService orders, IOptions<SandboxOptions> sandbox, IOptions<PlatformOptions> platform)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        return TypedResults.Ok(FirmSettingsResponse.From(firm, sandbox.Value, platform.Value, ShopEndpoints.SettingsOf(firm, orders, platform.Value)));
    }

    private static async Task<Results<Ok<FirmSettingsResponse>, ProblemHttpResult>> SetBrandingAsync(
        BrandingRequest request,
        HttpContext context,
        FirmStore store,
        FirmCatalog firms,
        OrderService orders,
        IOptions<SandboxOptions> sandbox,
        IOptions<PlatformOptions> platform,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var colors = request.Colors ?? new Dictionary<string, string>();
        if (FirmRules.ColorProblem(colors) is { } problem)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, problem);
        }

        var firm = PortalFirmFilter.FirmOf(context);
        await store.SetColorsAsync(firm.Id, colors, time.GetUtcNow(), cancellationToken);
        var saved = await ReloadAsync(firm, store, firms, cancellationToken);
        return TypedResults.Ok(FirmSettingsResponse.From(saved, sandbox.Value, platform.Value, ShopEndpoints.SettingsOf(saved, orders, platform.Value)));
    }

    /// <summary>Turns notification emails on or off by kind. Kinds that are left out keep their setting.</summary>
    private static async Task<Results<Ok<FirmSettingsResponse>, ProblemHttpResult>> SetEmailSettingsAsync(
        EmailSettingsRequest request,
        HttpContext context,
        FirmStore store,
        FirmCatalog firms,
        OrderService orders,
        IOptions<SandboxOptions> sandbox,
        IOptions<PlatformOptions> platform,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var changes = request.Settings ?? new Dictionary<string, bool>();
        if (changes.Keys.FirstOrDefault(k => !NotificationKinds.All.Contains(k)) is { } unknown)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, $"There is no email called {unknown}.");
        }

        var firm = PortalFirmFilter.FirmOf(context);
        var settings = new Dictionary<string, bool>(firm.EmailSettings ?? new Dictionary<string, bool>());
        foreach (var (kind, on) in changes)
        {
            settings[kind] = on;
        }

        await store.SetEmailSettingsAsync(firm.Id, settings, time.GetUtcNow(), cancellationToken);
        var saved = await ReloadAsync(firm, store, firms, cancellationToken);
        return TypedResults.Ok(FirmSettingsResponse.From(saved, sandbox.Value, platform.Value, ShopEndpoints.SettingsOf(saved, orders, platform.Value)));
    }

    /// <summary>
    /// The notification email of the kind as it would go out for the firm now, in its name and look and about its first
    /// challenge, with a sample trader, account, payout and support ticket. Nothing is sent, queued or saved. 404 for a kind
    /// there is no email of.
    /// </summary>
    private static async Task<Results<Ok<NotificationPreviewResponse>, ProblemHttpResult>> PreviewEmailAsync(
        string kind,
        HttpContext context,
        ClaimsPrincipal principal,
        Notifications notifications,
        ChallengeCatalog challenges,
        PriceCatalog prices,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!NotificationKinds.All.Contains(kind))
        {
            return AccountActions.Problem(StatusCodes.Status404NotFound, $"There is no email called {kind}.");
        }

        var firm = PortalFirmFilter.FirmOf(context);
        var preview = notifications.Preview(
            firm,
            await challenges.ListAsync(firm.Id, cancellationToken),
            await prices.ListAsync(firm.Id, cancellationToken),
            kind,
            principal.FindFirstValue(ClaimTypes.Email) ?? "",
            time.GetUtcNow());
        var message = preview.Message;
        return TypedResults.Ok(new NotificationPreviewResponse(
            preview.Kind, preview.Audience, message.Subject, message.Html ?? throw new InvalidOperationException($"The email {kind} has no HTML."), message.Body));
    }

    /// <summary>Where replies to the emails to the firm's traders go. Empty for nowhere.</summary>
    private static async Task<Results<Ok<FirmSettingsResponse>, ProblemHttpResult>> SetSupportEmailAsync(
        SupportEmailRequest request,
        HttpContext context,
        FirmStore store,
        FirmCatalog firms,
        OrderService orders,
        IOptions<SandboxOptions> sandbox,
        IOptions<PlatformOptions> platform,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (email is not null && (email.Length > 254 || !MimeKit.MailboxAddress.TryParse(email, out var address) || address.Address != email || !email.Contains('@', StringComparison.Ordinal)))
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "Write the support address as an email address, for example support@yourfirm.com.");
        }

        var firm = PortalFirmFilter.FirmOf(context);
        await store.SetSupportEmailAsync(firm.Id, email, time.GetUtcNow(), cancellationToken);
        var saved = await ReloadAsync(firm, store, firms, cancellationToken);
        return TypedResults.Ok(FirmSettingsResponse.From(saved, sandbox.Value, platform.Value, ShopEndpoints.SettingsOf(saved, orders, platform.Value)));
    }

    /// <summary>
    /// Whether the shop shows what the firm paid out to traders in the last 30 days and how soon. Off until the firm turns
    /// it on, since it makes the firm's own figures public.
    /// </summary>
    private static async Task<Ok<FirmSettingsResponse>> SetShopPayoutsAsync(
        ShopPayoutsRequest request,
        HttpContext context,
        FirmStore store,
        FirmCatalog firms,
        OrderService orders,
        IOptions<SandboxOptions> sandbox,
        IOptions<PlatformOptions> platform,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        await store.SetShopShowsPayoutsAsync(firm.Id, request.Show, time.GetUtcNow(), cancellationToken);
        var saved = await ReloadAsync(firm, store, firms, cancellationToken);
        return TypedResults.Ok(FirmSettingsResponse.From(saved, sandbox.Value, platform.Value, ShopEndpoints.SettingsOf(saved, orders, platform.Value)));
    }

    /// <summary>A PNG, JPEG, WebP or SVG logo of at most 1 MB, in the form field file. It replaces the firm's earlier logo.</summary>
    private static async Task<Results<Ok<FirmSettingsResponse>, ProblemHttpResult>> UploadLogoAsync(
        IFormFile? file,
        HttpContext context,
        FirmStore store,
        FirmCatalog firms,
        OrderService orders,
        WorkSignals signals,
        IOptions<SandboxOptions> sandbox,
        IOptions<PlatformOptions> platform,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "Choose an image file.");
        }

        if (file.Length > FirmLogo.MaxBytes)
        {
            return AccountActions.Problem(StatusCodes.Status413PayloadTooLarge, "A logo can be at most 1 MB.");
        }

        using var content = new MemoryStream((int)file.Length);
        await file.CopyToAsync(content, cancellationToken);
        var (logo, problem) = FirmLogo.From(content.ToArray());
        if (logo is null)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, problem!);
        }

        var firm = PortalFirmFilter.FirmOf(context);
        await store.SetLogoAsync(firm.Id, logo, time.GetUtcNow(), cancellationToken);
        var saved = await ReloadAsync(firm, store, firms, cancellationToken);

        // The terminal shows the logo too.
        signals.Provisioning.Set();
        return TypedResults.Ok(FirmSettingsResponse.From(saved, sandbox.Value, platform.Value, ShopEndpoints.SettingsOf(saved, orders, platform.Value)));
    }

    /// <summary>Removes the logo, so the portal shows the firm's name.</summary>
    private static async Task<Ok<FirmSettingsResponse>> RemoveLogoAsync(
        HttpContext context,
        FirmStore store,
        FirmCatalog firms,
        OrderService orders,
        WorkSignals signals,
        IOptions<SandboxOptions> sandbox,
        IOptions<PlatformOptions> platform,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        await store.RemoveLogoAsync(firm.Id, time.GetUtcNow(), cancellationToken);
        var saved = await ReloadAsync(firm, store, firms, cancellationToken);
        signals.Provisioning.Set();
        return TypedResults.Ok(FirmSettingsResponse.From(saved, sandbox.Value, platform.Value, ShopEndpoints.SettingsOf(saved, orders, platform.Value)));
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

    /// <summary>
    /// Sets where webhooks go, or turns them off. The first time, a secret is made and shown, only now. The address must be
    /// on the public internet (ADR 0044); a name is checked again each time it is called.
    /// </summary>
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

        if (url is not null && !PublicAddresses.MayBeCalled(url.Host))
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "The webhook address must be on the public internet.");
        }

        var firm = PortalFirmFilter.FirmOf(context);
        var secret = url is not null && !await store.HasWebhookSecretAsync(firm.Id, cancellationToken) ? NewSecret() : null;
        await store.SetWebhookAsync(firm.Id, url, secret, time.GetUtcNow(), cancellationToken);
        await ReloadAsync(firm, store, firms, cancellationToken);
        return TypedResults.Ok(new WebhookResponse(url, secret));
    }

    /// <summary>Where webhooks go, the events they tell about, and the latest deliveries with how they went.</summary>
    private static async Task<Ok<WebhookOverviewResponse>> GetWebhookAsync(HttpContext context, WebhookDeliveries deliveries, CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        return TypedResults.Ok(new WebhookOverviewResponse(
            firm.Webhook?.Url,
            [.. WebhookEvents.All.Select(e => new WebhookEventResponse(e.Type, e.Description))],
            [.. (await deliveries.LatestAsync(firm.Id, WebhookDeliveries.Shown, cancellationToken)).Select(WebhookDeliveryResponse.From)]));
    }

    /// <summary>Queues the event webhook.test to the firm's address, so the firm can see that its system receives and checks them.</summary>
    private static async Task<Results<Accepted, ProblemHttpResult>> SendTestWebhookAsync(
        HttpContext context,
        WebhookDeliveries deliveries,
        WorkSignals signals,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        if (firm.Webhook is null)
        {
            return AccountActions.Problem(StatusCodes.Status409Conflict, "Save the webhook's address first.");
        }

        await deliveries.AddTestAsync(firm, time.GetUtcNow(), cancellationToken);
        signals.Webhooks.Set();
        return TypedResults.Accepted((string?)null);
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
                "one-step",
                "One-step",
                "One phase with a 10 percent profit target, 4 percent daily loss, 6 percent max loss and an 80 percent profit split.",
                ChallengeTemplates.OneStep("one-step-100k", 100_000m, currency)),
            new(
                "two-step",
                "Two-step",
                "Profit targets of 10 and 5 percent, 5 percent daily loss, 10 percent max loss and an 80 percent profit split.",
                ChallengeTemplates.TwoStep("two-step-100k", 100_000m, currency)),
            new(
                "three-step",
                "Three-step",
                "Three phases with 6 percent targets, 5 percent daily loss, 10 percent max loss and an 80 percent profit split.",
                ChallengeTemplates.ThreeStep("three-step-100k", 100_000m, currency)),
            new(
                "instant-funded",
                "Instant funded",
                "Funded from the start, without evaluation: 3 percent daily loss, 6 percent trailing max loss and a 70 percent profit split.",
                ChallengeTemplates.InstantFunded("instant-funded-100k", 100_000m, currency)),
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
            [.. all.Select(a => new AdminResponse(a.Id, a.Email, a.CreatedAt, a.LastLoginAt, a.Id == me))],
            [.. invites.Select(i => new AdminInviteResponse(i.Email, i.ExpiresAt))]));
    }

    /// <summary>Takes back the invitation to the email, so its link no longer works.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> WithdrawAdminInviteAsync(
        AdminInviteRequest request,
        HttpContext context,
        FirmAdmins admins,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        !string.IsNullOrWhiteSpace(request.Email) && await admins.WithdrawInvitesAsync(PortalFirmFilter.FirmOf(context).Id, request.Email, time.GetUtcNow(), cancellationToken)
            ? TypedResults.NoContent()
            : AccountActions.Problem(StatusCodes.Status404NotFound, "There is no invitation to that email.");

    /// <summary>
    /// Emails an invitation to administer the firm. It replaces earlier ones to the same address, so sending it again is
    /// the same. Before we approve the firm, it sends a few in a month at most (ADR 0043).
    /// </summary>
    private static async Task<Results<Created<AdminInviteResponse>, ProblemHttpResult>> InviteAdminAsync(
        AdminInviteRequest request,
        HttpContext context,
        ClaimsPrincipal principal,
        FirmAdmins admins,
        FirmApproval approval,
        IEmailSender email,
        IOptions<PlatformOptions> platform,
        IOptions<SandboxOptions> sandbox,
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

        var limit = await approval.IsApprovedAsync(firm.Id, cancellationToken) ? (int?)null : sandbox.Value.MaxAdminInvites;
        if (await admins.CreateInviteAsync(firm.Id, address, limit, time.GetUtcNow(), cancellationToken) is not { } invite)
        {
            return AccountActions.Problem(
                StatusCodes.Status409Conflict,
                FormattableString.Invariant($"Until we have approved your firm, it can send {limit} invitations in 30 days. You can send more once we have approved it."));
        }

        var (token, expiresAt) = invite;
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

    /// <summary>The firm as saved, put into the catalog so every request sees the change.</summary>
    internal static async Task<Firm> ReloadAsync(Firm firm, FirmStore store, FirmCatalog firms, CancellationToken cancellationToken)
    {
        var reloaded = await store.GetAsync(firm.Id, cancellationToken) ?? throw new InvalidOperationException($"Firm {firm.Id} disappeared.");
        firms.Put(reloaded);
        return reloaded;
    }

    /// <summary>256 random bits, safe in a header.</summary>
    private static string NewSecret() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}

public sealed record WelcomeRequest(string? Token);

/// <summary>
/// The firm's settings for its admin panel. <paramref name="SandboxMaxOpenAccounts"/> is set while the firm is in
/// the sandbox. <paramref name="Payments"/> is how its portal takes payment. <paramref name="EmailSettings"/> has every
/// notification email by kind, and whether the firm sends it, and <paramref name="SupportEmail"/> is where replies to the
/// emails to its traders go. <paramref name="FirmApiUrl"/> is where the firm's own systems reach the firm API, and
/// <paramref name="OpenApiUrl"/> its description for code generators. <paramref name="ShopShowsPayouts"/> is whether the
/// shop shows what the firm paid out lately.
/// </summary>
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
    int? SandboxMaxOpenAccounts,
    PaymentSettingsResponse Payments,
    IReadOnlyDictionary<string, bool> EmailSettings,
    Uri FirmApiUrl,
    Uri OpenApiUrl,
    string? SupportEmail,
    bool ShopShowsPayouts)
{
    internal static FirmSettingsResponse From(Firm firm, SandboxOptions sandbox, PlatformOptions platform, PaymentSettingsResponse payments) =>
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
            firm.Status == FirmStatus.Live ? null : sandbox.MaxOpenAccounts,
            payments,
            NotificationKinds.All.ToDictionary(kind => kind, kind => NotificationKinds.IsOn(firm, kind)),
            new Uri(platform.ApiUrl!, "api/firm/v1/"),
            new Uri(platform.ApiUrl!, "openapi/v1.json"),
            firm.SupportEmail,
            firm.ShopShowsPayouts);
}

/// <summary>Where replies to the emails to the firm's traders go. Empty for nowhere.</summary>
public sealed record SupportEmailRequest(string? Email);

/// <summary>Whether the shop shows what the firm paid out to traders in the last 30 days and how soon.</summary>
public sealed record ShopPayoutsRequest(bool Show);

/// <summary>Notification emails to turn on (true) or off (false), by kind. Kinds that are left out keep their setting.</summary>
public sealed record EmailSettingsRequest(IReadOnlyDictionary<string, bool>? Settings);

/// <summary>
/// A notification email as it would go out for the firm now, with sample data: who it goes to, its subject, and the same
/// email as HTML and as plain text.
/// </summary>
public sealed record NotificationPreviewResponse(string Kind, EmailAudience Audience, string Subject, string Html, string Text);

/// <summary>The portal's colors to override, as #rrggbb. The logo is uploaded on its own.</summary>
public sealed record BrandingRequest(IReadOnlyDictionary<string, string>? Colors);

/// <summary>The key for the firm API. It is shown only once.</summary>
public sealed record ApiKeyResponse(string ApiKey);

/// <summary>An https address for webhooks, or empty to turn them off.</summary>
public sealed record WebhookRequest(string? Url);

/// <summary><paramref name="Secret"/> is set only when it was just made, and is shown only once.</summary>
public sealed record WebhookResponse(Uri? Url, string? Secret);

public sealed record WebhookSecretResponse(string Secret);

/// <summary>A ready-made challenge with its default values, to adjust and save as the firm's own.</summary>
public sealed record ChallengeTemplateResponse(string Id, string Name, string Description, ChallengeDefinition Definition);

/// <summary>
/// <paramref name="LastLoginAt"/> is when the administrator last logged in, empty for one who has not since we began to keep
/// it, and <paramref name="IsYou"/> marks the administrator who asked.
/// </summary>
public sealed record AdminResponse(Guid Id, string Email, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt, bool IsYou);

public sealed record AdminInviteRequest(string? Email);

public sealed record AdminInviteResponse(string Email, DateTimeOffset ExpiresAt);

/// <summary>The firm's administrators, and the invitations that wait for a password.</summary>
public sealed record AdminsResponse(IReadOnlyList<AdminResponse> Admins, IReadOnlyList<AdminInviteResponse> Invites);

/// <summary>Where webhooks go, every event they tell about, and the latest deliveries, the newest first.</summary>
public sealed record WebhookOverviewResponse(Uri? Url, IReadOnlyList<WebhookEventResponse> Events, IReadOnlyList<WebhookDeliveryResponse> Deliveries);

public sealed record WebhookEventResponse(string Type, string Description);

/// <summary>
/// One webhook and how it went: <c>Delivered</c>, <c>Failed</c> after the last try, or <c>Pending</c> while it is tried
/// again, with the firm's last answer (<paramref name="LastStatus"/>) or the error.
/// </summary>
public sealed record WebhookDeliveryResponse(
    Guid Id,
    string EventType,
    DateTimeOffset CreatedAt,
    string Status,
    int Attempts,
    int? LastStatus,
    string? LastError,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? NextAttemptAt)
{
    internal static WebhookDeliveryResponse From(WebhookDelivery delivery) =>
        new(
            delivery.Id,
            delivery.EventType,
            delivery.CreatedAt,
            delivery.DeliveredAt is not null ? "Delivered" : delivery.FailedAt is not null ? "Failed" : "Pending",
            delivery.Attempts,
            delivery.LastStatus,
            delivery.LastError,
            delivery.DeliveredAt,
            delivery.DeliveredAt is null && delivery.FailedAt is null ? delivery.NextAttemptAt : null);
}
