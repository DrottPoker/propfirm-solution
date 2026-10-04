using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Prop.Api.Payments;

/// <summary>A Stripe Checkout Session to create for an order, paid to the firm's own Stripe account.</summary>
internal sealed record StripeCheckoutRequest(
    Guid OrderId,
    string FirmId,
    string Email,
    string ProductName,
    decimal Amount,
    string Currency,
    Uri SuccessUrl,
    Uri CancelUrl,
    DateTimeOffset ExpiresAt);

/// <summary>The Checkout Session, and the Stripe page where the buyer pays.</summary>
internal sealed record StripeCheckout(string Id, Uri Url);

/// <summary>The payment provider could not start the payment. The message is safe to show the firm, not the buyer.</summary>
internal sealed class CheckoutNotStartedException : Exception
{
    public CheckoutNotStartedException()
    {
    }

    public CheckoutNotStartedException(string message)
        : base(message)
    {
    }

    public CheckoutNotStartedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Stripe did not let us set up the firm's webhook. The message is Stripe's, safe to show the firm.</summary>
internal sealed class StripeSetupException : Exception
{
    public StripeSetupException()
    {
    }

    public StripeSetupException(string message)
        : base(message)
    {
    }

    public StripeSetupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Creates Stripe Checkout Sessions with the firm's own secret key, so the money goes straight to the firm. The
/// buyer pays on Stripe's page, and the card never reaches us. Also sets up the firm's webhook in its Stripe account.
/// </summary>
internal sealed class StripeClient(IHttpClientFactory httpClients)
{
    public const string HttpClientName = "Stripe";

    /// <summary>The Stripe events that move the firm's orders along.</summary>
    public static readonly IReadOnlyList<string> WebhookEvents =
    [
        "checkout.session.completed",
        "checkout.session.async_payment_succeeded",
        "checkout.session.async_payment_failed",
        "checkout.session.expired",
        "charge.refunded",
        "charge.dispute.created",
    ];

    /// <summary>
    /// Points a webhook in the firm's Stripe account at <paramref name="url"/> for <see cref="WebhookEvents"/>, and returns its
    /// signing secret, which Stripe shows only when the webhook is made. Webhooks we made at the same address before are
    /// removed, so a new key never leaves two behind. Also proves that the key works.
    /// </summary>
    public async Task<string> SetUpWebhookAsync(string secretKey, Uri url, string firmId, CancellationToken cancellationToken)
    {
        var client = httpClients.CreateClient(HttpClientName);
        try
        {
            using (var list = Request(HttpMethod.Get, "v1/webhook_endpoints?limit=100", secretKey))
            {
                using var response = await client.SendAsync(list, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new StripeSetupException(ErrorMessageOf(body));
                }

                using var endpoints = JsonDocument.Parse(body);
                foreach (var endpoint in endpoints.RootElement.GetProperty("data").EnumerateArray())
                {
                    if (endpoint.GetProperty("url").GetString() == url.ToString())
                    {
                        using var delete = Request(HttpMethod.Delete, $"v1/webhook_endpoints/{endpoint.GetProperty("id").GetString()}", secretKey);
                        using var deleted = await client.SendAsync(delete, cancellationToken);
                    }
                }
            }

            using var create = Request(HttpMethod.Post, "v1/webhook_endpoints", secretKey);
            create.Content = new FormUrlEncodedContent(
            [
                new("url", url.ToString()),
                .. WebhookEvents.Select(e => new KeyValuePair<string, string>("enabled_events[]", e)),
                new("description", "Orders in the firm's portal"),
                new("metadata[firm_id]", firmId),
            ]);
            using var created = await client.SendAsync(create, cancellationToken);
            var createdBody = await created.Content.ReadAsStringAsync(cancellationToken);
            if (!created.IsSuccessStatusCode)
            {
                throw new StripeSetupException(ErrorMessageOf(createdBody));
            }

            using var webhook = JsonDocument.Parse(createdBody);
            return webhook.RootElement.GetProperty("secret").GetString() ?? throw new StripeSetupException("Stripe sent no signing secret.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException && !cancellationToken.IsCancellationRequested)
        {
            throw new StripeSetupException("Stripe could not be reached.", exception);
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string secretKey)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        return request;
    }

    public async Task<StripeCheckout> CreateCheckoutAsync(string secretKey, StripeCheckoutRequest checkout, CancellationToken cancellationToken)
    {
        var orderId = checkout.OrderId.ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("v1/checkout/sessions", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);

        // A retry with the same order never makes a second session.
        request.Headers.Add("Idempotency-Key", $"order-{orderId}");
        request.Content = new FormUrlEncodedContent(
        [
            new("mode", "payment"),
            new("line_items[0][quantity]", "1"),
            new("line_items[0][price_data][currency]", checkout.Currency.ToLowerInvariant()),
            new("line_items[0][price_data][unit_amount]", MinorUnits(checkout.Amount)),
            new("line_items[0][price_data][product_data][name]", checkout.ProductName),
            new("customer_email", checkout.Email),
            new("client_reference_id", orderId),
            new("metadata[order_id]", orderId),
            new("metadata[firm_id]", checkout.FirmId),
            new("payment_intent_data[metadata][order_id]", orderId),
            new("success_url", checkout.SuccessUrl.ToString()),
            new("cancel_url", checkout.CancelUrl.ToString()),
            new("expires_at", checkout.ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        ]);

        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new CheckoutNotStartedException($"Stripe answered {(int)response.StatusCode}: {ErrorMessageOf(body)}");
            }

            using var session = JsonDocument.Parse(body);
            return new StripeCheckout(session.RootElement.GetProperty("id").GetString()!, new Uri(session.RootElement.GetProperty("url").GetString()!));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException && !cancellationToken.IsCancellationRequested)
        {
            throw new CheckoutNotStartedException("Stripe could not be reached.", exception);
        }
    }

    /// <summary>Cents, since every allowed currency has two decimals.</summary>
    public static string MinorUnits(decimal amount) => decimal.ToInt64(amount * 100m).ToString(CultureInfo.InvariantCulture);

    public static decimal FromMinorUnits(long amount) => amount / 100m;

    private static string ErrorMessageOf(string body)
    {
        try
        {
            using var error = JsonDocument.Parse(body);
            return error.RootElement.GetProperty("error").GetProperty("message").GetString() ?? "no message";
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return "no message";
        }
    }
}

/// <summary>
/// Stripe's webhook signature: <c>t={unix seconds},v1={hex HMAC-SHA256 of "{t}.{body}"}</c> with the endpoint's
/// signing secret. Old signatures are refused, so a captured webhook cannot be replayed much later.
/// </summary>
internal static class StripeSignature
{
    public const string Header = "Stripe-Signature";

    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static bool IsValid(string secret, string? header, string body, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(header))
        {
            return false;
        }

        long? timestamp = null;
        var signatures = new List<byte[]>();
        foreach (var part in header.Split(','))
        {
            var (key, value) = part.Split('=', 2) is [var k, var v] ? (k.Trim(), v.Trim()) : ("", "");
            if (key == "t" && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var t))
            {
                timestamp = t;
            }
            else if (key == "v1" && TryFromHex(value) is { } signature)
            {
                signatures.Add(signature);
            }
        }

        if (timestamp is not { } at || (now - DateTimeOffset.FromUnixTimeSeconds(at)).Duration() > Tolerance)
        {
            return false;
        }

        var expected = Mac(secret, at, body);
        return signatures.Any(s => CryptographicOperations.FixedTimeEquals(s, expected));
    }

    /// <summary>The header Stripe would send. For tests and local trials.</summary>
    public static string Sign(string secret, DateTimeOffset at, string body)
    {
        var timestamp = at.ToUnixTimeSeconds();
        return FormattableString.Invariant($"t={timestamp},v1={Convert.ToHexStringLower(Mac(secret, timestamp, body))}");
    }

    private static byte[] Mac(string secret, long timestamp, string body) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(FormattableString.Invariant($"{timestamp}.{body}")));

    private static byte[]? TryFromHex(string value)
    {
        try
        {
            return Convert.FromHexString(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
