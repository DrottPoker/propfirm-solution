using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using Prop.Api.Payments;

namespace Prop.Api.Tests.Support;

/// <summary>
/// Stripe's API as far as orders use it: Checkout Sessions are created and remembered, or refused. Also makes
/// the webhook events Stripe would send, signed with a firm's secret.
/// </summary>
internal sealed class FakeStripe : HttpMessageHandler
{
    public const string SecretKey = "sk_test_51FakeStripeSecretKey";

    public const string WebhookSecret = "whsec_FakeStripeWebhookSecret";

    private readonly Lock _lock = new();
    private readonly List<StripeRequest> _requests = [];
    private string? _refusal;

    public IReadOnlyList<StripeRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>The next session is refused with Stripe's error message.</summary>
    public void RefuseNext(string message)
    {
        lock (_lock)
        {
            _refusal = message;
        }
    }

    /// <summary>A checkout.session.* event about the session of the order.</summary>
    public static string CheckoutEvent(string type, StripeRequest session, string paymentStatus = "paid", long? amountTotal = null) =>
        Event(type, new JsonObject
        {
            ["object"] = "checkout.session",
            ["id"] = session.SessionId,
            ["client_reference_id"] = session.Form["client_reference_id"],
            ["metadata"] = new JsonObject { ["order_id"] = session.Form["metadata[order_id]"], ["firm_id"] = session.Form["metadata[firm_id]"] },
            ["payment_status"] = paymentStatus,
            ["payment_intent"] = session.PaymentIntent,
            ["amount_total"] = amountTotal ?? long.Parse(session.Form["line_items[0][price_data][unit_amount]"], System.Globalization.CultureInfo.InvariantCulture),
            ["currency"] = session.Form["line_items[0][price_data][currency]"],
        });

    /// <summary>A charge.refunded event for the session's payment, fully refunded.</summary>
    public static string RefundEvent(StripeRequest session) =>
        Event("charge.refunded", new JsonObject
        {
            ["object"] = "charge",
            ["id"] = $"ch_{session.SessionId}",
            ["payment_intent"] = session.PaymentIntent,
            ["refunded"] = true,
        });

    public static string DisputeEvent(StripeRequest session) =>
        Event("charge.dispute.created", new JsonObject
        {
            ["object"] = "dispute",
            ["id"] = $"dp_{session.SessionId}",
            ["payment_intent"] = session.PaymentIntent,
            ["reason"] = "fraudulent",
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = (await request.Content!.ReadAsStringAsync(cancellationToken))
            .Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(p => WebUtility.UrlDecode(p[0]), p => WebUtility.UrlDecode(p[1]));
        lock (_lock)
        {
            var sessionId = $"cs_test_{_requests.Count + 1}";
            _requests.Add(new StripeRequest(
                request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.ToString(),
                request.Headers.GetValues("Idempotency-Key").Single(),
                form,
                sessionId));
            if (_refusal is { } refusal)
            {
                _refusal = null;
                return Json(HttpStatusCode.BadRequest, new JsonObject { ["error"] = new JsonObject { ["message"] = refusal } });
            }

            return Json(HttpStatusCode.OK, new JsonObject { ["id"] = sessionId, ["url"] = $"https://checkout.stripe.test/c/pay/{sessionId}" });
        }
    }

    private static string Event(string type, JsonObject data) =>
        new JsonObject
        {
            ["id"] = $"evt_{Guid.NewGuid():N}",
            ["object"] = "event",
            ["type"] = type,
            ["data"] = new JsonObject { ["object"] = data },
        }.ToJsonString();

    private static HttpResponseMessage Json(HttpStatusCode status, JsonObject body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
}

/// <summary>A Checkout Session the service asked Stripe for, and the session Stripe made.</summary>
internal sealed record StripeRequest(string Path, string? Authorization, string IdempotencyKey, IReadOnlyDictionary<string, string> Form, string SessionId)
{
    public string PaymentIntent => $"pi_{SessionId}";

    public Guid OrderId => Guid.Parse(Form["metadata[order_id]"]);
}

internal static class StripeWebhooks
{
    /// <summary>Sends the event to the firm's Stripe webhook, signed now with the secret.</summary>
    public static Task<HttpResponseMessage> SendAsync(PropFactory factory, string firmId, string body, string secret = FakeStripe.WebhookSecret)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/payments/v1/stripe/{firmId}", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(StripeSignature.Header, StripeSignature.Sign(secret, factory.Time.GetUtcNow(), body));
        return client.SendAsync(request);
    }
}
