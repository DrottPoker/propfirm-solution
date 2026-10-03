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

/// <summary>
/// Creates Stripe Checkout Sessions with the firm's own secret key, so the money goes straight to the firm. The
/// buyer pays on Stripe's page, and the card never reaches us.
/// </summary>
internal sealed class StripeClient(IHttpClientFactory httpClients)
{
    public const string HttpClientName = "Stripe";

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
