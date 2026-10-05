using Microsoft.AspNetCore.Http.HttpResults;

using Prop.Api.Billing;
using Prop.Api.Challenges;
using Prop.Api.Firms;
using Prop.Api.Identity;
using Prop.Api.Portal;
using Prop.Api.Trading;
using Prop.Rules;

namespace Prop.Api.Api;

/// <summary>
/// What can be done with a firm's challenge accounts, shared by the firm API and the portal. A trader id
/// limits an action to that trader's own accounts.
/// </summary>
internal static class AccountActions
{
    public static async Task<Results<Created<AccountResponse>, Ok<AccountResponse>, ProblemHttpResult>> StartAsync(
        Firm firm,
        StartAccountRequest request,
        string locationPrefix,
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

        var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference;
        var result = await challenges.StartAsync(firm, request.Email, request.ChallengeId, reference, cancellationToken);
        if (result.Refusal is { } refusal)
        {
            return Problem(StatusCodes.Status409Conflict, SlotService.RefusalMessage(refusal));
        }

        if (result.Account is not { } account)
        {
            return Problem(StatusCodes.Status404NotFound, "The firm has no such challenge.");
        }

        var response = AccountResponse.From((await queries.GetAsync(firm.Id, account.Id, cancellationToken))!);
        return result.Created ? TypedResults.Created($"{locationPrefix}/{account.Id}", response) : TypedResults.Ok(response);
    }

    /// <summary>
    /// Approves the funded account. 409 when the account has not passed its evaluation, or when the firm wants the trader's
    /// identity verified first and it is not (ADR 0042).
    /// </summary>
    public static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> ApproveFundingAsync(
        Firm firm,
        Guid accountId,
        ChallengeService challenges,
        ChallengeQueries queries,
        IdentityService identity,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await queries.GetAsync(firm.Id, accountId, cancellationToken) is { } view
            && await identity.ProblemAsync(firm, view.Account.TraderId, IdentityRequirement.Funding, cancellationToken) is { } unverified)
        {
            return Problem(StatusCodes.Status409Conflict, unverified);
        }

        return await ApplyAsync(firm, accountId, new ApproveFunding(time.GetUtcNow()), challenges, queries, cancellationToken);
    }

    /// <summary>Applies the firm's input, such as approving funding or cancelling. 409 when it does not fit the account's state.</summary>
    public static async Task<Results<Ok<AccountResponse>, ProblemHttpResult>> ApplyAsync(
        Firm firm,
        Guid accountId,
        ChallengeInput input,
        ChallengeService challenges,
        ChallengeQueries queries,
        CancellationToken cancellationToken)
    {
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

    /// <summary>The account as the portal shows it, with its trading account valued right now.</summary>
    public static async Task<Results<Ok<AccountDetailsResponse>, ProblemHttpResult>> DetailsAsync(
        Firm firm,
        Guid accountId,
        Guid? traderId,
        ChallengeQueries queries,
        AccountDetailsBuilder details,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(firm, accountId, traderId, queries, cancellationToken) is not { } view)
        {
            return UnknownAccount();
        }

        return TypedResults.Ok((await details.BuildAsync(firm, [view], cancellationToken)).Single());
    }

    /// <summary>A one-time link that logs the trader in to the trading terminal on the current stage's account.</summary>
    public static async Task<Results<Ok<LoginLinkResponse>, ProblemHttpResult>> TerminalLinkAsync(
        Firm firm,
        Guid accountId,
        Guid? traderId,
        ChallengeQueries queries,
        ITradingPlatform trading,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(firm, accountId, traderId, queries, cancellationToken) is not { } view)
        {
            return UnknownAccount();
        }

        if (view.Account.State is not { Status: ChallengeStatus.Active, AccountId: { } tradingAccountId }
            || firm.Trading is not { } firmTrading
            || await queries.TradingUserOfAsync(view.Account.TraderId, cancellationToken) is not { } userId)
        {
            return Problem(StatusCodes.Status409Conflict, "The trader has no open trading account right now.");
        }

        try
        {
            var link = await trading.CreateLoginLinkAsync(firmTrading, userId, tradingAccountId, cancellationToken);
            return TypedResults.Ok(new LoginLinkResponse(link.Url, link.ExpiresAt));
        }
        catch (TradingPlatformUnavailableException)
        {
            return Problem(StatusCodes.Status503ServiceUnavailable, "The trading platform cannot be reached. Try again shortly.");
        }
    }

    /// <summary>
    /// An invitation for the account's trader to choose a password for the firm's portal, which also resets a
    /// forgotten one. It replaces the trader's older unused invitations.
    /// </summary>
    public static async Task<Results<Ok<InviteResponse>, ProblemHttpResult>> InviteAsync(
        Firm firm,
        Guid accountId,
        ChallengeQueries queries,
        PortalUsers users,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await queries.GetAsync(firm.Id, accountId, cancellationToken) is not { } view)
        {
            return UnknownAccount();
        }

        var invite = await users.CreateInviteAsync(view.Account.TraderId, time.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(new InviteResponse(new Uri(firm.Portal.Url, $"invite?token={invite.Token}"), invite.ExpiresAt));
    }

    public static ProblemHttpResult UnknownAccount() => Problem(StatusCodes.Status404NotFound, "The firm has no such account.");

    public static ProblemHttpResult Problem(int statusCode, string title, IReadOnlyList<string>? errors = null) =>
        TypedResults.Problem(
            statusCode: statusCode,
            title: title,
            extensions: errors is null ? null : new Dictionary<string, object?> { ["errors"] = errors });

    // A trader's own accounts only; other accounts look like they do not exist.
    internal static async Task<AccountView?> FindAsync(Firm firm, Guid accountId, Guid? traderId, ChallengeQueries queries, CancellationToken cancellationToken) =>
        await queries.GetAsync(firm.Id, accountId, cancellationToken) is { } view && (traderId is null || view.Account.TraderId == traderId)
            ? view
            : null;
}
