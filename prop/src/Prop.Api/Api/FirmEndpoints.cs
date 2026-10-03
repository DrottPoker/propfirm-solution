using Microsoft.AspNetCore.Http.HttpResults;

using Prop.Api.Challenges;
using Prop.Api.Firms;
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
        return app;
    }

    /// <summary>Creates or replaces a challenge. Accounts already started keep the rules they were started with.</summary>
    private static async Task<Results<Ok<ChallengeDefinition>, ProblemHttpResult>> SaveChallengeAsync(
        string challengeId,
        ChallengeDefinition definition,
        HttpContext context,
        ChallengeCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (definition.Id != challengeId)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "The id in the body must be the id in the address.");
        }

        if (ChallengeCatalog.Validate(definition) is { Count: > 0 } errors)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "The challenge is not valid.", errors);
        }

        await catalog.SaveAsync(FirmApiKeyFilter.FirmOf(context).Id, definition, cancellationToken);
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

    /// <summary>The firm has done its checks, for example KYC and agreement, and the trader gets the funded account.</summary>
    private static Task<Results<Ok<AccountResponse>, ProblemHttpResult>> ApproveFundingAsync(
        Guid accountId,
        HttpContext context,
        ChallengeService challenges,
        ChallengeQueries queries,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        AccountActions.ApplyAsync(FirmApiKeyFilter.FirmOf(context), accountId, new ApproveFunding(time.GetUtcNow()), challenges, queries, cancellationToken);

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
