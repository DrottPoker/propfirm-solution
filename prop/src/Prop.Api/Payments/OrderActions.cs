using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Http.HttpResults;

using Prop.Api.Api;
using Prop.Api.Challenges;
using Prop.Api.Firms;

namespace Prop.Api.Payments;

/// <summary>What the firm can do with its orders and prices, shared by the firm API and the admin panel.</summary>
internal static class OrderActions
{
    public static async Task<Results<Ok<List<OrderResponse>>, ProblemHttpResult>> ListAsync(
        Firm firm,
        OrderStatus? status,
        int limit,
        OrderStore orders,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > OrderStore.MaxOrdersPerRequest)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, $"limit must be 1 to {OrderStore.MaxOrdersPerRequest}.");
        }

        var list = await orders.ListAsync(firm.Id, status, limit, time.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(list.Select(OrderResponse.From).ToList());
    }

    public static async Task<Results<Ok<OrderDetailsResponse>, ProblemHttpResult>> GetAsync(
        Firm firm,
        Guid orderId,
        OrderStore orders,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        await orders.GetAsync(firm.Id, orderId, time.GetUtcNow(), cancellationToken) is { } order
            ? TypedResults.Ok(OrderDetailsResponse.From(order, await orders.EventsAsync(order.Id, cancellationToken)))
            : UnknownOrder();

    /// <summary>
    /// The firm got paid through its own checkout. The order's account starts. Only for orders paid through the
    /// firm's own checkout: Stripe reports its own payments, and test orders are paid on their test page.
    /// </summary>
    public static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> MarkPaidAsync(
        Firm firm,
        Guid orderId,
        MarkOrderRequest request,
        string source,
        OrderService service,
        CancellationToken cancellationToken)
    {
        var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();
        var change = await service.MarkPaidAsync(
            firm,
            orderId,
            new PaymentConfirmation(PaymentProvider.External, source, reference, null, null, Detail(reference)),
            cancellationToken);
        return ResultOf(change);
    }

    /// <summary>The firm gave the money back. Stripe reports its own refunds, so only the other orders are marked by hand.</summary>
    public static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> MarkRefundedAsync(
        Firm firm,
        Guid orderId,
        MarkOrderRequest request,
        string source,
        OrderService service,
        OrderStore orders,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (await orders.GetAsync(firm.Id, orderId, time.GetUtcNow(), cancellationToken) is not { } order)
        {
            return UnknownOrder();
        }

        if (order.Provider == PaymentProvider.Stripe)
        {
            return AccountActions.Problem(StatusCodes.Status409Conflict, OrderService.WrongProviderProblem(order.Provider));
        }

        var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();
        return ResultOf(await service.MarkRefundedAsync(firm, orderId, order.Provider, source, Detail(reference), cancellationToken));
    }

    public static async Task<Ok<List<ChallengePrice>>> ListPricesAsync(Firm firm, PriceCatalog prices, CancellationToken cancellationToken) =>
        TypedResults.Ok((await prices.ListAsync(firm.Id, cancellationToken)).ToList());

    /// <summary>Sets what the challenge sells for in the portal. Orders already made keep their price.</summary>
    public static async Task<Results<Ok<ChallengePrice>, ProblemHttpResult>> SavePriceAsync(
        Firm firm,
        string challengeId,
        PriceRequest request,
        PriceCatalog prices,
        CancellationToken cancellationToken)
    {
        if (PriceRules.Problem(request.Amount, request.Currency) is { } problem)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, problem);
        }

        var price = new ChallengePrice(challengeId, request.Amount, request.Currency!, request.ForSale);
        return await prices.SaveAsync(firm.Id, price, cancellationToken)
            ? TypedResults.Ok(price)
            : AccountActions.Problem(StatusCodes.Status404NotFound, "The firm has no such challenge.");
    }

    public static ProblemHttpResult UnknownOrder() => AccountActions.Problem(StatusCodes.Status404NotFound, "The firm has no such order.");

    public static Results<Ok<OrderResponse>, ProblemHttpResult> ResultOf(OrderChange change) => change.Outcome switch
    {
        OrderChangeOutcome.Done or OrderChangeOutcome.AlreadyDone => TypedResults.Ok(OrderResponse.From(change.Order!)),
        OrderChangeOutcome.Unknown => UnknownOrder(),
        _ => AccountActions.Problem(StatusCodes.Status409Conflict, change.Problem ?? "The order cannot be changed like that."),
    };

    private static string? Detail(string? reference) => reference is null ? null : new JsonObject { ["reference"] = reference }.ToJsonString();
}
