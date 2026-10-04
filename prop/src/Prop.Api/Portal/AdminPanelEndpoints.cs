using Microsoft.AspNetCore.Http.HttpResults;

using Prop.Api.Api;
using Prop.Api.Challenges;
using Prop.Api.Email;
using Prop.Api.History;
using Prop.Api.Payments;
using Prop.Rules;

namespace Prop.Api.Portal;

/// <summary>
/// The admin panel's own views of the firm (ADR 0023): the overview, the search among accounts, an account's trader and
/// trading history, the payouts the firm works through and each challenge's figures.
/// </summary>
internal static class AdminPanelEndpoints
{
    public const int MaxAccountsPerPage = 200;

    /// <summary>The window of the overview's sales and payouts, and of the challenges started.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(30);

    /// <summary>The window of the pass rate: evaluations that ended in it.</summary>
    public static readonly TimeSpan PassRateWindow = TimeSpan.FromDays(90);

    public const int OverviewWeeks = 12;

    public const int OverviewActivity = 12;

    public static RouteGroupBuilder MapAdminPanel(this RouteGroupBuilder admin)
    {
        admin.MapGet("/overview", GetOverviewAsync);
        admin.MapGet("/accounts", SearchAccountsAsync);
        admin.MapGet("/accounts/{accountId:guid}/trader", GetTraderAsync);
        admin.MapPost("/accounts/{accountId:guid}/email-trader", EmailTraderAsync);
        admin.MapGet("/accounts/{accountId:guid}/performance", GetPerformanceAsync);
        admin.MapGet("/accounts/{accountId:guid}/trades", ListTradesAsync);
        admin.MapGet("/accounts/{accountId:guid}/trades.csv", DownloadTradesAsync);
        admin.MapGet("/payouts", ListPayoutsAsync);
        admin.MapGet("/payouts/summary", GetPayoutSummaryAsync);
        admin.MapGet("/challenges/figures", ListChallengeFiguresAsync);
        return admin;
    }

    /// <summary>How the firm is doing: its accounts, its payouts, its sales, its pass rate, week by week and what happened lately.</summary>
    private static async Task<Ok<AdminOverviewResponse>> GetOverviewAsync(
        HttpContext context,
        ChallengeQueries accounts,
        PayoutQueries payouts,
        AdminFigures figures,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var now = time.GetUtcNow();
        var counts = await accounts.CountAsync(firm.Id, new AccountSearch(null, null, AccountGroup.All), cancellationToken);
        var payoutSummary = await payouts.SummaryAsync(firm.Id, now - RecentWindow, cancellationToken);
        var sales = await figures.SalesAsync(firm.Id, now - RecentWindow, cancellationToken);
        var challenges = await figures.ChallengesAsync(firm.Id, now - RecentWindow, now - PassRateWindow, cancellationToken);
        var weeks = await figures.WeeksAsync(firm.Id, now, OverviewWeeks, cancellationToken);
        var activity = await figures.ActivityAsync(firm.Id, now - RecentWindow, OverviewActivity, cancellationToken);
        return TypedResults.Ok(new AdminOverviewResponse(
            AccountCountsResponse.From(counts),
            PayoutSummaryResponse.From(payoutSummary),
            new SalesResponse(sales.Orders, MoneyTotalResponse.From(sales.Totals)),
            new PassRateResponse(challenges.Sum(c => c.Passed), challenges.Sum(c => c.Ended)),
            [.. weeks.Select(w => new WeekResponse(w.Start, MoneyTotalResponse.From(w.Sales), MoneyTotalResponse.From(w.Payouts)))],
            [.. activity.Select(ActivityResponse.From)]));
    }

    /// <summary>
    /// A page of the firm's accounts, the newest first: those the search finds in the group, with how many it finds in every
    /// group. <paramref name="before"/> is the <c>next</c> of the previous page.
    /// </summary>
    private static async Task<Results<Ok<AdminAccountsResponse>, ProblemHttpResult>> SearchAccountsAsync(
        HttpContext context,
        ChallengeQueries queries,
        CancellationToken cancellationToken,
        string? search = null,
        AccountGroup group = AccountGroup.All,
        string? challengeId = null,
        long? before = null,
        int limit = 50)
    {
        if (limit is < 1 or > MaxAccountsPerPage)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, $"limit must be 1 to {MaxAccountsPerPage}.");
        }

        var firm = PortalFirmFilter.FirmOf(context);
        var query = new AccountSearch(search, challengeId, group);
        var views = await queries.SearchAsync(firm.Id, query, before, limit, cancellationToken);
        var counts = await queries.CountAsync(firm.Id, query, cancellationToken);
        return TypedResults.Ok(new AdminAccountsResponse(
            [.. views.Select(AccountResponse.From)],
            AccountCountsResponse.From(counts),
            views.Count == limit ? views[^1].Account.Number : null));
    }

    /// <summary>The account's trader: their accounts at the firm, what they bought and were paid out, and the order behind this account.</summary>
    private static async Task<Results<Ok<TraderSummaryResponse>, ProblemHttpResult>> GetTraderAsync(
        Guid accountId,
        HttpContext context,
        ChallengeQueries accounts,
        PayoutQueries payouts,
        AdminFigures figures,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        if (await accounts.GetAsync(firm.Id, accountId, cancellationToken) is not { } view
            || await figures.TraderAsync(firm.Id, view.Account.TraderId, cancellationToken) is not { } trader)
        {
            return AccountActions.UnknownAccount();
        }

        var traderAccounts = await accounts.ListByTraderAsync(firm.Id, trader.Id, cancellationToken);
        var paidOut = (await payouts.ListByTraderAsync(firm.Id, trader.Id, cancellationToken))
            .Where(p => p.Status == PayoutStatus.Paid)
            .GroupBy(p => p.Currency, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new MoneyTotalResponse(g.Key, g.Sum(p => p.Amount)));
        var order = await figures.OrderOfAsync(firm.Id, accountId, cancellationToken);
        return TypedResults.Ok(new TraderSummaryResponse(
            trader.Email,
            trader.CreatedAt,
            trader.HasPassword,
            [.. traderAccounts.OrderByDescending(a => a.Account.Number).Select(AccountResponse.From)],
            trader.Orders,
            MoneyTotalResponse.From(trader.Bought),
            [.. paidOut],
            order is null ? null : new OrderSummaryResponse(order.Id, order.Number, order.Amount, order.Currency, order.Provider, order.PaidAt)));
    }

    /// <summary>
    /// Emails the account's trader in the firm's name: an invitation to choose a password for the portal, or, for a
    /// trader who has one, that the challenge has started. 503 when the email cannot be sent.
    /// </summary>
    private static async Task<Results<Ok<TraderEmailResponse>, ProblemHttpResult>> EmailTraderAsync(
        Guid accountId,
        HttpContext context,
        ChallengeQueries accounts,
        PortalUsers users,
        IEmailSender email,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        if (await accounts.GetAsync(firm.Id, accountId, cancellationToken) is not { } view
            || await users.FindByIdAsync(view.Account.TraderId, PortalRoles.Trader, cancellationToken) is not { } trader)
        {
            return AccountActions.UnknownAccount();
        }

        var challengeName = view.Account.State.Definition.Name;
        var (message, kind) = trader.PasswordHash is null
            ? (PlatformEmails.InviteTrader(
                firm.Name,
                challengeName,
                trader.Email,
                new Uri(firm.Portal.Url, $"invite?token={(await users.CreateInviteAsync(trader.Id, time.GetUtcNow(), cancellationToken)).Token}"),
                PortalUsers.InviteLifetime), TraderEmailKind.Invitation)
            : (PlatformEmails.ChallengeStarted(firm.Name, challengeName, trader.Email, new Uri(firm.Portal.Url, $"accounts/{accountId}")), TraderEmailKind.Notice);
        try
        {
            await email.SendAsync(message, cancellationToken);
        }
        catch (EmailNotSentException)
        {
            return AccountActions.Problem(StatusCodes.Status503ServiceUnavailable, "The email could not be sent. Try again shortly.");
        }

        return TypedResults.Ok(new TraderEmailResponse(trader.Email, kind));
    }

    /// <summary>How a stage of any of the firm's accounts has gone, as its trader sees it. Without a stage, the latest that has started.</summary>
    private static Task<Results<Ok<PerformanceResponse>, ProblemHttpResult>> GetPerformanceAsync(
        Guid accountId,
        HttpContext context,
        ChallengeQueries queries,
        TradingHistoryQueries history,
        CancellationToken cancellationToken,
        int? stage = null) =>
        HistoryActions.PerformanceAsync(PortalFirmFilter.FirmOf(context), accountId, null, stage, queries, history, cancellationToken);

    /// <summary>A stage's closed positions, newest first, a page at a time.</summary>
    private static Task<Results<Ok<TradesResponse>, ProblemHttpResult>> ListTradesAsync(
        Guid accountId,
        HttpContext context,
        ChallengeQueries queries,
        TradingHistoryQueries history,
        CancellationToken cancellationToken,
        int? stage = null,
        long? before = null,
        int limit = 50) =>
        HistoryActions.TradesAsync(PortalFirmFilter.FirmOf(context), accountId, null, stage, before, limit, queries, history, cancellationToken);

    /// <summary>A stage's closed positions as a CSV file.</summary>
    private static Task<Results<FileContentHttpResult, ProblemHttpResult>> DownloadTradesAsync(
        Guid accountId,
        HttpContext context,
        ChallengeQueries queries,
        TradingHistoryQueries history,
        CancellationToken cancellationToken,
        int? stage = null) =>
        HistoryActions.TradesCsvAsync(PortalFirmFilter.FirmOf(context), accountId, null, stage, queries, history, cancellationToken);

    /// <summary>
    /// The firm's payouts, optionally only those with the statuses: the newest first, or with <paramref name="oldestFirst"/>
    /// the oldest first, for a queue the firm works through.
    /// </summary>
    private static async Task<Results<Ok<List<AdminPayoutResponse>>, ProblemHttpResult>> ListPayoutsAsync(
        HttpContext context,
        PayoutQueries payouts,
        CancellationToken cancellationToken,
        PayoutStatus[]? status = null,
        bool oldestFirst = false,
        int limit = 100)
    {
        if (limit is < 1 or > PayoutActions.MaxPayoutsPerRequest)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, $"limit must be 1 to {PayoutActions.MaxPayoutsPerRequest}.");
        }

        var views = await payouts.ListForAdminAsync(PortalFirmFilter.FirmOf(context).Id, status ?? [], oldestFirst, limit, cancellationToken);
        return TypedResults.Ok(views.Select(v => new AdminPayoutResponse(PayoutResponse.From(v.Payout), v.ChallengeName, v.PaidBefore, v.PaidBeforeAmount)).ToList());
    }

    private static async Task<Ok<PayoutSummaryResponse>> GetPayoutSummaryAsync(
        HttpContext context,
        PayoutQueries payouts,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(PayoutSummaryResponse.From(await payouts.SummaryAsync(PortalFirmFilter.FirmOf(context).Id, time.GetUtcNow() - RecentWindow, cancellationToken)));

    /// <summary>Each challenge's open accounts, those started in the last 30 days, and how its evaluations ended in the last 90.</summary>
    private static async Task<Ok<List<ChallengeFiguresResponse>>> ListChallengeFiguresAsync(
        HttpContext context,
        AdminFigures figures,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var challenges = await figures.ChallengesAsync(PortalFirmFilter.FirmOf(context).Id, now - RecentWindow, now - PassRateWindow, cancellationToken);
        return TypedResults.Ok(challenges.Select(c => new ChallengeFiguresResponse(c.ChallengeId, c.Trading, c.Started, new PassRateResponse(c.Passed, c.Ended))).ToList());
    }
}

/// <summary>How many accounts are in each group.</summary>
public sealed record AccountCountsResponse(int All, int Evaluation, int AwaitingFunding, int Funded, int Ended)
{
    internal static AccountCountsResponse From(AccountCounts counts) =>
        new(counts.All, counts.Evaluation, counts.AwaitingFunding, counts.Funded, counts.Ended);
}

/// <summary>A page of accounts, the counts in each group, and <paramref name="Next"/> to ask for the next page with, if there is one.</summary>
public sealed record AdminAccountsResponse(IReadOnlyList<AccountResponse> Accounts, AccountCountsResponse Counts, long? Next);

/// <summary>An amount in one currency.</summary>
public sealed record MoneyTotalResponse(string Currency, decimal Amount)
{
    internal static IReadOnlyList<MoneyTotalResponse> From(IEnumerable<MoneyAmount> amounts) => [.. amounts.Select(a => new MoneyTotalResponse(a.Currency, a.Amount))];
}

/// <summary>Payouts with a status, their amounts per currency and when the oldest got there: asked for, or approved for those to pay.</summary>
public sealed record PayoutGroupResponse(int Count, IReadOnlyList<MoneyTotalResponse> Totals, DateTimeOffset? Oldest)
{
    internal static PayoutGroupResponse From(PayoutGroup group) => new(group.Count, MoneyTotalResponse.From(group.Totals), group.Oldest);
}

/// <summary>
/// The payouts to approve, those approved and to pay, those paid in the last 30 days, and how many days those took on
/// average from the request to the payment.
/// </summary>
public sealed record PayoutSummaryResponse(PayoutGroupResponse ToApprove, PayoutGroupResponse ToPay, PayoutGroupResponse PaidLast30Days, double? AverageDaysToPay)
{
    internal static PayoutSummaryResponse From(PayoutSummary summary) =>
        new(
            PayoutGroupResponse.From(summary.ToApprove),
            PayoutGroupResponse.From(summary.ToPay),
            PayoutGroupResponse.From(summary.Paid),
            summary.AverageTimeToPay is { } average ? Math.Round(average.TotalDays, 1) : null);
}

/// <summary>
/// A payout as the admin panel lists it: the payout, its challenge's name, and how many payouts the account had paid
/// before it was asked for, and how much.
/// </summary>
public sealed record AdminPayoutResponse(PayoutResponse Payout, string ChallengeName, int PaidBefore, decimal PaidBeforeAmount);

/// <summary>Challenges bought in the portal and paid in the last 30 days, without those refunded.</summary>
public sealed record SalesResponse(int Orders, IReadOnlyList<MoneyTotalResponse> Totals);

/// <summary>Of the evaluations that ended in the last 90 days, how many passed every evaluation stage. Cancelled ones are left out.</summary>
public sealed record PassRateResponse(int Passed, int Ended);

/// <summary>The week from Monday <paramref name="Start"/> in UTC: challenges bought in the portal and payouts paid, per currency.</summary>
public sealed record WeekResponse(DateOnly Start, IReadOnlyList<MoneyTotalResponse> Sales, IReadOnlyList<MoneyTotalResponse> Payouts);

/// <summary>What happened to an account.</summary>
public enum ActivityKind
{
    /// <summary>The firm started the challenge, in the admin panel or through the firm API.</summary>
    ChallengeStarted,

    /// <summary>The trader bought the challenge in the portal.</summary>
    ChallengeBought,

    /// <summary>An evaluation stage was passed, and the next one started.</summary>
    StagePassed,

    /// <summary>The last evaluation stage was passed, and the funded account waits for the firm.</summary>
    EvaluationPassed,

    /// <summary>The firm approved the funded account, and it started.</summary>
    FundedStarted,

    ChallengeFailed,

    /// <summary>The challenge ran out of time.</summary>
    ChallengeExpired,

    ChallengeCancelled,
    PayoutRequested,
    PayoutPaid,
    PayoutRejected,
}

/// <summary>
/// Something that happened to one of the firm's accounts. <paramref name="StageName"/> is the stage passed, started or
/// ended on. <paramref name="Amount"/> is the price of a challenge bought, the result of a stage passed, or the payout.
/// <paramref name="Reason"/> is why a challenge failed (a <see cref="FailureReason"/>), expired (an
/// <see cref="ExpiryReason"/>), was cancelled or why a payout was rejected. <paramref name="Reference"/> is the firm's
/// reference for a payment.
/// </summary>
public sealed record ActivityResponse(
    ActivityKind Kind,
    DateTimeOffset Time,
    Guid AccountId,
    long AccountNumber,
    string Email,
    string ChallengeName,
    string? StageName,
    decimal? Amount,
    string? Currency,
    long? OrderNumber,
    int? TradingDays,
    string? Reason,
    string? Reference)
{
    internal static ActivityResponse From(ActivityRecord record) =>
        new(
            Enum.Parse<ActivityKind>(record.Kind),
            record.Time,
            record.AccountId,
            record.AccountNumber,
            record.Email,
            record.ChallengeName,
            record.StageName,
            record.Amount,
            record.Currency,
            record.OrderNumber,
            record.TradingDays,
            record.Reason,
            record.Reference);
}

/// <summary>
/// How the firm is doing: its accounts in each group, its payouts, its sales in the last 30 days, its pass rate in the
/// last 90, sales and payouts in each of the last 12 weeks, oldest first, and what happened lately, newest first.
/// </summary>
public sealed record AdminOverviewResponse(
    AccountCountsResponse Accounts,
    PayoutSummaryResponse Payouts,
    SalesResponse Sales,
    PassRateResponse PassRate,
    IReadOnlyList<WeekResponse> Weeks,
    IReadOnlyList<ActivityResponse> Activity);

/// <summary>A challenge's open accounts, those started in the last 30 days, and its pass rate in the last 90.</summary>
public sealed record ChallengeFiguresResponse(string ChallengeId, int Trading, int StartedLast30Days, PassRateResponse PassRate);

/// <summary>The paid order that started an account.</summary>
public sealed record OrderSummaryResponse(Guid Id, long Number, decimal Amount, string Currency, PaymentProvider Provider, DateTimeOffset PaidAt);

/// <summary>
/// An account's trader as the firm sees them: since when, whether they have chosen a password for the portal, their
/// accounts at the firm, newest first, their paid orders and what they bought for and were paid out, per currency, and
/// <paramref name="Order"/>, the order that started the account asked about.
/// </summary>
public sealed record TraderSummaryResponse(
    string Email,
    DateTimeOffset Since,
    bool HasPassword,
    IReadOnlyList<AccountResponse> Accounts,
    int Orders,
    IReadOnlyList<MoneyTotalResponse> Bought,
    IReadOnlyList<MoneyTotalResponse> PaidOut,
    OrderSummaryResponse? Order);

/// <summary>What the trader was emailed: an invitation to choose a password, or that the challenge has started.</summary>
public enum TraderEmailKind
{
    Invitation,
    Notice,
}

public sealed record TraderEmailResponse(string Email, TraderEmailKind Kind);
