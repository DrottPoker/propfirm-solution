using System.Text.Json.Nodes;

using Microsoft.Extensions.Options;

using Npgsql;

using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Email;
using Prop.Api.Firms;
using Prop.Api.Portal;

namespace Prop.Api.Identity;

/// <summary>
/// ID checks of a firm's traders, its KYC (ADR 0042). Before it goes live, a firm chooses our built-in checks through
/// Didit, or its own service, which tells us the outcome through the firm API and must have worked through the whole
/// flow. A check that is approved ticks "ID checked" on the trader's card, and the firm chooses whether payouts or the
/// funded account wait for it. The firm can tick "ID checked" by hand as an
/// exception. We keep only the outcome and what the document said; the document and the pictures stay with the provider.
/// </summary>
internal sealed class IdentityService(
    IdentityStore store,
    DiditChecker didit,
    TestChecker test,
    PortalUsers users,
    TraderChecks checks,
    FirmCatalog firms,
    WorkSignals signals,
    IOptions<IdentityCheckOptions> options,
    IOptions<BillingOptions> billing,
    TimeProvider time)
{
    /// <summary>A check started this long ago and not sent in yet is opened again instead of starting another.</summary>
    public static readonly TimeSpan ReuseWindow = TimeSpan.FromMinutes(30);

    /// <summary>A check that waits for the provider is asked about at most this often when the trader looks.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    public const int MaxReasonLength = 500;

    public const int MaxNameLength = 200;

    /// <summary>Who ticked "ID checked" when the firm's own service approved the trader.</summary>
    public const string ExternalChecker = "Your KYC service";

    /// <summary>The firm's choice, with our prices and the built-in checks since the firm was last charged for them.</summary>
    public async Task<IdentitySettingsResponse> SettingsViewAsync(Firm firm, CancellationToken cancellationToken)
    {
        var prices = billing.Value.IdentityChecks;
        return IdentitySettingsResponse.From(
            await store.GetSettingsAsync(firm.Id, cancellationToken),
            new IdentityPricesResponse(billing.Value.Currency, prices.MonthlyPrice, prices.Included, prices.PerCheck, prices.Address, prices.Sanctions),
            await store.CountUnbilledAsync(firm.Id, cancellationToken),
            CheckerFor(firm).Provider == IdentityProvider.Test);
    }

    /// <summary>Saves how the firm checks its traders. Its own service needs the address of its page.</summary>
    public async Task<IdentityResult> SaveSettingsAsync(Firm firm, IdentitySettingsRequest request, string adminEmail, CancellationToken cancellationToken)
    {
        if (request.Mode is not { } mode)
        {
            return new IdentityResult.Refused(StatusCodes.Status422UnprocessableEntity, "Choose our built-in KYC or your own KYC service.", "mode");
        }

        string? externalUrl = null;
        if (mode == IdentityMode.External)
        {
            if (ExternalUrlProblem(request.ExternalUrl, out externalUrl) is { } problem)
            {
                return new IdentityResult.Refused(StatusCodes.Status422UnprocessableEntity, problem, "externalUrl");
            }
        }

        var settings = new IdentitySettings(mode, request.RequiredBefore, request.CheckAddress, request.CheckSanctions, externalUrl);
        await store.SaveSettingsAsync(firm.Id, settings, adminEmail, time.GetUtcNow(), cancellationToken);
        return new IdentityResult.Done();
    }

    /// <summary>
    /// The trader's own check, asked about at the provider first when it waits for it, so the outcome shows even when the
    /// provider's message was lost.
    /// </summary>
    public async Task<MyIdentityResponse> MineAsync(Firm firm, Guid traderId, CancellationToken cancellationToken)
    {
        var settings = await store.GetSettingsAsync(firm.Id, cancellationToken);
        var identity = await store.GetTraderAsync(traderId, cancellationToken);
        if (identity is { Status: IdentityStatus.Pending or IdentityStatus.InReview, Provider: IdentityProvider.Didit, SessionId: { } sessionId }
            && await RefreshAsync(firm, sessionId, cancellationToken))
        {
            identity = await store.GetTraderAsync(traderId, cancellationToken);
        }

        var verified = await IsVerifiedAsync(traderId, identity, cancellationToken);
        var status = identity?.Status ?? IdentityStatus.NotStarted;
        return new MyIdentityResponse(
            settings?.Mode,
            settings?.RequiredBefore ?? IdentityRequirement.FirstPayout,
            status,
            verified,
            settings is not null && !verified && status != IdentityStatus.InReview,
            status == IdentityStatus.Declined ? identity?.Reason : null,
            identity?.DecidedAt);
    }

    /// <summary>
    /// Where the trader does the check: the provider's page, opened again when a check was started a moment ago, or the
    /// firm's own page. Refused when the firm has not chosen how, the trader is verified or a check is in review.
    /// </summary>
    public async Task<(IdentityResult Result, Uri? Url)> StartAsync(Firm firm, PortalUser trader, CancellationToken cancellationToken)
    {
        if (await store.GetSettingsAsync(firm.Id, cancellationToken) is not { } settings)
        {
            return (new IdentityResult.Refused(StatusCodes.Status409Conflict, $"{firm.Name} has not set up ID checks yet."), null);
        }

        var current = await store.GetTraderAsync(trader.Id, cancellationToken);
        if (await IsVerifiedAsync(trader.Id, current, cancellationToken))
        {
            return (new IdentityResult.Refused(StatusCodes.Status409Conflict, "Your identity is verified already."), null);
        }

        if (settings.Mode == IdentityMode.External)
        {
            var page = ExternalUrlFor(settings.ExternalUrl!, trader);
            await RecordExternalStartAsync(firm, trader, page, cancellationToken);
            return (new IdentityResult.Done(), page);
        }

        if (current?.Status == IdentityStatus.InReview)
        {
            return (new IdentityResult.Refused(StatusCodes.Status409Conflict, "Your check is being reviewed. You get an email when it is done."), null);
        }

        var checker = CheckerFor(firm);
        var now = time.GetUtcNow();
        if (await store.OpenSessionAsync(trader.Id, checker.Provider, now - ReuseWindow, cancellationToken) is { } open
            && open.CheckAddress == settings.CheckAddress
            && open.CheckSanctions == settings.CheckSanctions
            && current?.SessionId == open.Id)
        {
            return (new IdentityResult.Done(), open.Url);
        }

        var sessionId = Guid.CreateVersion7(now);
        (string ProviderSessionId, Uri Url) started;
        try
        {
            started = await checker.StartAsync(
                new IdentityCheckRequest(sessionId, trader.Id, settings.CheckAddress, settings.CheckSanctions, new Uri(firm.Portal.Url, "identity?returned=1"), firm.Portal.Url),
                cancellationToken);
        }
        catch (IdentityCheckNotStartedException exception)
        {
            return (new IdentityResult.Refused(StatusCodes.Status503ServiceUnavailable, exception.Message), null);
        }

        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await IdentityStore.InsertSessionAsync(
            connection,
            new IdentitySession(sessionId, firm.Id, trader.Id, checker.Provider, started.ProviderSessionId, started.Url, settings.CheckAddress, settings.CheckSanctions, IdentityStatus.Pending, now, null),
            cancellationToken);
        await IdentityStore.SaveTraderAsync(
            connection,
            firm.Id,
            new TraderIdentity(trader.Id, checker.Provider, sessionId, IdentityStatus.Pending, null, null, null, false, false, null, null, now),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (new IdentityResult.Done(), started.Url);
    }

    /// <summary>The test page approves or declines the trader's own test check, with the trader's name from the shop when it is known.</summary>
    public async Task<IdentityResult> DecideTestAsync(Firm firm, PortalUser trader, Guid sessionId, bool approve, CancellationToken cancellationToken)
    {
        var decision = approve
            ? new IdentityDecision(IdentityStatus.Approved, trader.Name ?? "Test Trader", new DateOnly(1990, 1, 1), trader.Country ?? "SE", true, true, null)
            : new IdentityDecision(IdentityStatus.Declined, null, null, null, false, false, "The test check was declined on the test page.");
        return await ApplyAsync(IdentityProvider.Test, sessionId.ToString(), decision, s => s.FirmId == firm.Id && s.TraderId == trader.Id, cancellationToken)
            ? new IdentityResult.Done()
            : new IdentityResult.Refused(StatusCodes.Status404NotFound, "No such test check.");
    }

    /// <summary>
    /// Didit tells about a check: what it decided is read from Didit's API, so only Didit's own word is saved. False when
    /// Didit's API could not tell. A check that is not ours is let be.
    /// </summary>
    public async Task<bool> DiditChangedAsync(string providerSessionId, CancellationToken cancellationToken)
    {
        if (!await store.HasSessionAsync(IdentityProvider.Didit, providerSessionId, cancellationToken))
        {
            return true;
        }

        if (await didit.DecisionAsync(providerSessionId, cancellationToken) is not { } decision)
        {
            return false;
        }

        await ApplyAsync(IdentityProvider.Didit, providerSessionId, decision, _ => true, cancellationToken);
        return true;
    }

    /// <summary>The firm's own service tells how the trader with the email was checked.</summary>
    public async Task<(IdentityResult Result, TraderIdentity? Identity)> ReportAsync(Firm firm, ExternalIdentityRequest request, CancellationToken cancellationToken)
    {
        if ((await store.GetSettingsAsync(firm.Id, cancellationToken))?.Mode != IdentityMode.External)
        {
            return (new IdentityResult.Refused(StatusCodes.Status409Conflict, "Choose your own KYC service under KYC in the admin panel first."), null);
        }

        if (request.Status is not (IdentityStatus.Pending or IdentityStatus.InReview or IdentityStatus.Approved or IdentityStatus.Declined))
        {
            return (new IdentityResult.Refused(StatusCodes.Status422UnprocessableEntity, "status must be Pending, InReview, Approved or Declined.", "status"), null);
        }

        if (Clean(request.Reason)?.Length > MaxReasonLength || Clean(request.FullName)?.Length > MaxNameLength || Clean(request.Country)?.Length > 100)
        {
            return (new IdentityResult.Refused(StatusCodes.Status422UnprocessableEntity, $"Keep the reason to {MaxReasonLength}, the name to {MaxNameLength} and the country to 100 characters."), null);
        }

        if (string.IsNullOrWhiteSpace(request.Email) || await users.FindTraderAsync(firm.Id, request.Email, cancellationToken) is not { } trader)
        {
            return (new IdentityResult.Refused(StatusCodes.Status404NotFound, "No trader at your firm has this email.", "email"), null);
        }

        var now = time.GetUtcNow();
        var status = request.Status.Value;
        var decided = status is IdentityStatus.Approved or IdentityStatus.Declined;
        var identity = new TraderIdentity(
            trader.Id,
            IdentityProvider.External,
            null,
            status,
            status == IdentityStatus.Approved ? Clean(request.FullName) : null,
            status == IdentityStatus.Approved ? request.DateOfBirth : null,
            status == IdentityStatus.Approved ? Clean(request.Country) : null,
            status == IdentityStatus.Approved && request.AddressChecked,
            false,
            status == IdentityStatus.Declined ? Clean(request.Reason) : null,
            decided ? now : null,
            now);
        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await IdentityStore.LockTraderAsync(connection, trader.Id, cancellationToken);
        await IdentityStore.SaveTraderAsync(connection, firm.Id, identity, cancellationToken);
        if (status == IdentityStatus.Approved)
        {
            await TickAsync(connection, identity, ExternalChecker, now, cancellationToken);
        }

        if (decided)
        {
            await IdentityStore.CompleteExternalFlowAsync(connection, firm.Id, trader.Id, status, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (new IdentityResult.Done(), identity);
    }

    /// <summary>The trader's ID check as it stands, or null when none was started or reported.</summary>
    public Task<TraderIdentity?> TraderAsync(Guid traderId, CancellationToken cancellationToken) => store.GetTraderAsync(traderId, cancellationToken);

    /// <summary>
    /// Why the requirement is not met: the firm wants the trader's ID checked first, and it is not. Null when it is met,
    /// and while the firm has not chosen how to check. A check the firm ticked by hand counts, so the firm can always let a
    /// trader through as an exception.
    /// </summary>
    public async Task<string?> ProblemAsync(Firm firm, Guid traderId, IdentityRequirement requirement, CancellationToken cancellationToken)
    {
        if (await store.GetSettingsAsync(firm.Id, cancellationToken) is not { } settings
            || !settings.Requires(requirement)
            || await IsVerifiedAsync(traderId, await store.GetTraderAsync(traderId, cancellationToken), cancellationToken))
        {
            return null;
        }

        return requirement == IdentityRequirement.FirstPayout
            ? "Verify your identity first, under Payouts."
            : "The trader's identity is not verified yet. The trader is asked to verify it in the portal. If you checked it yourself, tick ID checked on the trader's card.";
    }

    /// <summary>Why the firm's KYC keeps it from going live. Null when it is ready.</summary>
    public static string? ReadinessProblem(IdentityReadiness readiness) => readiness switch
    {
        IdentityReadiness.NotChosen => "Choose how your traders are checked, under KYC, before you go live.",
        IdentityReadiness.NotTested =>
            "Show that your own KYC service works, under KYC, before you go live: a trader starts the check in your portal, and your service reports the outcome through the firm API.",
        _ => null,
    };

    /// <summary>Why the address is not a page the firm's traders can be sent to, with {traderId} and {email} filled in. Null when it is.</summary>
    public static string? ExternalUrlProblem(string? template, out string? url)
    {
        url = null;
        var text = template?.Trim() ?? "";
        if (text.Length == 0 || text.Length > 2_000)
        {
            return "Add the address of your own page where traders are checked.";
        }

        var sample = text.Replace("{traderId}", Guid.Empty.ToString(), StringComparison.Ordinal).Replace("{email}", "trader%40example.com", StringComparison.Ordinal);
        if (!Uri.TryCreate(sample, UriKind.Absolute, out var parsed) || !(parsed.Scheme == Uri.UriSchemeHttps || (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback)))
        {
            return "The address must be a whole https address, such as https://kyc.yourfirm.com/start?trader={traderId}.";
        }

        url = text;
        return null;
    }

    /// <summary>The firm's page for the trader, with the trader's id and email in place of {traderId} and {email}.</summary>
    public static Uri ExternalUrlFor(string template, PortalUser trader) =>
        new(template
            .Replace("{traderId}", trader.Id.ToString(), StringComparison.Ordinal)
            .Replace("{email}", Uri.EscapeDataString(trader.Email), StringComparison.Ordinal));

    // Notes that the portal sent the trader to the firm's own page, so the firm's report of the outcome shows that the
    // whole flow works.
    private async Task RecordExternalStartAsync(Firm firm, PortalUser trader, Uri page, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        await using var connection = await store.OpenAsync(cancellationToken);
        await IdentityStore.InsertSessionAsync(
            connection,
            new IdentitySession(id, firm.Id, trader.Id, IdentityProvider.External, id.ToString(), page, false, false, IdentityStatus.Pending, now, null),
            cancellationToken);
    }

    // Firms in the sandbox only ever get test checks, which cost nothing.
    private IIdentityChecker CheckerFor(Firm firm) =>
        firm.Status == FirmStatus.Live && options.Value.Provider == nameof(IdentityProvider.Didit) ? didit : test;

    // Asks Didit about the check unless that was done a moment ago. True when something was saved.
    private async Task<bool> RefreshAsync(Firm firm, Guid sessionId, CancellationToken cancellationToken)
    {
        if (await store.GetSessionAsync(firm.Id, sessionId, cancellationToken) is not { } session
            || (session.CheckedAt is { } checkedAt && time.GetUtcNow() - checkedAt < RefreshInterval))
        {
            return false;
        }

        await store.MarkCheckedAsync(sessionId, time.GetUtcNow(), cancellationToken);
        return await didit.DecisionAsync(session.ProviderSessionId, cancellationToken) is { } decision
            && await ApplyAsync(IdentityProvider.Didit, session.ProviderSessionId, decision, _ => true, cancellationToken);
    }

    // Saves what the provider decided about a check of ours. The trader's outcome changes only while the check is the
    // trader's latest. Approval ticks the firm's checks, and a decision emails the trader and tells the firm's webhook.
    private async Task<bool> ApplyAsync(
        IdentityProvider provider,
        string providerSessionId,
        IdentityDecision decision,
        Func<IdentitySession, bool> allowed,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await IdentityStore.LockSessionAsync(connection, provider, providerSessionId, cancellationToken) is not { } session || !allowed(session))
        {
            return false;
        }

        await IdentityStore.UpdateSessionAsync(connection, session.Id, decision.Status, now, cancellationToken);
        var current = await IdentityStore.LockTraderAsync(connection, session.TraderId, cancellationToken);
        if (current?.SessionId != session.Id || current.Status == decision.Status)
        {
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        var identity = decision.Status switch
        {
            IdentityStatus.Approved => current with
            {
                Status = IdentityStatus.Approved,
                FullName = Clean(decision.FullName),
                DateOfBirth = decision.DateOfBirth,
                Country = Clean(decision.Country),
                AddressChecked = session.CheckAddress && decision.AddressApproved,
                SanctionsChecked = session.CheckSanctions && decision.SanctionsClear,
                Reason = null,
                DecidedAt = now,
                UpdatedAt = now,
            },
            IdentityStatus.Declined => current with { Status = IdentityStatus.Declined, Reason = decision.Reason, DecidedAt = now, UpdatedAt = now },
            _ => current with { Status = decision.Status, UpdatedAt = now },
        };
        await IdentityStore.SaveTraderAsync(connection, session.FirmId, identity, cancellationToken);
        if (identity.Status is IdentityStatus.Approved or IdentityStatus.Declined
            && firms.ById(session.FirmId) is { } firm
            && await users.FindByIdAsync(session.TraderId, PortalRoles.Trader, cancellationToken) is { } trader)
        {
            if (identity.Status == IdentityStatus.Approved)
            {
                await TickAsync(connection, identity, provider == IdentityProvider.Test ? "Test check" : provider.ToString(), now, cancellationToken);
            }

            await Notifications.QueueIdentityAsync(connection, firm, trader.Email, identity, now, cancellationToken);
            await WebhookOutbox.AddAsync(
                connection,
                firm,
                identity.Status == IdentityStatus.Approved ? IdentityWebhooks.Verified : IdentityWebhooks.Declined,
                null,
                IdentityWebhooks.Data(trader, identity),
                now,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        signals.Emails.Set();
        signals.Webhooks.Set();
        return true;
    }

    // An approved check ticks "ID checked", and "Address checked" when the address was checked too.
    private static async Task TickAsync(NpgsqlConnection connection, TraderIdentity identity, string checkedBy, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await TraderChecks.SetAsync(connection, identity.TraderId, TraderCheckItems.Identity, checkedBy, now, cancellationToken);
        if (identity.AddressChecked)
        {
            await TraderChecks.SetAsync(connection, identity.TraderId, TraderCheckItems.Address, checkedBy, now, cancellationToken);
        }
    }

    // Approved by a provider, or ticked by hand by the firm.
    private async Task<bool> IsVerifiedAsync(Guid traderId, TraderIdentity? identity, CancellationToken cancellationToken) =>
        identity?.Status == IdentityStatus.Approved
        || (await checks.ListAsync([traderId], cancellationToken))[traderId].Any(c => c.Item == TraderCheckItems.Identity);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>The webhooks about the built-in ID checks.</summary>
internal static class IdentityWebhooks
{
    public const string Verified = "trader.identity_verified";

    public const string Declined = "trader.identity_declined";

    /// <summary>The trader and the outcome, without the document.</summary>
    public static JsonObject Data(PortalUser trader, TraderIdentity identity) =>
        new()
        {
            ["trader"] = new JsonObject { ["id"] = trader.Id, ["email"] = trader.Email },
            ["identity"] = new JsonObject
            {
                ["status"] = identity.Status.ToString(),
                ["provider"] = identity.Provider.ToString(),
                ["fullName"] = identity.FullName,
                ["dateOfBirth"] = identity.DateOfBirth?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ["country"] = identity.Country,
                ["addressChecked"] = identity.AddressChecked,
                ["sanctionsChecked"] = identity.SanctionsChecked,
                ["reason"] = identity.Reason,
            },
        };
}
