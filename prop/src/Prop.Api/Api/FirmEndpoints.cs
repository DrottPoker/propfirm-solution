using Microsoft.AspNetCore.Http.HttpResults;

using Prop.Api.Challenges;
using Prop.Api.Firms;
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
            return Problem(StatusCodes.Status422UnprocessableEntity, "The id in the body must be the id in the address.");
        }

        if (ChallengeCatalog.Validate(definition) is { Count: > 0 } errors)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "The challenge is not valid.", errors);
        }

        await catalog.SaveAsync(FirmApiKeyFilter.FirmOf(context).Id, definition, cancellationToken);
        return TypedResults.Ok(definition);
    }

    private static async Task<Ok<IReadOnlyList<ChallengeDefinition>>> ListChallengesAsync(
        HttpContext context,
        ChallengeCatalog catalog,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await catalog.ListAsync(FirmApiKeyFilter.FirmOf(context).Id, cancellationToken));

    private static async Task<Results<Created<AccountResponse>, Ok<AccountResponse>, ProblemHttpResult>> StartAccountAsync(
        StartAccountRequest request,
        HttpContext context,
        ChallengeService challenges,
        ChallengeQueries queries,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@', StringComparison.Ordinal))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "A valid email address is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ChallengeId))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "A challenge id is required.");
        }

        var firm = FirmApiKeyFilter.FirmOf(context);
        var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference;
        var result = await challenges.StartAsync(firm, request.Email, request.ChallengeId, reference, cancellationToken);
        if (result.Account is not { } account)
        {
            return Problem(StatusCodes.Status404NotFound, "The firm has no such challenge.");
        }

        var response = AccountResponse.From((await queries.GetAsync(firm.Id, account.Id, cancellationToken))!);
        return result.Created ? TypedResults.Created($"/api/firm/v1/accounts/{account.Id}", response) : TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<List<AccountResponse>>, ProblemHttpResult>> ListAccountsAsync(
        string? email,
        HttpContext context,
        ChallengeQueries queries,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "An email address is required.");
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
            : UnknownAccount();

    /// <summary>Every input and decision, in order: the audit trail, including the evidence of a breach.</summary>
    private static async Task<Results<Ok<List<StepResponse>>, ProblemHttpResult>> GetHistoryAsync(
        Guid accountId,
        HttpContext context,
        ChallengeQueries queries,
        CancellationToken cancellationToken)
    {
        var steps = await queries.HistoryAsync(FirmApiKeyFilter.FirmOf(context).Id, accountId, cancellationToken);
        return steps.Count == 0
            ? UnknownAccount()
            : TypedResults.Ok(steps.Select(s => new StepResponse(s.Step, s.RecordedAt, s.Input, s.Outputs, s.SourceEvent)).ToList());
    }

    /// <summary>The firm has done its checks, for example KYC and agreement, and the trader gets the funded account.</summary>
    private static Task<Results<Ok<AccountResponse>, ProblemHttpResult>> ApproveFundingAsync(
        Guid accountId,
        HttpContext context,
        ChallengeService challenges,
        ChallengeQueries queries,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ApplyAsync(context, accountId, new ApproveFunding(time.GetUtcNow()), challenges, queries, cancellationToken);

    private static Task<Results<Ok<AccountResponse>, ProblemHttpResult>> CancelAsync(
        Guid accountId,
        CancelAccountRequest request,
        HttpContext context,
        ChallengeService challenges,
        ChallengeQueries queries,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        ApplyAsync(
            context,
            accountId,
            new CancelChallenge(time.GetUtcNow(), string.IsNullOrWhiteSpace(request.Reason) ? "Cancelled by the firm." : request.Reason),
            challenges,
            queries,
            cancellationToken);

    /// <summary>A one-time link that logs the trader in to the trading terminal on the current stage's account.</summary>
    private static async Task<Results<Ok<LoginLinkResponse>, ProblemHttpResult>> CreateLoginLinkAsync(
        Guid accountId,
        HttpContext context,
        ChallengeQueries queries,
        ITradingPlatform trading,
        CancellationToken cancellationToken)
    {
        var firm = FirmApiKeyFilter.FirmOf(context);
        if (await queries.GetAsync(firm.Id, accountId, cancellationToken) is not { } view)
        {
            return UnknownAccount();
        }

        var state = view.Account.State;
        if (state is not { Status: ChallengeStatus.Active, AccountId: { } tradingAccountId }
            || await queries.TradingUserOfAsync(view.Account.TraderId, cancellationToken) is not { } userId)
        {
            return Problem(StatusCodes.Status409Conflict, "The trader has no open trading account right now.");
        }

        try
        {
            var link = await trading.CreateLoginLinkAsync(firm.Trading, userId, tradingAccountId, cancellationToken);
            return TypedResults.Ok(new LoginLinkResponse(link.Url, link.ExpiresAt));
        }
        catch (TradingPlatformUnavailableException)
        {
            return Problem(StatusCodes.Status503ServiceUnavailable, "The trading platform cannot be reached. Try again shortly.");
        }
    }

    private static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> ApplyAsync(
        HttpContext context,
        Guid accountId,
        ChallengeInput input,
        ChallengeService challenges,
        ChallengeQueries queries,
        CancellationToken cancellationToken)
    {
        var firm = FirmApiKeyFilter.FirmOf(context);
        var step = await challenges.ApplyAsync(firm, accountId, input, cancellationToken);
        if (step is null)
        {
            return UnknownAccount();
        }

        if (step.Outputs.OfType<InputIgnored>().FirstOrDefault() is { } ignored)
        {
            return Problem(StatusCodes.Status409Conflict, ignored.Reason);
        }

        return TypedResults.Ok(AccountResponse.From((await queries.GetAsync(firm.Id, accountId, cancellationToken))!));
    }

    private static ProblemHttpResult UnknownAccount() => Problem(StatusCodes.Status404NotFound, "The firm has no such account.");

    private static ProblemHttpResult Problem(int statusCode, string title, IReadOnlyList<string>? errors = null) =>
        TypedResults.Problem(
            statusCode: statusCode,
            title: title,
            extensions: errors is null ? null : new Dictionary<string, object?> { ["errors"] = errors });
}
