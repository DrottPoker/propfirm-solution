using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Http.HttpResults;

using Prop.Api.Firms;

namespace Prop.Api.Payments;

/// <summary>
/// The payment providers' webhooks, which must be reachable from the internet (ADR 0018), and the firm API's
/// orders and prices.
/// </summary>
internal static partial class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentWebhooks(this IEndpointRouteBuilder app)
    {
        // Not part of the API anyone calls: Stripe does, signed with the firm's own secret.
        app.MapPost("/api/payments/v1/stripe/{firmId}", StripeWebhookAsync).ExcludeFromDescription();
        return app;
    }

    /// <summary>Maps the orders and prices on the firm API's group.</summary>
    public static RouteGroupBuilder MapFirmOrders(this RouteGroupBuilder firm)
    {
        firm.MapGet("/prices", (HttpContext context, PriceCatalog prices, CancellationToken cancellationToken) =>
            OrderActions.ListPricesAsync(FirmApiKeyFilter.FirmOf(context), prices, cancellationToken));
        firm.MapPut("/challenges/{challengeId}/price", (string challengeId, PriceRequest request, HttpContext context, PriceCatalog prices, CancellationToken cancellationToken) =>
            OrderActions.SavePriceAsync(FirmApiKeyFilter.FirmOf(context), challengeId, request, prices, cancellationToken));
        firm.MapGet("/orders", ListOrdersAsync);
        firm.MapGet("/orders/{orderId:guid}", (Guid orderId, HttpContext context, OrderStore orders, TimeProvider time, CancellationToken cancellationToken) =>
            OrderActions.GetAsync(FirmApiKeyFilter.FirmOf(context), orderId, orders, time, cancellationToken));
        firm.MapPost("/orders/{orderId:guid}/mark-paid", (Guid orderId, MarkOrderRequest request, HttpContext context, OrderService service, CancellationToken cancellationToken) =>
            OrderActions.MarkPaidAsync(FirmApiKeyFilter.FirmOf(context), orderId, request, OrderSources.FirmApi, service, cancellationToken));
        firm.MapPost(
            "/orders/{orderId:guid}/mark-refunded",
            (Guid orderId, MarkOrderRequest request, HttpContext context, OrderService service, OrderStore orders, TimeProvider time, CancellationToken cancellationToken) =>
                OrderActions.MarkRefundedAsync(FirmApiKeyFilter.FirmOf(context), orderId, request, OrderSources.FirmApi, service, orders, time, cancellationToken));
        return firm;
    }

    /// <summary>The firm's newest orders, optionally only those with a status.</summary>
    private static Task<Results<Ok<List<OrderResponse>>, ProblemHttpResult>> ListOrdersAsync(
        HttpContext context,
        OrderStore orders,
        TimeProvider time,
        CancellationToken cancellationToken,
        OrderStatus? status = null,
        int limit = 100) =>
        OrderActions.ListAsync(FirmApiKeyFilter.FirmOf(context), status, limit, orders, time, cancellationToken);

    /// <summary>
    /// A Stripe event for the firm's checkouts. Only a valid signature with the firm's webhook secret is
    /// accepted. Events about other things in the firm's Stripe account are answered with 200 and ignored, so
    /// Stripe does not send them again.
    /// </summary>
    private static async Task<IResult> StripeWebhookAsync(
        string firmId,
        HttpContext context,
        FirmCatalog firms,
        OrderService service,
        OrderStore orders,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        await firms.Ready.WaitAsync(cancellationToken);
        if (firms.ById(firmId) is not { Payments.Stripe: { } keys } firm)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "No firm takes Stripe payments here.");
        }

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);
        if (!StripeSignature.IsValid(keys.WebhookSecret, context.Request.Headers[StripeSignature.Header], body, time.GetUtcNow()))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The Stripe signature is not valid.");
        }

        StripeEvent stripeEvent;
        try
        {
            stripeEvent = StripeEvent.Parse(body);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The event is not a Stripe event.");
        }

        var logger = loggers.CreateLogger(typeof(PaymentEndpoints));
        switch (stripeEvent.Type)
        {
            case "checkout.session.completed" or "checkout.session.async_payment_succeeded" when stripeEvent.PaymentStatus == "paid":
                if (await CheckoutOrderAsync(firm, stripeEvent, orders, time, cancellationToken) is { } paid)
                {
                    var payment = new PaymentConfirmation(
                        PaymentProvider.Stripe,
                        OrderSources.Stripe,
                        stripeEvent.PaymentIntent,
                        stripeEvent.AmountTotal is { } total ? StripeClient.FromMinorUnits(total) : null,
                        stripeEvent.Currency,
                        body);
                    await service.MarkPaidAsync(firm, paid.Id, payment, cancellationToken);
                }

                break;
            case "checkout.session.expired" or "checkout.session.async_payment_failed":
                if (await CheckoutOrderAsync(firm, stripeEvent, orders, time, cancellationToken) is { } expired)
                {
                    await service.ExpireAsync(firm, expired.Id, PaymentProvider.Stripe, OrderSources.Stripe, body, cancellationToken);
                }

                break;
            case "charge.refunded" when stripeEvent.Refunded:
                if (await PaymentOrderAsync(firm, stripeEvent, orders, time, cancellationToken) is { } refunded)
                {
                    await service.MarkRefundedAsync(firm, refunded.Id, PaymentProvider.Stripe, OrderSources.Stripe, body, cancellationToken);
                }

                break;
            case "charge.dispute.created":
                if (await PaymentOrderAsync(firm, stripeEvent, orders, time, cancellationToken) is { } disputed)
                {
                    await service.MarkDisputedAsync(firm, disputed.Id, PaymentProvider.Stripe, OrderSources.Stripe, body, cancellationToken);
                }

                break;
            default:
                LogIgnored(logger, stripeEvent.Type, firm.Id);
                break;
        }

        return TypedResults.Ok();
    }

    // The order the checkout was made for. The session must be the one the order started, so an event cannot
    // name another order of the firm.
    private static async Task<Order?> CheckoutOrderAsync(Firm firm, StripeEvent stripeEvent, OrderStore orders, TimeProvider time, CancellationToken cancellationToken) =>
        stripeEvent.OrderId is { } orderId
        && await orders.GetAsync(firm.Id, orderId, time.GetUtcNow(), cancellationToken) is { Provider: PaymentProvider.Stripe } order
        && order.CheckoutId == stripeEvent.ObjectId
            ? order
            : null;

    private static async Task<Order?> PaymentOrderAsync(Firm firm, StripeEvent stripeEvent, OrderStore orders, TimeProvider time, CancellationToken cancellationToken) =>
        stripeEvent.PaymentIntent is { } paymentIntent
            ? await orders.FindByPaymentAsync(firm.Id, PaymentProvider.Stripe, paymentIntent, time.GetUtcNow(), cancellationToken)
            : null;

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored Stripe event {EventType} for firm {FirmId}")]
    private static partial void LogIgnored(ILogger logger, string eventType, string firmId);

    /// <summary>The parts of a Stripe event that orders need.</summary>
    private sealed record StripeEvent(
        string Type,
        string? ObjectId,
        Guid? OrderId,
        string? PaymentStatus,
        string? PaymentIntent,
        long? AmountTotal,
        string? Currency,
        bool Refunded)
    {
        public static StripeEvent Parse(string body)
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var data = root.GetProperty("data").GetProperty("object");
            return new StripeEvent(
                root.GetProperty("type").GetString()!,
                String(data, "id"),
                data.TryGetProperty("metadata", out var metadata) && String(metadata, "order_id") is { } orderId && Guid.TryParse(orderId, out var id) ? id : null,
                String(data, "payment_status"),
                String(data, "payment_intent"),
                data.TryGetProperty("amount_total", out var amount) && amount.ValueKind == JsonValueKind.Number ? amount.GetInt64() : null,
                String(data, "currency"),
                data.TryGetProperty("refunded", out var refunded) && refunded.ValueKind == JsonValueKind.True);
        }

        private static string? String(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
