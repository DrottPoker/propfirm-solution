using Microsoft.AspNetCore.Http.HttpResults;

using Prop.Api.Challenges;
using Prop.Api.Firms;
using Prop.Rules;

namespace Prop.Api.Api;

/// <summary>
/// What can be done with payouts, shared by the firm API and the portal. The trader asks for a payout, and
/// the firm approves it, marks it as paid or rejects it. The rule engine decides; a decision that does not
/// fit the payout answers 409 with the reason.
/// </summary>
internal static class PayoutActions
{
    public const int MaxPayoutsPerRequest = 500;

    /// <summary>
    /// Asks for a payout of the funded account's profit. A trader id limits it to that trader's own accounts.
    /// The profit is then withdrawn from the trading account in the background.
    /// </summary>
    public static async Task<Results<Created<PayoutResponse>, ProblemHttpResult>> RequestAsync(
        Firm firm,
        Guid accountId,
        Guid? traderId,
        Func<Guid, string> location,
        ChallengeService challenges,
        ChallengeQueries accounts,
        PayoutQueries payouts,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await accounts.GetAsync(firm.Id, accountId, cancellationToken) is not { } view || (traderId is not null && view.Account.TraderId != traderId))
        {
            return AccountActions.UnknownAccount();
        }

        var now = time.GetUtcNow();
        var payoutId = Guid.CreateVersion7(now);
        var step = await challenges.ApplyAsync(firm, accountId, new RequestPayout(now, payoutId.ToString()), cancellationToken);
        if (step is null)
        {
            return AccountActions.UnknownAccount();
        }

        if (step.Outputs.OfType<InputIgnored>().FirstOrDefault() is { } ignored)
        {
            return AccountActions.Problem(StatusCodes.Status409Conflict, ignored.Reason);
        }

        var payout = await payouts.GetAsync(firm.Id, payoutId, cancellationToken);
        return TypedResults.Created(location(payoutId), PayoutResponse.From(payout!));
    }

    /// <summary>
    /// The trader's payouts from every account, newest first, with what the firm has paid, what is on its way and
    /// what the trader can ask for now, per currency.
    /// </summary>
    public static async Task<Ok<TraderPayoutsResponse>> ListForTraderAsync(
        Firm firm,
        Guid traderId,
        ChallengeQueries accounts,
        PayoutQueries payouts,
        CancellationToken cancellationToken)
    {
        var traderPayouts = await payouts.ListByTraderAsync(firm.Id, traderId, cancellationToken);
        var ready = (await accounts.ListByTraderAsync(firm.Id, traderId, cancellationToken))
            .Select(v => (v.Account.State.Definition.Currency, Quote: ChallengeRules.QuotePayout(v.Account.State)))
            .Where(r => r.Quote.CanRequest)
            .ToList();
        var totals = traderPayouts.Select(p => p.Currency)
            .Concat(ready.Select(r => r.Currency))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(currency => new PayoutTotalResponse(
                currency,
                traderPayouts.Where(p => p.Currency == currency && p.Status == PayoutStatus.Paid).Sum(p => p.Amount),
                traderPayouts.Where(p => p.Currency == currency && p.Status is PayoutStatus.Withdrawing or PayoutStatus.Pending or PayoutStatus.Approved).Sum(p => p.Amount),
                ready.Where(r => r.Currency == currency).Sum(r => r.Quote.Amount)));
        return TypedResults.Ok(new TraderPayoutsResponse([.. traderPayouts.Select(PayoutResponse.From)], [.. totals]));
    }

    public static async Task<Results<Ok<List<PayoutResponse>>, ProblemHttpResult>> ListAsync(
        Firm firm,
        PayoutStatus[]? status,
        int limit,
        PayoutQueries payouts,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > MaxPayoutsPerRequest)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, $"limit must be 1 to {MaxPayoutsPerRequest}.");
        }

        var views = await payouts.ListAsync(firm.Id, status ?? [], limit, cancellationToken);
        return TypedResults.Ok(views.Select(PayoutResponse.From).ToList());
    }

    public static async Task<Results<Ok<PayoutResponse>, ProblemHttpResult>> GetAsync(
        Firm firm,
        Guid payoutId,
        PayoutQueries payouts,
        CancellationToken cancellationToken) =>
        await payouts.GetAsync(firm.Id, payoutId, cancellationToken) is { } view ? TypedResults.Ok(PayoutResponse.From(view)) : UnknownPayout();

    /// <summary>Applies the firm's decision about the payout, such as approving it, to the payout's account.</summary>
    public static async Task<Results<Ok<PayoutResponse>, ProblemHttpResult>> DecideAsync(
        Firm firm,
        Guid payoutId,
        Func<string, ChallengeInput> decision,
        ChallengeService challenges,
        PayoutQueries payouts,
        CancellationToken cancellationToken)
    {
        if (await payouts.GetAsync(firm.Id, payoutId, cancellationToken) is not { } view)
        {
            return UnknownPayout();
        }

        var step = await challenges.ApplyAsync(firm, view.ChallengeAccountId, decision(payoutId.ToString()), cancellationToken);
        if (step?.Outputs.OfType<InputIgnored>().FirstOrDefault() is { } ignored)
        {
            return AccountActions.Problem(StatusCodes.Status409Conflict, ignored.Reason);
        }

        return TypedResults.Ok(PayoutResponse.From((await payouts.GetAsync(firm.Id, payoutId, cancellationToken))!));
    }

    public static Func<string, ChallengeInput> Approve(TimeProvider time) => id => new ApprovePayout(time.GetUtcNow(), id);

    public static Func<string, ChallengeInput> MarkPaid(MarkPayoutPaidRequest request, TimeProvider time) =>
        id => new MarkPayoutPaid(time.GetUtcNow(), id, string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim());

    public static Func<string, ChallengeInput> Reject(RejectPayoutRequest request, TimeProvider time) =>
        id => new RejectPayout(time.GetUtcNow(), id, string.IsNullOrWhiteSpace(request.Reason) ? "Rejected by the firm." : request.Reason.Trim());

    public static ProblemHttpResult UnknownPayout() => AccountActions.Problem(StatusCodes.Status404NotFound, "The firm has no such payout.");
}
