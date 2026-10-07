using System.Security.Claims;

using Common.Postgres;

using Microsoft.Extensions.Options;

using Npgsql;

using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Email;
using Prop.Api.Firms;
using Prop.Api.Ops;
using Prop.Api.Trading;
using Prop.Rules;

namespace Prop.Api.Incidents;

/// <summary>How an action on an incident went: done, or a problem with the status code to answer with.</summary>
internal sealed record IncidentResult(int Status, string? Problem)
{
    public static readonly IncidentResult Done = new(StatusCodes.Status200OK, null);

    public bool Succeeded => Problem is null;

    public static IncidentResult NotFound(string problem) => new(StatusCodes.Status404NotFound, problem);

    public static IncidentResult Invalid(string problem) => new(StatusCodes.Status422UnprocessableEntity, problem);

    public static IncidentResult Conflict(string problem) => new(StatusCodes.Status409Conflict, problem);
}

/// <summary>
/// Outages of the trading platform and what is done about them (ADR 0053). The platform finds a price feed that stopped
/// by itself, and our staff write, publish and resolve incidents. A published incident shows on the firms' status pages,
/// in their terminals and admin panels, where each firm sees its accounts that the incident reached and reinstates or
/// credits them.
/// </summary>
internal sealed partial class IncidentService(
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    IncidentStore store,
    FirmCatalog firms,
    ITradingPlatform trading,
    ITradingPartner partner,
    ChallengeService challenges,
    ChallengeQueries queries,
    StaffNotifier staffNotifier,
    WorkSignals signals,
    IOptions<PlatformOptions> platform,
    IOptions<IncidentOptions> options,
    TimeProvider time,
    ILogger<IncidentService> logger)
{
    public const int MaxTitleLength = 120;
    public const int MaxTextLength = 2_000;
    public const int MaxReasonLength = 500;

    /// <summary>How far back a status page and a firm's list of incidents go.</summary>
    public static readonly TimeSpan ShownFor = TimeSpan.FromDays(90);

    /// <summary>What a found outage of the price feed says until our staff change it.</summary>
    public const string OutageText =
        "No prices are coming from the price feed. New orders, closes and stop changes are refused until prices return, so nothing is filled at an old price.";

    private static readonly IncidentResult NoSuchIncident = IncidentResult.NotFound("There is no such incident.");

    // The trading platform looks back at most a day, and 30 days back (ADR 0053).
    private static readonly TimeSpan MaxImpactLength = TimeSpan.FromDays(1);
    private static readonly TimeSpan MaxImpactAge = TimeSpan.FromDays(30);

    // A terminal notice has at most this many characters (the trading platform's limit).
    private const int MaxNoticeLength = 1_000;

    /// <summary>The staff member or administrator who acts, as their email.</summary>
    public static string ActorOf(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.Email) ?? "";

    // Our staff

    /// <summary>The incidents of the last 90 days and those that go on, newest first, with how the platform is doing now.</summary>
    public async Task<OpsIncidentsResponse> ListForStaffAsync(CancellationToken cancellationToken)
    {
        var incidents = await store.ListAsync(time.GetUtcNow() - ShownFor, publishedOnly: false, cancellationToken);
        PriceFeedNow? feed = null;
        try
        {
            var status = await partner.GetPriceFeedAsync(cancellationToken);
            var open = status.Symbols.Where(s => s.MarketOpen).ToList();
            feed = new PriceFeedNow(
                status.Feed,
                status.LastPriceAt,
                open.Count,
                open.Count(s => s.LastPriceAt is not { } at || status.Now - at >= options.Value.PriceGap));
        }
        catch (TradingPlatformUnavailableException exception)
        {
            LogFeedUnknown(logger, exception);
        }

        return new OpsIncidentsResponse([.. incidents.Select(ToOps)], feed, await ExposureAsync(cancellationToken));
    }

    /// <summary>How many drafts wait for our staff, such as an outage the platform found.</summary>
    public Task<int> DraftsAsync(CancellationToken cancellationToken) => store.CountDraftsAsync(cancellationToken);

    /// <summary>Writes a draft, which nobody but our staff sees until it is published.</summary>
    public async Task<(IncidentResult Result, OpsIncidentResponse? Incident)> CreateAsync(IncidentRequest request, string staff, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (Problem(request, now) is { } problem)
        {
            return (IncidentResult.Invalid(problem), null);
        }

        var incident = new Incident(
            Guid.CreateVersion7(now),
            request.Kind,
            request.Title!.Trim(),
            request.PublicText!.Trim(),
            request.InternalNote?.Trim() ?? "",
            request.StartedAt,
            request.EndedAt,
            IncidentStatus.Draft,
            null,
            FirmsOf(request),
            false,
            staff,
            now,
            []);
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await IncidentStore.InsertAsync(connection, incident, cancellationToken);
        return (IncidentResult.Done, ToOps(incident));
    }

    /// <summary>Changes what an incident says, when it was and whom it concerns. The terminals of a published one show the change at once.</summary>
    public async Task<(IncidentResult Result, OpsIncidentResponse? Incident)> EditAsync(Guid id, IncidentRequest request, string staff, CancellationToken cancellationToken)
    {
        if (Problem(request, time.GetUtcNow()) is { } problem)
        {
            return (IncidentResult.Invalid(problem), null);
        }

        return await ChangeAsync(
            id,
            (incident, _) => incident.Status == IncidentStatus.Resolved && request.EndedAt is null
                ? (incident, IncidentResult.Invalid("A resolved incident needs its end."), null)
                : (incident with
                {
                    Kind = request.Kind,
                    Title = request.Title!.Trim(),
                    PublicText = request.PublicText!.Trim(),
                    InternalNote = request.InternalNote?.Trim() ?? "",
                    StartedAt = request.StartedAt,
                    EndedAt = request.EndedAt,
                    Firms = FirmsOf(request),
                    CreatedBy = incident.CreatedBy ?? staff,
                }, IncidentResult.Done, null),
            published: false,
            cancellationToken);
    }

    /// <summary>
    /// Shows the incident on the firms' status pages, in their terminals while it goes on, and emails their administrators.
    /// One that has ended is published as resolved.
    /// </summary>
    public Task<(IncidentResult Result, OpsIncidentResponse? Incident)> PublishAsync(Guid id, string staff, CancellationToken cancellationToken) =>
        ChangeAsync(
            id,
            (incident, now) =>
            {
                if (incident.PublishedAt is not null)
                {
                    return (incident, IncidentResult.Conflict("The incident is already published."), null);
                }

                var status = incident.EndedAt is null ? IncidentStatus.Open : IncidentStatus.Resolved;
                return (
                    incident with { Status = status, PublishedAt = now, CreatedBy = incident.CreatedBy ?? staff },
                    IncidentResult.Done,
                    new IncidentUpdate(now, status, incident.PublicText, staff));
            },
            published: true,
            cancellationToken);

    /// <summary>
    /// Says how the incident goes on. Resolved ends it, now unless it has an end already, and takes its notice out of the
    /// terminals. Open again takes the end away.
    /// </summary>
    public Task<(IncidentResult Result, OpsIncidentResponse? Incident)> PostUpdateAsync(Guid id, IncidentUpdateRequest request, string staff, CancellationToken cancellationToken) =>
        ChangeAsync(
            id,
            (incident, now) =>
            {
                var text = request.Text?.Trim() ?? "";
                if (request.Status == IncidentStatus.Draft || text.Length is 0 or > MaxTextLength)
                {
                    return (incident, IncidentResult.Invalid($"Say how it goes on in 1 to {MaxTextLength} characters, as open or resolved."), null);
                }

                if (incident.PublishedAt is null)
                {
                    return (incident, IncidentResult.Conflict("Publish the incident first."), null);
                }

                DateTimeOffset? ended = request.Status == IncidentStatus.Resolved ? incident.EndedAt ?? now : null;
                return (incident with { Status = request.Status, EndedAt = ended }, IncidentResult.Done, new IncidentUpdate(now, request.Status, text, staff));
            },
            published: false,
            cancellationToken);

    /// <summary>Hides a draft, such as a false alarm. An outage the platform found is not found again while it goes on.</summary>
    public async Task<IncidentResult> DismissAsync(Guid id, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await IncidentStore.DismissAsync(connection, id, time.GetUtcNow(), cancellationToken)
            ? IncidentResult.Done
            : IncidentResult.NotFound("There is no such draft. A published incident is resolved instead.");
    }

    // The platform

    /// <summary>
    /// Asks the trading platform how the price feed is doing. A feed without prices for a while, though a market is open,
    /// becomes a draft incident and our staff are emailed. When prices come again, the incident gets its end.
    /// </summary>
    public async Task CheckPriceFeedAsync(CancellationToken cancellationToken)
    {
        PriceFeedStatus status;
        try
        {
            status = await partner.GetPriceFeedAsync(cancellationToken);
        }
        catch (TradingPlatformUnavailableException exception)
        {
            LogFeedUnknown(logger, exception);
            return;
        }

        // Without any price yet, as on a new platform, there is nothing to measure from.
        var now = time.GetUtcNow();
        var stopped = status.AnyMarketOpen && status.LastPriceAt is { } last && status.Now - last >= options.Value.PriceGap;
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var ongoing = await IncidentStore.FindOngoingDetectedAsync(connection, IncidentKind.PriceFeedOutage, cancellationToken);
        Incident? found = null;
        if (stopped && ongoing is null)
        {
            found = new Incident(
                Guid.CreateVersion7(now),
                IncidentKind.PriceFeedOutage,
                "Price feed outage",
                OutageText,
                "",
                status.LastPriceAt!.Value,
                null,
                IncidentStatus.Draft,
                null,
                null,
                true,
                null,
                now,
                []);
            await IncidentStore.InsertAsync(connection, found, cancellationToken);
        }
        else if (!stopped && ongoing is not null && status.LastPriceAt is { } back && back > ongoing.StartedAt)
        {
            // The latest price, at most a check after the first one after the gap.
            await IncidentStore.UpdateAsync(connection, ongoing with { EndedAt = back }, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        if (found is not null)
        {
            LogFeedStopped(logger, found.StartedAt);
            await staffNotifier.PriceFeedStoppedAsync(found, cancellationToken);
        }
    }

    // A firm

    /// <summary>The published incidents of the last 90 days that concern the firm, newest first, with how many of its accounts it decided on.</summary>
    public async Task<FirmIncidentsResponse> ListForFirmAsync(Firm firm, CancellationToken cancellationToken)
    {
        var incidents = (await store.ListAsync(time.GetUtcNow() - ShownFor, publishedOnly: true, cancellationToken)).Where(i => i.Concerns(firm.Id)).ToList();
        var decisions = await DecisionCountsAsync(firm, cancellationToken);
        return new FirmIncidentsResponse(
            [.. incidents.Select(i => new FirmIncidentSummary(i.Id, i.Kind, i.Title, i.StartedAt, i.EndedAt, i.Status, decisions.GetValueOrDefault(i.Id)))],
            incidents.Count(i => i.Status == IncidentStatus.Open));
    }

    /// <summary>A published incident that concerns the firm, with its accounts the incident reached and what the firm did for them.</summary>
    public async Task<FirmIncidentResponse?> DetailForFirmAsync(Firm firm, Guid id, CancellationToken cancellationToken)
    {
        if (await PublishedForAsync(firm, id, cancellationToken) is not { } incident)
        {
            return null;
        }

        var note = await store.NoteAsync(id, firm.Id, cancellationToken);
        var decisions = await store.DecisionsAsync(id, firm.Id, cancellationToken);
        var (known, accounts) = await AccountsOfAsync(firm, incident, cancellationToken);
        var numbers = accounts.GroupBy(a => a.AccountId).ToDictionary(g => g.Key, g => g.First().Number);
        foreach (var accountId in decisions.Select(d => d.AccountId).Where(a => !numbers.ContainsKey(a)).Distinct().ToList())
        {
            numbers[accountId] = (await queries.GetAsync(firm.Id, accountId, cancellationToken))?.Account.Number ?? 0;
        }

        return new FirmIncidentResponse(
            incident.Id,
            incident.Kind,
            incident.Title,
            incident.PublicText,
            incident.StartedAt,
            incident.EndedAt,
            incident.Status,
            [.. incident.Updates.Select(u => new IncidentUpdateResponse(u.At, u.Status, u.Text, null))],
            note?.Text,
            known,
            accounts,
            [.. decisions.Select(d => new IncidentDecisionResponse(d.Id, d.AccountId, numbers[d.AccountId], d.Kind, d.Amount, d.Reason, d.DecidedBy, d.DecidedAt))]);
    }

    /// <summary>Sets the firm's own words to its traders about the incident, on its status page and in its terminals while it goes on. Empty removes them.</summary>
    public async Task<IncidentResult> SetNoteAsync(Firm firm, Guid id, string? text, string admin, CancellationToken cancellationToken)
    {
        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length > MaxTextLength)
        {
            return IncidentResult.Invalid($"The note may have at most {MaxTextLength} characters.");
        }

        if (await PublishedForAsync(firm, id, cancellationToken) is null)
        {
            return NoSuchIncident;
        }

        var now = time.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await IncidentStore.SetNoteAsync(connection, id, firm.Id, trimmed.Length == 0 ? null : new IncidentNote(trimmed, now, admin), cancellationToken);
        var notified = await QueueNoticesAsync(connection, [firm], now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Wake(notified);
        return IncidentResult.Done;
    }

    /// <summary>
    /// Opens a stage again that a loss limit broken during the incident ended, on the same trading account with the
    /// balance, and records why. The balance is at most the account's balance or equity when the incident began, or its
    /// starting balance. The trader gets an email unless the firm turned it off.
    /// </summary>
    public async Task<IncidentResult> ReinstateAsync(Firm firm, Guid id, ReinstateRequest request, string admin, CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim() ?? "";
        if (request.Balance <= 0m || request.Balance != decimal.Round(request.Balance, 2))
        {
            return IncidentResult.Invalid("The balance must be above zero, with at most 2 decimals.");
        }

        if (reason.Length is 0 or > MaxReasonLength)
        {
            return IncidentResult.Invalid($"Give the reason in 1 to {MaxReasonLength} characters.");
        }

        if (await PublishedForAsync(firm, id, cancellationToken) is not { } incident)
        {
            return NoSuchIncident;
        }

        var (reached, problem) = await ReachedAsync(firm, incident, request.AccountId, a => a.CanReinstate, "Only a stage that a loss limit broken during the incident ended can be reinstated.", cancellationToken);
        if (reached is null)
        {
            return problem!;
        }

        var initial = (await queries.GetAsync(firm.Id, request.AccountId, cancellationToken))!.Account.State.Definition.InitialBalance;
        var most = Math.Max(Math.Max(reached.BalanceAtStart, reached.EquityAtStart ?? 0m), initial);
        if (request.Balance > most)
        {
            return IncidentResult.Invalid($"The balance may be at most {most:0.00}, the account's balance or equity when the incident began, or its starting balance.");
        }

        var now = time.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var step = await challenges.ApplyAsync(
            connection,
            firm,
            request.AccountId,
            s => new ReinstateStage(now, TradingDays.DayOf(now, s.Definition.TradingDay), request.Balance, request.KeepTradingDays),
            null,
            cancellationToken);
        if (step?.Outputs.OfType<InputIgnored>().FirstOrDefault() is { } ignored)
        {
            return IncidentResult.Conflict(ignored.Reason);
        }

        await IncidentStore.AddDecisionAsync(
            connection,
            id,
            firm.Id,
            new IncidentDecision(Guid.CreateVersion7(now), request.AccountId, IncidentDecisionKind.Reinstated, request.Balance, reason, admin, now),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        challenges.Notify(firm);
        return IncidentResult.Done;
    }

    /// <summary>Puts the amount on an account that the incident reached and still trades, once, and records why. At most the starting balance.</summary>
    public async Task<IncidentResult> CreditAsync(Firm firm, Guid id, CreditRequest request, string admin, CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim() ?? "";
        if (request.Amount <= 0m || request.Amount != decimal.Round(request.Amount, 2))
        {
            return IncidentResult.Invalid("The amount must be above zero, with at most 2 decimals.");
        }

        if (reason.Length is 0 or > MaxReasonLength)
        {
            return IncidentResult.Invalid($"Give the reason in 1 to {MaxReasonLength} characters.");
        }

        if (await PublishedForAsync(firm, id, cancellationToken) is not { } incident)
        {
            return NoSuchIncident;
        }

        var (reached, problem) = await ReachedAsync(firm, incident, request.AccountId, a => a.CanCredit, "Only an account that still trades on the stage the incident reached can be credited.", cancellationToken);
        if (reached is null)
        {
            return problem!;
        }

        var initial = (await queries.GetAsync(firm.Id, request.AccountId, cancellationToken))!.Account.State.Definition.InitialBalance;
        if (request.Amount > initial)
        {
            return IncidentResult.Invalid($"The amount may be at most {initial:0.00}, the account's starting balance.");
        }

        var now = time.GetUtcNow();
        var decision = new IncidentDecision(Guid.CreateVersion7(now), request.AccountId, IncidentDecisionKind.Credited, request.Amount, reason, admin, now);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ChallengeService.QueueAccountCommandAsync(
            connection,
            firm,
            request.AccountId,
            new DepositToTradingAccount(reached.TradingAccountId, $"incident-credit-{decision.Id:N}", request.Amount),
            now,
            cancellationToken);
        await IncidentStore.AddDecisionAsync(connection, id, firm.Id, decision, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        signals.CommandsOf(firm.Id).Set();
        return IncidentResult.Done;
    }

    /// <summary>The firm's status page: what runs now, and the published incidents of the last 90 days with the firm's own notes.</summary>
    public async Task<StatusPageResponse> StatusAsync(Firm firm, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var incidents = (await store.ListAsync(now - ShownFor, publishedOnly: true, cancellationToken)).Where(i => i.Concerns(firm.Id)).ToList();
        var notes = await store.NotesAsync(firm.Id, [.. incidents.Select(i => i.Id)], cancellationToken);
        var down = incidents.Where(i => i.Status == IncidentStatus.Open).SelectMany(i => i.Parts).ToHashSet();
        return new StatusPageResponse(
            firm.Name,
            now,
            down.Count == 0,
            [.. Enum.GetValues<ServicePart>().Select(p => new StatusPartResponse(p, !down.Contains(p)))],
            [
                .. incidents.Select(i => new StatusIncidentResponse(
                    i.Id,
                    i.Title,
                    i.StartedAt,
                    i.EndedAt,
                    i.Status,
                    i.Parts,
                    [.. i.Updates.Select(u => new IncidentUpdateResponse(u.At, u.Status, u.Text, null))],
                    notes.GetValueOrDefault(i.Id)?.Text)),
            ]);
    }

    // Changes an incident with it locked and adds what was said. The terminals of the firms it concerned or concerns now
    // get their notice again, and on publishing their administrators are emailed.
    private async Task<(IncidentResult Result, OpsIncidentResponse? Incident)> ChangeAsync(
        Guid id,
        Func<Incident, DateTimeOffset, (Incident Changed, IncidentResult Result, IncidentUpdate? Update)> change,
        bool published,
        CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        var now = time.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await IncidentStore.FindAsync(connection, id, forUpdate: true, cancellationToken) is not { } incident)
        {
            return (NoSuchIncident, null);
        }

        var (changed, result, update) = change(incident, now);
        if (!result.Succeeded)
        {
            return (result, null);
        }

        await IncidentStore.UpdateAsync(connection, changed, now, cancellationToken);
        if (update is not null)
        {
            await IncidentStore.AddUpdateAsync(connection, id, update, cancellationToken);
            changed = changed with { Updates = [.. incident.Updates, update] };
        }

        static bool Shown(Incident i, Firm f) => i.PublishedAt is not null && i.Concerns(f.Id);
        var affected = firms.All.Where(f => f.Trading is not null && (Shown(incident, f) || Shown(changed, f))).ToList();
        var notified = await QueueNoticesAsync(connection, affected, now, cancellationToken);
        if (published)
        {
            foreach (var firm in affected)
            {
                await EmailAdminsAsync(connection, firm, changed, now, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        Wake(notified);
        if (published)
        {
            signals.Emails.Set();
        }

        return (IncidentResult.Done, ToOps(changed));
    }

    // Queues each firm's terminal notice as the open incidents that concern it now say: the latest started, with the
    // firm's own note, or none. Firms without a server on the trading platform have no terminals.
    private static async Task<IReadOnlyList<Firm>> QueueNoticesAsync(NpgsqlConnection connection, IReadOnlyList<Firm> firms, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var open = await IncidentStore.ListOpenAsync(connection, cancellationToken);
        var notified = new List<Firm>();
        foreach (var firm in firms.Where(f => f.Trading is not null))
        {
            TradingNotice? notice = null;
            if (open.FirstOrDefault(i => i.Concerns(firm.Id)) is { } current)
            {
                var notes = await IncidentStore.NotesAsync(connection, firm.Id, [current.Id], cancellationToken);
                notice = NoticeOf(firm, current, notes.GetValueOrDefault(current.Id));
            }

            await ChallengeService.QueueFirmCommandAsync(connection, firm, new SetTradingNotice(notice), now, cancellationToken);
            notified.Add(firm);
        }

        return notified;
    }

    private void Wake(IEnumerable<Firm> notified)
    {
        foreach (var firm in notified)
        {
            signals.CommandsOf(firm.Id).Set();
        }
    }

    private async Task EmailAdminsAsync(NpgsqlConnection connection, Firm firm, Incident incident, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!incident.Concerns(firm.Id))
        {
            return;
        }

        var admins = new List<string>();
        await using (var command = new NpgsqlCommand("select email from firm_admins where firm_id = $1 order by created_at", connection))
        {
            command.Parameters.AddWithValue(firm.Id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                admins.Add(reader.GetString(0));
            }
        }

        var url = new Uri(firm.Portal.Url, $"admin/incidents/{incident.Id}");
        foreach (var admin in admins)
        {
            await EmailOutbox.AddAsync(
                connection,
                PlatformEmails.IncidentPublished(platform.Value.Name, firm.Name, admin, incident.Title, incident.PublicText, incident.Status == IncidentStatus.Open, url),
                "incident",
                firm.Id,
                now,
                cancellationToken,
                $"incident-{incident.Id:N}-{Emails.Normalize(admin)}");
        }
    }

    // What the firm's terminals show while the incident goes on: what we said last, then the firm's own words.
    private static TradingNotice NoticeOf(Firm firm, Incident incident, string? note)
    {
        var said = incident.Updates.Count > 0 ? incident.Updates[^1].Text : incident.PublicText;
        var text = note is null ? said : $"{said}\n\n{firm.Name}: {note}";
        return new TradingNotice(incident.Title, text.Length > MaxNoticeLength ? text[..(MaxNoticeLength - 3)] + "..." : text, true, new Uri(firm.Portal.Url, "status"));
    }

    private async Task<Incident?> PublishedForAsync(Firm firm, Guid id, CancellationToken cancellationToken) =>
        await store.FindAsync(id, cancellationToken) is { PublishedAt: not null } incident && incident.Concerns(firm.Id) ? incident : null;

    // The account's row among those the incident reached, when the firm may act on it, or the problem.
    private async Task<(IncidentAccountResponse? Account, IncidentResult? Problem)> ReachedAsync(
        Firm firm,
        Incident incident,
        Guid accountId,
        Func<IncidentAccountResponse, bool> may,
        string refused,
        CancellationToken cancellationToken)
    {
        var (known, accounts) = await AccountsOfAsync(firm, incident, cancellationToken);
        if (!known)
        {
            return (null, IncidentResult.Conflict("Which accounts the incident reached is not known now, so nothing can be done for them. Try again shortly."));
        }

        var rows = accounts.Where(a => a.AccountId == accountId).ToList();
        return rows.Count == 0
            ? (null, IncidentResult.NotFound("The incident did not reach that account."))
            : rows.FirstOrDefault(may) is { } row ? (row, null) : (null, IncidentResult.Conflict(refused));
    }

    // The firm's accounts the incident reached, as the trading platform tells, with what the firm can do for each.
    private async Task<(bool Known, IReadOnlyList<IncidentAccountResponse> Accounts)> AccountsOfAsync(Firm firm, Incident incident, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (firm.Trading is null || incident.StartedAt < now - MaxImpactAge)
        {
            return (false, []);
        }

        var to = incident.EndedAt ?? now;
        TradingImpact impact;
        try
        {
            impact = await trading.GetImpactAsync(firm.Trading, incident.StartedAt, to - incident.StartedAt > MaxImpactLength ? incident.StartedAt + MaxImpactLength : to, cancellationToken);
        }
        catch (Exception exception) when (exception is TradingPlatformUnavailableException or TradingPlatformRejectedException)
        {
            LogImpactUnknown(logger, exception, firm.Id, incident.Id);
            return (false, []);
        }

        var challengeOf = await ChallengeAccountsOfAsync(firm, [.. impact.Accounts.Select(a => a.AccountId)], cancellationToken);
        var accounts = new List<IncidentAccountResponse>();
        foreach (var account in impact.Accounts)
        {
            if (!challengeOf.TryGetValue(account.AccountId, out var challengeId) || await queries.GetAsync(firm.Id, challengeId, cancellationToken) is not { } view)
            {
                // An account the firm opened on the trading platform itself.
                continue;
            }

            var state = view.Account.State;
            var current = state.AccountId == account.AccountId;
            accounts.Add(new IncidentAccountResponse(
                view.Account.Id,
                view.Account.Number,
                account.AccountId,
                state.Definition.Name,
                state.Rules.Name,
                view.Account.Email,
                state.Definition.Currency,
                state.Status.ToString(),
                account.OpenPositions,
                account.BalanceAtStart,
                account.EquityAtStart,
                account.OrdersRefused,
                account.ClosesRefused,
                account.ChangesRefused,
                account.Breach is { } breach ? new IncidentBreachResponse(breach.At, breach.FloorId, breach.Level, breach.Equity) : null,
                account.Balance,
                account.Equity,
                state.TradingDays.Count,
                current && account.Breach is not null && state.Status == ChallengeStatus.Failed && view.Ending is ChallengeFailed,
                current && state.Status == ChallengeStatus.Active));
        }

        return (true, [.. accounts.OrderBy(a => a.Breach is null).ThenBy(a => a.Number).ThenBy(a => a.TradingAccountId, StringComparer.Ordinal)]);
    }

    private async Task<Dictionary<string, Guid>> ChallengeAccountsOfAsync(Firm firm, string[] tradingAccountIds, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            select t.account_id, t.challenge_account_id from trading_accounts t
            join challenge_accounts a on a.id = t.challenge_account_id
            where a.firm_id = $1 and t.account_id = any($2)
            """);
        command.Parameters.AddWithValue(firm.Id);
        command.Parameters.AddWithValue(tradingAccountIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var accounts = new Dictionary<string, Guid>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            accounts[reader.GetString(0)] = reader.GetGuid(1);
        }

        return accounts;
    }

    private async Task<Dictionary<Guid, int>> DecisionCountsAsync(Firm firm, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select incident_id, count(*)::int from incident_decisions where firm_id = $1 group by incident_id");
        command.Parameters.AddWithValue(firm.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var counts = new Dictionary<Guid, int>();
        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetGuid(0)] = reader.GetInt32(1);
        }

        return counts;
    }

    // The firms with traders in open positions, which an outage would reach, the most first.
    private async Task<IReadOnlyList<FirmExposure>> ExposureAsync(CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            select a.firm_id, count(*)::int from trading_accounts t
            join challenge_accounts a on a.id = t.challenge_account_id
            where t.open_positions > 0 and not t.disabled
            group by a.firm_id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var exposure = new List<FirmExposure>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var firmId = reader.GetString(0);
            exposure.Add(new FirmExposure(firmId, firms.ById(firmId)?.Name ?? firmId, reader.GetInt32(1)));
        }

        return [.. exposure.OrderByDescending(e => e.AccountsWithPositions).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private string? Problem(IncidentRequest request, DateTimeOffset now) =>
        string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > MaxTitleLength ? $"The title must have 1 to {MaxTitleLength} characters."
        : string.IsNullOrWhiteSpace(request.PublicText) || request.PublicText.Trim().Length > MaxTextLength ? $"What traders read must have 1 to {MaxTextLength} characters."
        : request.InternalNote?.Trim().Length > MaxTextLength ? $"The note for staff may have at most {MaxTextLength} characters."
        : request.StartedAt > now + TimeSpan.FromMinutes(1) ? "The incident cannot start in the future."
        : request.EndedAt is { } ended && ended < request.StartedAt ? "The incident cannot end before it started."
        : request.Firms is { } chosen && (chosen.Count == 0 || chosen.Any(f => firms.ById(f) is null)) ? "Choose firms that exist, or every firm."
        : null;

    private static IReadOnlyList<string>? FirmsOf(IncidentRequest request) =>
        request.Firms is null ? null : [.. request.Firms.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static OpsIncidentResponse ToOps(Incident i) =>
        new(
            i.Id,
            i.Kind,
            i.Title,
            i.PublicText,
            i.InternalNote,
            i.StartedAt,
            i.EndedAt,
            i.Status,
            i.PublishedAt,
            i.Firms,
            i.Detected,
            i.CreatedBy,
            [.. i.Updates.Select(u => new IncidentUpdateResponse(u.At, u.Status, u.Text, u.By))]);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No prices since {Since}: a draft incident was made for our staff")]
    private static partial void LogFeedStopped(ILogger logger, DateTimeOffset since);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The trading platform could not say how the price feed is doing")]
    private static partial void LogFeedUnknown(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The trading platform could not say what incident {IncidentId} did to the accounts of firm {FirmId}")]
    private static partial void LogImpactUnknown(ILogger logger, Exception exception, string firmId, Guid incidentId);
}
