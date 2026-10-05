using Microsoft.AspNetCore.Http.HttpResults;

using Prop.Api.Billing;
using Prop.Api.Challenges;
using Prop.Api.Firms;
using Prop.Api.Identity;
using Prop.Api.Payments;
using Prop.Api.Portal;
using Prop.Api.Trading;
using Prop.Rules;

namespace Prop.Api.Api;

/// <summary>
/// For the firm's own systems, such as its website, checkout or CRM: the challenges it sells and its traders'
/// accounts. Every request carries the firm's key and reaches only the firm's own data. Versioned, since
/// systems outside our control depend on it.
/// </summary>
internal static class FirmEndpoints
{
    private const string Accounts = "/api/firm/v1/accounts";

    public static IEndpointRouteBuilder MapFirmApi(this IEndpointRouteBuilder app)
    {
        var firm = app.MapGroup("/api/firm/v1").WithTags("Firm").AddEndpointFilter<FirmApiKeyFilter>();
        firm.MapPut("/challenges/{challengeId}", SaveChallengeAsync);
        firm.MapGet("/challenges", ListChallengesAsync);
        firm.MapPost("/accounts", StartAccountAsync);
        firm.MapGet("/accounts", ListAccountsAsync);
        firm.MapGet("/accounts/{accountId:guid}", GetAccountAsync);
        firm.MapGet("/accounts/{accountId:guid}/history", GetHistoryAsync);
        firm.MapPost("/accounts/{accountId:guid}/approve-funding", ApproveFundingAsync);
        firm.MapPost("/accounts/{accountId:guid}/cancel", CancelAsync);
        firm.MapPost("/accounts/{accountId:guid}/login-link", CreateLoginLinkAsync);
        firm.MapPost("/accounts/{accountId:guid}/invite", CreateInviteAsync);
        firm.MapPost("/accounts/{accountId:guid}/payouts", RequestPayoutAsync);
        firm.MapGet("/accounts/{accountId:guid}/payouts", ListAccountPayoutsAsync);
        firm.MapGet("/payouts", ListPayoutsAsync);
        firm.MapGet("/payouts/{payoutId:guid}", GetPayoutAsync);
        firm.MapPost("/payouts/{payoutId:guid}/approve", ApprovePayoutAsync);
        firm.MapPost("/payouts/{payoutId:guid}/mark-paid", MarkPayoutPaidAsync);
        firm.MapPost("/payouts/{payoutId:guid}/reject", RejectPayoutAsync);
        firm.MapFirmOrders();
        firm.MapFirmSlots();
        firm.MapFirmIdentity();
        return app;
    }

    /// <summary>Creates or replaces a challenge. Accounts already started keep the rules they were started with.</summary>
    private static Task<Results<Ok<ChallengeDefinition>, ProblemHttpResult>> SaveChallengeAsync(
        string challengeId,
        ChallengeDefinition definition,
        HttpContext context,
        ChallengeCatalog catalog,
        CancellationToken cancellationToken) =>
        SaveChallengeOfAsync(FirmApiKeyFilter.FirmOf(context), challengeId, definition, catalog, cancellationToken);

    /// <summary>Saves the firm's challenge if it is valid. Shared with the admin panel.</summary>
    internal static async Task<Results<Ok<ChallengeDefinition>, ProblemHttpResult>> SaveChallengeOfAsync(
        Firm firm,
        string challengeId,
        ChallengeDefinition definition,
        ChallengeCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (definition.Id != challengeId)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "The id in the body must be the id in the address.");
        }

        if (ChallengeCatalog.Validate(definition, firm) is { Count: > 0 } errors)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "The challenge is not valid.", errors);
        }

        await catalog.SaveAsync(firm.Id, definition, cancellationToken);
        return TypedResults.Ok(definition);
    }

    private static async Task<Ok<IReadOnlyList<ChallengeDefinition>>> ListChallengesAsync(
        HttpContext context,
        ChallengeCatalog catalog,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await catalog.ListAsync(FirmApiKeyFilter.FirmOf(context).Id, cancellationToken));

    private static Task<Results<Created<AccountResponse>, Ok<AccountResponse>, ProblemHttpResult>> StartAccountAsync(
        StartAccountRequest request,
        HttpContext context,
        ChallengeService challenges,
        ChallengeQueries queries,
        CancellationToken cancellationToken) =>
        AccountActions.StartAsync(FirmApiKeyFilter.FirmOf(context), request, Accounts, challenges, queries, cancellationToken);

    private static async Task<Results<Ok<List<AccountResponse>>, ProblemHttpResult>> ListAccountsAsync(
        string? email,
        HttpContext context,
        ChallengeQueries queries,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "An email address is required.");
        }

        var views = await queries.ListByEmailAsync(FirmApiKeyFilter.FirmOf(context).Id, email, cancellationToken);
        return TypedResults.Ok(views.Select(AccountResponse.From).ToList());
    }

    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> GetAccountAsync(
        Guid accountId,
        HttpContext context,
        ChallengeQueries queries,
        CancellationToken cancellationToken) =>
        await queries.GetAsync(FirmApiKeyFilter.FirmOf(context).Id, accountId, cancellationToken) is { } view
            ? TypedResults.Ok(AccountResponse.From(view))
            : AccountActions.UnknownAccount();

    /// <summary>Every input and decision, in order: the audit trail, including the evidence of a breach.</summary>
    private static Task<Results<Ok<List<StepResponse>>, ProblemHttpResult>> GetHistoryAsync(
        Guid accountId,
        HttpContext context,
        ChallengeQueries queries,
        CancellationToken cancellationToken) =>
        HistoryAsync(FirmApiKeyFilter.FirmOf(context), accountId, queries, cancellationToken);

    /// <summary>
    /// The firm has done its checks, for example KYC and agreement, and the trader gets the funded account. 409 when the
    /// firm wants the trader's identity verified first and it is not.
    /// </summary>
    private static Task<Results<Ok<AccountResponse>, ProblemHttpResult>> ApproveFundingAsync(
        Guid accountId,
        HttpContext context,
        ChallengeService challenges,
        ChallengeQueries queries,
        IdentityService identity,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        AccountActions.ApproveFundingAsync(FirmApiKeyFilter.FirmOf(context), accountId, challenges, queries, identity, time, cancellationToken);

    private static Task<Results<Ok<AccountResponse>, ProblemHttpResult>> CancelAsync(
        Guid accountId,
        CancelAccountRequest request,
        HttpContext context,
        ChallengeService challenges,
        ChallengeQueries queries,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        AccountActions.ApplyAsync(FirmApiKeyFilter.FirmOf(context), accountId, Cancel(request, time), challenges, queries, cancellationToken);

    private static Task<Results<Ok<LoginLinkResponse>, ProblemHttpResult>> CreateLoginLinkAsync(
        Guid accountId,
        HttpContext context,
        ChallengeQueries queries,
        ITradingPlatform trading,
        CancellationToken cancellationToken) =>
        AccountActions.TerminalLinkAsync(FirmApiKeyFilter.FirmOf(context), accountId, null, queries, trading, cancellationToken);

    /// <summary>An invitation for the trader to choose a password for the firm's portal, for the firm to send.</summary>
    private static Task<Results<Ok<InviteResponse>, ProblemHttpResult>> CreateInviteAsync(
        Guid accountId,
        HttpContext context,
        ChallengeQueries queries,
        PortalUsers users,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        AccountActions.InviteAsync(FirmApiKeyFilter.FirmOf(context), accountId, queries, users, time, cancellationToken);

    /// <summary>A payout for the trader, for firms whose own site lets traders ask for one. The portal does the same.</summary>
    private static Task<Results<Created<PayoutResponse>, ProblemHttpResult>> RequestPayoutAsync(
        Guid accountId,
        HttpContext context,
        ChallengeService challenges,
        ChallengeQueries accounts,
        PayoutQueries payouts,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        PayoutActions.RequestAsync(
            FirmApiKeyFilter.FirmOf(context), accountId, null, id => $"/api/firm/v1/payouts/{id}", challenges, accounts, payouts, time, cancellationToken);

    private static async Task<Results<Ok<List<PayoutResponse>>, ProblemHttpResult>> ListAccountPayoutsAsync(
        Guid accountId,
        HttpContext context,
        ChallengeQueries accounts,
        PayoutQueries payouts,
        CancellationToken cancellationToken)
    {
        var firm = FirmApiKeyFilter.FirmOf(context);
        if (await accounts.GetAsync(firm.Id, accountId, cancellationToken) is null)
        {
            return AccountActions.UnknownAccount();
        }

        return TypedResults.Ok((await payouts.ListByAccountAsync(firm.Id, accountId, cancellationToken)).Select(PayoutResponse.From).ToList());
    }

    /// <summary>The firm's newest payouts, optionally only those with the given statuses, for example Pending to find those waiting for approval.</summary>
    private static Task<Results<Ok<List<PayoutResponse>>, ProblemHttpResult>> ListPayoutsAsync(
        HttpContext context,
        PayoutQueries payouts,
        CancellationToken cancellationToken,
        PayoutStatus[]? status = null,
        int limit = 100) =>
        PayoutActions.ListAsync(FirmApiKeyFilter.FirmOf(context), status, limit, payouts, cancellationToken);

    private static Task<Results<Ok<PayoutResponse>, ProblemHttpResult>> GetPayoutAsync(
        Guid payoutId,
        HttpContext context,
        PayoutQueries payouts,
        CancellationToken cancellationToken) =>
        PayoutActions.GetAsync(FirmApiKeyFilter.FirmOf(context), payoutId, payouts, cancellationToken);

    /// <summary>The firm has done its checks, for example KYC, and will send the money. 409 unless the payout is waiting for approval.</summary>
    private static Task<Results<Ok<PayoutResponse>, ProblemHttpResult>> ApprovePayoutAsync(
        Guid payoutId,
        HttpContext context,
        ChallengeService challenges,
        PayoutQueries payouts,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        PayoutActions.DecideAsync(FirmApiKeyFilter.FirmOf(context), payoutId, PayoutActions.Approve(time), challenges, payouts, cancellationToken);

    /// <summary>The firm has sent the money. 409 unless the payout is approved.</summary>
    private static Task<Results<Ok<PayoutResponse>, ProblemHttpResult>> MarkPayoutPaidAsync(
        Guid payoutId,
        MarkPayoutPaidRequest request,
        HttpContext context,
        ChallengeService challenges,
        PayoutQueries payouts,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        PayoutActions.DecideAsync(FirmApiKeyFilter.FirmOf(context), payoutId, PayoutActions.MarkPaid(request, time), challenges, payouts, cancellationToken);

    /// <summary>Refuses a payout waiting for approval or payment. The withdrawn profit is not returned to the account.</summary>
    private static Task<Results<Ok<PayoutResponse>, ProblemHttpResult>> RejectPayoutAsync(
        Guid payoutId,
        RejectPayoutRequest request,
        HttpContext context,
        ChallengeService challenges,
        PayoutQueries payouts,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        PayoutActions.DecideAsync(FirmApiKeyFilter.FirmOf(context), payoutId, PayoutActions.Reject(request, time), challenges, payouts, cancellationToken);

    internal static CancelChallenge Cancel(CancelAccountRequest request, TimeProvider time) =>
        new(time.GetUtcNow(), string.IsNullOrWhiteSpace(request.Reason) ? "Cancelled by the firm." : request.Reason);

    internal static async Task<Results<Ok<List<StepResponse>>, ProblemHttpResult>> HistoryAsync(
        Firm firm,
        Guid accountId,
        ChallengeQueries queries,
        CancellationToken cancellationToken)
    {
        var steps = await queries.HistoryAsync(firm.Id, accountId, cancellationToken);
        return steps.Count == 0
            ? AccountActions.UnknownAccount()
            : TypedResults.Ok(steps.Select(s => new StepResponse(s.Step, s.RecordedAt, s.Input, s.Outputs, s.SourceEvent)).ToList());
    }
}
