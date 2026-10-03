using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using Prop.Api.Payments;

namespace Prop.Api.Tests.Support;

/// <summary>
/// Stripe's API as far as the service uses it: Checkout Sessions are created and remembered, or refused, and can
/// be expired. Charges of a saved card are made or declined, and payments and saved cards can be read back. Also
/// makes the webhook events Stripe would send, signed with a secret.
/// </summary>
internal sealed class FakeStripe : HttpMessageHandler
{
    /// <summary>A firm's own test key, for its shop.</summary>
    public const string SecretKey = "sk_test_51FakeStripeSecretKey";

    public const string WebhookSecret = "whsec_FakeStripeWebhookSecret";

    /// <summary>Our own account's key, for what firms pay us.</summary>
    public const string PlatformKey = "sk_test_51FakePlatformSecretKey";

    public const string PlatformWebhookSecret = "whsec_FakePlatformWebhookSecret";

    public const string Customer = "cus_test_firm";

    private readonly Lock _lock = new();
    private readonly List<StripeRequest> _requests = [];
    private readonly List<StripeRequest> _charges = [];
    private readonly List<string> _expired = [];
    private string? _refusal;
    private bool _declineCharges;
    private int _failingCharges;

    /// <summary>The Checkout Sessions asked for, in order.</summary>
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

    /// <summary>The charges of a saved card asked for, in order.</summary>
    public IReadOnlyList<StripeRequest> Charges
    {
        get
        {
            lock (_lock)
            {
                return [.. _charges];
            }
        }
    }

    /// <summary>The Checkout Sessions that were expired.</summary>
    public IReadOnlyList<string> Expired
    {
        get
        {
            lock (_lock)
            {
                return [.. _expired];
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

    /// <summary>Whether charges of a saved card are declined, as with a card that has no money.</summary>
    public void DeclineCharges(bool decline)
    {
        lock (_lock)
        {
            _declineCharges = decline;
        }
    }

    /// <summary>The next charges of a saved card fail with a server error, which Stripe keeps as the answer to their key.</summary>
    public void FailNextCharges(int charges)
    {
        lock (_lock)
        {
            _failingCharges = charges;
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
            ["amount_total"] = amountTotal ?? long.Parse(session.Form["line_items[0][price_data][unit_amount]"], CultureInfo.InvariantCulture),
            ["currency"] = session.Form["line_items[0][price_data][currency]"],
        });

    /// <summary>A checkout.session.* event about a session of ours: a payment, or a card saved in setup mode.</summary>
    public static string BillingCheckoutEvent(string type, StripeRequest session)
    {
        var setup = session.Form["mode"] == "setup";
        return Event(type, new JsonObject
        {
            ["object"] = "checkout.session",
            ["id"] = session.SessionId,
            ["mode"] = session.Form["mode"],
            ["customer"] = Customer,
            ["metadata"] = new JsonObject { ["firm_id"] = session.Form["metadata[firm_id]"], ["purpose"] = session.Form["metadata[purpose]"] },
            ["payment_status"] = setup ? "no_payment_required" : "paid",
            ["payment_intent"] = setup ? null : session.PaymentIntent,
            ["setup_intent"] = setup ? $"seti_{session.SessionId}" : null,
            ["amount_total"] = setup ? null : session.AmountTotal,
        });
    }

    /// <summary>A payment_intent.succeeded event about a charge of a saved card.</summary>
    public static string ChargeSucceededEvent(Guid chargeId, long amount, string source = "card_on_file") =>
        Event("payment_intent.succeeded", new JsonObject
        {
            ["object"] = "payment_intent",
            ["id"] = $"pi_reported_{chargeId:N}",
            ["amount_received"] = amount,
            ["currency"] = "usd",
            ["metadata"] = new JsonObject { ["charge_id"] = chargeId.ToString(), ["source"] = source },
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
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var form = body.Length == 0
            ? new Dictionary<string, string>()
            : body.Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(p => WebUtility.UrlDecode(p[0]), p => WebUtility.UrlDecode(p[1]));
        var key = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : "";
        lock (_lock)
        {
            if (request.Method == HttpMethod.Post && path == "/v1/checkout/sessions")
            {
                var sessionId = $"cs_test_{_requests.Count + 1}";
                _requests.Add(new StripeRequest(path, request.Headers.Authorization?.ToString(), key, form, sessionId));
                if (_refusal is { } refusal)
                {
                    _refusal = null;
                    return Error(HttpStatusCode.BadRequest, new JsonObject { ["message"] = refusal });
                }

                return Json(HttpStatusCode.OK, new JsonObject { ["id"] = sessionId, ["url"] = $"https://checkout.stripe.test/c/pay/{sessionId}" });
            }

            if (request.Method == HttpMethod.Post && path.StartsWith("/v1/checkout/sessions/", StringComparison.Ordinal) && path.EndsWith("/expire", StringComparison.Ordinal))
            {
                var sessionId = path.Split('/')[4];
                _expired.Add(sessionId);
                return Json(HttpStatusCode.OK, new JsonObject { ["id"] = sessionId, ["status"] = "expired" });
            }

            if (request.Method == HttpMethod.Post && path == "/v1/payment_intents")
            {
                var charge = new StripeRequest(path, request.Headers.Authorization?.ToString(), key, form, $"offsession_{_charges.Count + 1}");
                _charges.Add(charge);
                if (_failingCharges > 0)
                {
                    _failingCharges--;
                    return Error(HttpStatusCode.InternalServerError, new JsonObject { ["type"] = "api_error", ["message"] = "Something went wrong on Stripe's end." });
                }

                return _declineCharges
                    ? Error(
                        HttpStatusCode.PaymentRequired,
                        new JsonObject
                        {
                            ["type"] = "card_error",
                            ["code"] = "card_declined",
                            ["message"] = "Your card was declined.",
                            ["payment_intent"] = new JsonObject { ["id"] = charge.PaymentIntent, ["status"] = "requires_payment_method" },
                        })
                    : Json(HttpStatusCode.OK, new JsonObject { ["id"] = charge.PaymentIntent, ["status"] = "succeeded" });
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/v1/payment_intents/", StringComparison.Ordinal))
            {
                var paymentIntent = path.Split('/')[3];
                var session = _requests.Single(r => r.PaymentIntent == paymentIntent);
                return Json(HttpStatusCode.OK, new JsonObject
                {
                    ["id"] = paymentIntent,
                    ["amount"] = session.AmountTotal,
                    ["currency"] = "usd",
                    ["customer"] = Customer,
                    ["payment_method"] = Card("pm_card_visa", "visa", "4242"),
                });
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/v1/setup_intents/", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, new JsonObject
                {
                    ["id"] = path.Split('/')[3],
                    ["customer"] = Customer,
                    ["payment_method"] = Card("pm_card_mastercard", "mastercard", "4444"),
                });
            }

            return Error(HttpStatusCode.NotFound, new JsonObject { ["message"] = $"The fake Stripe has no {request.Method} {path}." });
        }
    }

    private static JsonObject Card(string id, string brand, string last4) =>
        new()
        {
            ["id"] = id,
            ["card"] = new JsonObject { ["brand"] = brand, ["last4"] = last4, ["exp_month"] = 12, ["exp_year"] = 2030 },
        };

    private static string Event(string type, JsonObject data) =>
        new JsonObject
        {
            ["id"] = $"evt_{Guid.NewGuid():N}",
            ["object"] = "event",
            ["type"] = type,
            ["data"] = new JsonObject { ["object"] = data },
        }.ToJsonString();

    private static HttpResponseMessage Error(HttpStatusCode status, JsonObject error) => Json(status, new JsonObject { ["error"] = error });

    private static HttpResponseMessage Json(HttpStatusCode status, JsonObject body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
}

/// <summary>A request the service made of Stripe, such as a Checkout Session, and the id Stripe gave it.</summary>
internal sealed record StripeRequest(string Path, string? Authorization, string IdempotencyKey, IReadOnlyDictionary<string, string> Form, string SessionId)
{
    public string PaymentIntent => $"pi_{SessionId}";

    public Guid OrderId => Guid.Parse(Form["metadata[order_id]"]);

    /// <summary>The sum of the session's lines, in cents.</summary>
    public long AmountTotal =>
        Form.Where(f => f.Key.EndsWith("[price_data][unit_amount]", StringComparison.Ordinal)).Sum(f => long.Parse(f.Value, CultureInfo.InvariantCulture));
}

internal static class StripeWebhooks
{
    /// <summary>Sends the event to the firm's Stripe webhook, signed now with the secret.</summary>
    public static Task<HttpResponseMessage> SendAsync(PropFactory factory, string firmId, string body, string secret = FakeStripe.WebhookSecret) =>
        PostAsync(factory, $"/api/payments/v1/stripe/{firmId}", body, secret);

    /// <summary>Sends the event to the webhook for our own payments, signed now with the secret.</summary>
    public static Task<HttpResponseMessage> SendBillingAsync(PropFactory factory, string body, string secret = FakeStripe.PlatformWebhookSecret) =>
        PostAsync(factory, "/api/payments/v1/billing/stripe", body, secret);

    private static Task<HttpResponseMessage> PostAsync(PropFactory factory, string path, string body, string secret)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(StripeSignature.Header, StripeSignature.Sign(secret, factory.Time.GetUtcNow(), body));
        return client.SendAsync(request);
    }
}
