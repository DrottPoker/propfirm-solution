using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Prop.Api.Api;
using Prop.Api.Billing;
using Prop.Api.Configuration;
using Prop.Api.Firms;
using Prop.Api.Incidents;
using Prop.Api.Portal;
using Prop.Api.Review;

namespace Prop.Api.Ops;

/// <summary>
/// Our own admin view across the firms (ADR 0024): the overview with what waits for us, the list of firms with search
/// and groups, and what firms pay us. Everything is worked out when it is read.
/// </summary>
internal static class OpsPanelEndpoints
{
    /// <summary>The window of what firms paid us and of what happened lately.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(30);

    /// <summary>The window of the firms that signed up, for how far they got, and of our decisions.</summary>
    public static readonly TimeSpan FunnelWindow = TimeSpan.FromDays(90);

    /// <summary>A payout traders have waited for this long since they asked is late.</summary>
    public static readonly TimeSpan LatePayoutAge = TimeSpan.FromDays(7);

    /// <summary>A trading server that takes this long to create needs us.</summary>
    public static readonly TimeSpan SettingUpTooLong = TimeSpan.FromMinutes(10);

    public const int OverviewWeeks = 12;

    public const int OverviewActivity = 12;

    public const int MaxFirmsPerRequest = 500;

    public const int PaidChargesShown = 100;

    // Every firm, for what the firms pay together. The platform has far fewer.
    private const int AllFirms = 100_000;

    public static RouteGroupBuilder MapOpsPanel(this RouteGroupBuilder staff)
    {
        staff.MapGet("/overview", GetOverviewAsync);
        staff.MapGet("/waiting", GetWaitingAsync);
        staff.MapGet("/firms", ListFirmsAsync);
        staff.MapGet("/billing", GetBillingAsync);
        return staff;
    }

    /// <summary>
    /// What waits for us across the firms and how the platform is doing: the firms in each group, what firms pay each
    /// month and paid lately, their challenges, week by week, from sign-up to live, and what happened lately.
    /// </summary>
    private static async Task<Ok<OpsOverviewResponse>> GetOverviewAsync(
        OpsFigures figures,
        BillingService billing,
        BillingStore store,
        FirmCatalog catalog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var terms = billing.Terms;
        var firms = await figures.ListFirmsAsync(OpsFirmGroup.All, null, now, AllFirms, cancellationToken);
        var counts = await figures.CountFirmsAsync(null, cancellationToken);
        var challenges = await figures.LiveChallengesAsync(cancellationToken);
        var weeks = await figures.WeeksAsync(now, OverviewWeeks, cancellationToken);
        var funnel = await figures.FunnelAsync(now - FunnelWindow, cancellationToken);
        var activity = await figures.ActivityAsync(now - RecentWindow, OverviewActivity, cancellationToken);
        return TypedResults.Ok(new OpsOverviewResponse(
            terms.Currency,
            await NeedsUsAsync(figures, store, catalog, firms, now, cancellationToken),
            OpsFirmCountsResponse.From(counts),
            Monthly(firms, terms),
            OpsPaidResponse.From(await figures.PaidSinceAsync(now - RecentWindow, cancellationToken)),
            new OpsChallengesResponse(challenges.Open, challenges.Paused),
            [.. weeks.Select(w => new OpsWeekResponse(w.Start, MoneyTotalResponse.From(w.Months), MoneyTotalResponse.From(w.GoingLive)))],
            new OpsFunnelResponse(
                funnel.SignedUp,
                funnel.Sent,
                funnel.Approved,
                funnel.Live,
                funnel.ApprovedNotLive,
                funnel.AverageTimeToDecision is { } average ? Math.Round(average.TotalHours, 1) : null),
            [.. activity.Select(OpsActivityResponse.From)]));
    }

    /// <summary>How many applications wait for us, how many charges were declined and how many incident drafts wait, for the menu.</summary>
    private static async Task<Ok<OpsWaitingResponse>> GetWaitingAsync(OpsFigures figures, BillingStore store, IncidentService incidents, CancellationToken cancellationToken)
    {
        var toReview = await figures.ToReviewAsync(cancellationToken);
        await using var connection = await store.OpenAsync(cancellationToken);
        var declined = await BillingStore.DeclinedChargesAsync(connection, cancellationToken);
        return TypedResults.Ok(new OpsWaitingResponse(toReview.Count, declined.Count, await incidents.DraftsAsync(cancellationToken)));
    }

    /// <summary>
    /// The firms in the group that the search finds, by name, short name or an administrator's email, with how many it
    /// finds in every group.
    /// </summary>
    private static async Task<Results<Ok<OpsFirmsResponse>, ProblemHttpResult>> ListFirmsAsync(
        OpsFigures figures,
        BillingService billing,
        IOptions<SandboxOptions> sandbox,
        TimeProvider time,
        CancellationToken cancellationToken,
        OpsFirmGroup group = OpsFirmGroup.All,
        string? search = null,
        int limit = 200)
    {
        if (limit is < 1 or > MaxFirmsPerRequest)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, $"limit must be 1 to {MaxFirmsPerRequest}.");
        }

        var terms = billing.Terms;
        var firms = await figures.ListFirmsAsync(group, search, time.GetUtcNow(), limit, cancellationToken);
        var counts = await figures.CountFirmsAsync(search, cancellationToken);
        return TypedResults.Ok(new OpsFirmsResponse(
            [.. firms.Select(f => OpsFirmListItemResponse.From(f, SlotsOf(f, sandbox.Value), MonthlyPriceOf(f, terms)))],
            OpsFirmCountsResponse.From(counts),
            terms.Currency));
    }

    /// <summary>
    /// What firms pay us: each month together, the next month firm by firm, what they paid in the last 30 days, the charges
    /// whose card was declined, the newest paid charges and the prices.
    /// </summary>
    private static async Task<Ok<OpsBillingOverviewResponse>> GetBillingAsync(
        OpsFigures figures,
        BillingService billing,
        BillingStore store,
        FirmCatalog catalog,
        IOptions<BillingOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var terms = billing.Terms;
        var firms = await figures.ListFirmsAsync(OpsFirmGroup.All, null, now, AllFirms, cancellationToken);
        var byId = firms.ToDictionary(f => f.Id, StringComparer.Ordinal);
        List<Charge> declined;
        List<Charge> paid;
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            declined = await BillingStore.DeclinedChargesAsync(connection, cancellationToken);
            paid = await BillingStore.PaidChargesAsync(connection, PaidChargesShown, cancellationToken);
        }

        var month = BillingRules.MonthOf(now).AddMonths(1);
        if (now >= BillingRules.ChargeTimeOf(month, terms.ChargeDaysBeforeMonth))
        {
            month = month.AddMonths(1);
        }

        var nextMonth = firms
            .Where(f => f.Paying)
            .Select(f => new OpsNextMonthFirmResponse(f.Id, f.Name, ChargedSlots(f, terms), MonthlyPriceOf(f, terms)!.Value, f.Card, f.SuspendedAt is not null, f.Unpaid))
            .OrderByDescending(f => f.Amount)
            .ThenBy(f => f.FirmName, StringComparer.Ordinal)
            .ToList();
        return TypedResults.Ok(new OpsBillingOverviewResponse(
            terms.Currency,
            Monthly(firms, terms),
            new OpsNextMonthResponse(month, BillingRules.ChargeTimeOf(month, terms.ChargeDaysBeforeMonth), nextMonth.Sum(f => f.Amount), nextMonth),
            OpsPaidResponse.From(await figures.PaidSinceAsync(now - RecentWindow, cancellationToken)),
            [.. declined.Select(c => OpsChargeResponse.From(c, catalog, byId))],
            [.. paid.Select(c => OpsChargeResponse.From(c, catalog, byId))],
            BillingEndpoints.PricesOf(terms, options.Value)));
    }

    /// <summary>The applications waiting, the declined charges, the late payouts and the trading servers that take too long.</summary>
    private static async Task<OpsNeedsUsResponse> NeedsUsAsync(
        OpsFigures figures,
        BillingStore store,
        FirmCatalog catalog,
        IReadOnlyList<OpsFirmRecord> firms,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var byId = firms.ToDictionary(f => f.Id, StringComparer.Ordinal);
        List<Charge> declined;
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            declined = await BillingStore.DeclinedChargesAsync(connection, cancellationToken);
        }

        return new OpsNeedsUsResponse(
            [.. (await figures.ToReviewAsync(cancellationToken)).Select(OpsWaitingFirmResponse.From)],
            [.. declined.Select(c => OpsChargeResponse.From(c, catalog, byId))],
            [
                .. (await figures.LatePayoutsAsync(now - LatePayoutAge, cancellationToken)).Select(l => new OpsLatePayoutsResponse(
                    l.FirmId, l.FirmName, l.Count, l.Approved, MoneyTotalResponse.From(l.Totals), l.OldestRequestedAt)),
            ],
            [.. (await figures.SettingUpAsync(now - SettingUpTooLong, cancellationToken)).Select(OpsWaitingFirmResponse.From)],
            (int)LatePayoutAge.TotalDays);
    }

    /// <summary>What the firms that pay by card pay together each month, and for how many slots.</summary>
    private static OpsMonthlyResponse Monthly(IReadOnlyList<OpsFirmRecord> firms, BillingTerms terms)
    {
        var paying = firms.Where(f => f.Paying).ToList();
        return new OpsMonthlyResponse(paying.Sum(f => MonthlyPriceOf(f, terms)!.Value), paying.Count, paying.Sum(f => ChargedSlots(f, terms)));
    }

    // The slots a paying firm's next month is charged for, as its own billing works them out.
    private static int ChargedSlots(OpsFirmRecord firm, BillingTerms terms) => terms.SlotsToCharge(firm.PlanSlots, firm.OpenAccounts + firm.Reserved);

    /// <summary>What a firm that pays by card pays each month. Null for the others.</summary>
    private static decimal? MonthlyPriceOf(OpsFirmRecord firm, BillingTerms terms) =>
        firm.Paying ? BillingRules.MonthlyPrice(ChargedSlots(firm, terms), terms) : null;

    /// <summary>How many challenges the firm may have open now, as its slots work it out. Null for no limit.</summary>
    private static int? SlotsOf(OpsFirmRecord firm, SandboxOptions sandbox) =>
        firm.Status != FirmStatus.Live ? sandbox.MaxOpenAccounts
        : firm.Plan is null || firm.Plan == BillingPlan.Complimentary ? firm.PlanSlots
        : firm.PaidSlots ?? firm.PlanSlots ?? 0;
}

/// <summary>How many firms are in each group. The live ones that have not paid are among the live ones too.</summary>
public sealed record OpsFirmCountsResponse(int All, int ToReview, int Sandbox, int Live, int Unpaid, int Suspended, int Rejected)
{
    internal static OpsFirmCountsResponse From(OpsFirmCounts counts) =>
        new(counts.All, counts.ToReview, counts.Sandbox, counts.Live, counts.Unpaid, counts.Suspended, counts.Rejected);
}

/// <summary>Where a firm is with us, in one word for the list.</summary>
public enum OpsFirmStage
{
    /// <summary>Its trading server is being created.</summary>
    SettingUp,

    /// <summary>Trying the platform, before sending an application.</summary>
    Sandbox,

    /// <summary>Its application waits for us.</summary>
    ToReview,

    /// <summary>We asked for changes to its application.</summary>
    ChangesRequested,

    /// <summary>Approved, and not live yet.</summary>
    Approved,

    /// <summary>Not approved.</summary>
    Rejected,

    Live,

    /// <summary>Live, with this month unpaid or a charge whose card was declined.</summary>
    Unpaid,

    Suspended,
}

/// <summary>
/// A firm in our list: where it is with us, when it signed up, sent its application, was decided on, went live, was
/// suspended or began unpaid, its challenges that have not ended and those paused, how many it may have open, or null
/// for no limit, and what it pays each month when it pays by card.
/// </summary>
public sealed record OpsFirmListItemResponse(
    string Id,
    string Name,
    OpsFirmStage Stage,
    FirmStatus Status,
    bool Configured,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? SuspendedAt,
    DateTimeOffset? UnpaidSince,
    int OpenChallenges,
    int PausedChallenges,
    int? Slots,
    decimal? MonthlyPrice)
{
    internal static OpsFirmListItemResponse From(OpsFirmRecord firm, int? slots, decimal? monthlyPrice) =>
        new(
            firm.Id,
            firm.Name,
            StageOf(firm),
            firm.Status,
            firm.Configured,
            firm.CreatedAt,
            firm.SubmittedAt,
            firm.DecidedAt,
            firm.ActivatedAt,
            firm.SuspendedAt,
            firm.UnpaidSince,
            firm.OpenAccounts,
            firm.PausedAccounts,
            slots,
            monthlyPrice);

    internal static OpsFirmStage StageOf(OpsFirmRecord firm) =>
        firm.SuspendedAt is not null ? OpsFirmStage.Suspended
        : firm.Status == FirmStatus.Live ? firm.Unpaid ? OpsFirmStage.Unpaid : OpsFirmStage.Live
        : firm.Review == ReviewStatus.Rejected ? OpsFirmStage.Rejected
        : firm.Status == FirmStatus.Provisioning ? OpsFirmStage.SettingUp
        : firm.Review switch
        {
            ReviewStatus.Submitted => OpsFirmStage.ToReview,
            ReviewStatus.ChangesRequested => OpsFirmStage.ChangesRequested,
            ReviewStatus.Approved => OpsFirmStage.Approved,
            _ => OpsFirmStage.Sandbox,
        };
}

/// <summary>The firms the search finds, the counts in every group, and the currency firms pay us in.</summary>
public sealed record OpsFirmsResponse(IReadOnlyList<OpsFirmListItemResponse> Firms, OpsFirmCountsResponse Counts, string Currency);

/// <summary>How many applications wait for us, how many charges were declined and are not paid, and how many incident drafts wait.</summary>
public sealed record OpsWaitingResponse(int ToReview, int Unpaid, int IncidentDrafts);

/// <summary>A firm and since when something has waited: its application, or its trading server.</summary>
public sealed record OpsWaitingFirmResponse(string Id, string Name, DateTimeOffset Since)
{
    internal static OpsWaitingFirmResponse From(WaitingFirm firm) => new(firm.Id, firm.Name, firm.Since);
}

/// <summary>
/// A charge with its firm. For one that is not paid: when the card was last declined, since when the firm's month is
/// unpaid and how many of its challenges are paused.
/// </summary>
public sealed record OpsChargeResponse(string FirmId, string FirmName, ChargeResponse Charge, DateTimeOffset? FailedAt, DateTimeOffset? UnpaidSince, int PausedChallenges)
{
    internal static OpsChargeResponse From(Charge charge, FirmCatalog catalog, IReadOnlyDictionary<string, OpsFirmRecord> firms)
    {
        var firm = firms.GetValueOrDefault(charge.FirmId);
        return new OpsChargeResponse(
            charge.FirmId,
            firm?.Name ?? catalog.ById(charge.FirmId)?.Name ?? charge.FirmId,
            ChargeResponse.From(charge),
            charge.FailedAt,
            firm?.UnpaidSince,
            firm?.PausedAccounts ?? 0);
    }
}

/// <summary>
/// A firm's payouts that traders asked for more than the late age ago and are neither paid nor rejected: how many, how
/// many of them the firm approved, how much per currency, and when the oldest was asked for.
/// </summary>
public sealed record OpsLatePayoutsResponse(string FirmId, string FirmName, int Count, int Approved, IReadOnlyList<MoneyTotalResponse> Totals, DateTimeOffset OldestRequestedAt);

/// <summary>
/// What waits for us: applications to review, the oldest first, charges whose card was declined, firms whose traders
/// have waited more than <paramref name="LateAfterDays"/> days for payouts, and trading servers that take too long.
/// </summary>
public sealed record OpsNeedsUsResponse(
    IReadOnlyList<OpsWaitingFirmResponse> ToReview,
    IReadOnlyList<OpsChargeResponse> Unpaid,
    IReadOnlyList<OpsLatePayoutsResponse> LatePayouts,
    IReadOnlyList<OpsWaitingFirmResponse> SettingUp,
    int LateAfterDays);

/// <summary>What the firms that pay by card pay each month together, before VAT, how many they are and for how many slots.</summary>
public sealed record OpsMonthlyResponse(decimal Amount, int Firms, int Slots);

/// <summary>Charges paid in the last 30 days: months, more slots, going live and deposits, and how much per currency.</summary>
public sealed record OpsPaidResponse(int Months, int Slots, int GoingLive, int Deposits, IReadOnlyList<MoneyTotalResponse> Totals)
{
    internal static OpsPaidResponse From(PaidCharges paid) =>
        new(paid.Months, paid.Slots, paid.GoingLive, paid.Deposits, MoneyTotalResponse.From(paid.Totals));
}

/// <summary>The challenges at live firms that have not ended, and how many of them are paused.</summary>
public sealed record OpsChallengesResponse(int Open, int Paused);

/// <summary>The week from Monday <paramref name="Start"/> in UTC: what firms paid for months and slots, and for going live and deposits.</summary>
public sealed record OpsWeekResponse(DateOnly Start, IReadOnlyList<MoneyTotalResponse> Months, IReadOnlyList<MoneyTotalResponse> GoingLive);

/// <summary>
/// The firms that signed up in the last 90 days and how far they got, the approved firms of any age that are not live
/// yet, and how many hours our decisions in the last 90 days took on average from the application they answered.
/// </summary>
public sealed record OpsFunnelResponse(int SignedUp, int Sent, int Approved, int Live, int ApprovedNotLive, double? AverageHoursToDecision);

/// <summary>What happened to a firm.</summary>
public enum OpsActivityKind
{
    SignedUp,
    ApplicationSent,
    Approved,
    ChangesRequested,
    Rejected,
    Suspended,
    SuspensionLifted,

    /// <summary>The firm paid its first charge and went live.</summary>
    WentLive,

    /// <summary>A month or more slots were paid.</summary>
    ChargePaid,

    /// <summary>The card was declined for a charge.</summary>
    ChargeDeclined,
}

/// <summary>
/// Something that happened to a firm. <paramref name="Actor"/> is who did it: an administrator, a staff member or the
/// platform. <paramref name="Text"/> is our message, the reason for a suspension or why a card was declined.
/// </summary>
public sealed record OpsActivityResponse(
    OpsActivityKind Kind,
    DateTimeOffset Time,
    string FirmId,
    string FirmName,
    string? Actor,
    decimal? Amount,
    string? Currency,
    string? Text,
    ChargeKind? ChargeKind,
    DateOnly? Month,
    int? Slots)
{
    internal static OpsActivityResponse From(OpsActivityRecord record) =>
        new(
            record.Kind switch
            {
                "signed_up" => OpsActivityKind.SignedUp,
                "submitted" => OpsActivityKind.ApplicationSent,
                "approved" => OpsActivityKind.Approved,
                "changes_requested" => OpsActivityKind.ChangesRequested,
                "rejected" => OpsActivityKind.Rejected,
                "suspended" => OpsActivityKind.Suspended,
                "suspension_lifted" => OpsActivityKind.SuspensionLifted,
                "paid" => record.ChargeKind == Billing.ChargeKind.Activation ? OpsActivityKind.WentLive : OpsActivityKind.ChargePaid,
                "declined" => OpsActivityKind.ChargeDeclined,
                _ => throw new InvalidOperationException($"Unknown activity {record.Kind}."),
            },
            record.Time,
            record.FirmId,
            record.FirmName,
            record.Actor,
            record.Amount,
            record.Currency,
            record.Text,
            record.ChargeKind,
            record.Month,
            record.Slots);
}

/// <summary>
/// What waits for us across the firms and how the platform is doing, in the currency firms pay us in: the firms in each
/// group, what firms pay each month and paid in the last 30 days, the challenges at live firms, what firms paid in each
/// of the last 12 weeks, oldest first, how far the firms that signed up got, and what happened lately, newest first.
/// </summary>
public sealed record OpsOverviewResponse(
    string Currency,
    OpsNeedsUsResponse NeedsUs,
    OpsFirmCountsResponse Firms,
    OpsMonthlyResponse Monthly,
    OpsPaidResponse PaidLast30Days,
    OpsChallengesResponse Challenges,
    IReadOnlyList<OpsWeekResponse> Weeks,
    OpsFunnelResponse Funnel,
    IReadOnlyList<OpsActivityResponse> Activity);

/// <summary>A paying firm's next month: the slots and the amount, its card, and whether it is suspended or has not paid.</summary>
public sealed record OpsNextMonthFirmResponse(string FirmId, string FirmName, int Slots, decimal Amount, CardResponse? Card, bool Suspended, bool Unpaid);

/// <summary>The next month that is charged, when, how much in all, and each paying firm's part, the largest first.</summary>
public sealed record OpsNextMonthResponse(DateOnly Month, DateTimeOffset ChargeAt, decimal Amount, IReadOnlyList<OpsNextMonthFirmResponse> Firms);

/// <summary>
/// What firms pay us, in the currency of our prices: each month together, the next month firm by firm, what they paid in
/// the last 30 days, the charges whose card was declined, the longest declined first, the newest paid charges, and the
/// prices.
/// </summary>
public sealed record OpsBillingOverviewResponse(
    string Currency,
    OpsMonthlyResponse Monthly,
    OpsNextMonthResponse NextMonth,
    OpsPaidResponse PaidLast30Days,
    IReadOnlyList<OpsChargeResponse> Unpaid,
    IReadOnlyList<OpsChargeResponse> Paid,
    PricesResponse Prices);
