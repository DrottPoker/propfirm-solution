using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.Extensions.Options;

using Prop.Api.Configuration;
using Prop.Api.Payments;

namespace Prop.Api.Billing;

/// <summary>
/// The firms' payments to us through our own Stripe account. The firm pays the first time on a Stripe Checkout
/// page, which saves the card on a Stripe customer for later charges without the firm. A new card is saved the
/// same way. The card number never reaches us.
/// </summary>
internal sealed partial class StripeBillingGateway(IHttpClientFactory httpClients, IOptions<BillingOptions> options, ILogger<StripeBillingGateway> logger)
    : IBillingGateway
{
    /// <summary>Marks our own charges of a saved card, so their webhooks are told apart from checkout payments.</summary>
    public const string CardOnFile = "card_on_file";

    public BillingProvider Provider => BillingProvider.Stripe;

    public async Task<CheckoutPage> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        List<KeyValuePair<string, string>> form =
        [
            new("client_reference_id", request.FirmId),
            new("metadata[firm_id]", request.FirmId),
            new("metadata[purpose]", request.Purpose.ToString()),
            new("success_url", new Uri(request.PortalUrl, $"{request.ReturnPath}?checkout={{CHECKOUT_SESSION_ID}}").ToString()),
            new("cancel_url", new Uri(request.PortalUrl, request.ReturnPath).ToString()),
            new("expires_at", request.ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        ];
        if (request.CustomerId is { } customerId)
        {
            form.Add(new("customer", customerId));
        }
        else
        {
            form.Add(new("customer_creation", "always"));
            form.Add(new("customer_email", request.Email));
        }

        if (request.Charge is { } charge)
        {
            // Only cards, since a saved card is what each month is charged to.
            form.Add(new("mode", "payment"));
            form.Add(new("payment_method_types[0]", "card"));
            form.Add(new("metadata[charge_id]", charge.Id.ToString()));
            form.Add(new("payment_intent_data[setup_future_usage]", "off_session"));
            form.Add(new("payment_intent_data[description]", charge.Description));
            form.Add(new("payment_intent_data[metadata][charge_id]", charge.Id.ToString()));
            foreach (var (line, i) in charge.Lines.Select((l, i) => (l, i)))
            {
                form.Add(new($"line_items[{i}][quantity]", "1"));
                form.Add(new($"line_items[{i}][price_data][currency]", charge.Currency.ToLowerInvariant()));
                form.Add(new($"line_items[{i}][price_data][unit_amount]", StripeClient.MinorUnits(line.Amount)));
                form.Add(new($"line_items[{i}][price_data][product_data][name]", line.Description));
            }
        }
        else
        {
            form.Add(new("mode", "setup"));
            form.Add(new("payment_method_types[0]", "card"));
            form.Add(new("setup_intent_data[metadata][firm_id]", request.FirmId));
        }

        try
        {
            using var session = await SendAsync(HttpMethod.Post, "v1/checkout/sessions", form, $"billing-checkout-{Guid.NewGuid()}", cancellationToken);
            return new CheckoutPage(session.RootElement.GetProperty("id").GetString()!, new Uri(session.RootElement.GetProperty("url").GetString()!));
        }
        catch (StripeRefusedException refused)
        {
            throw new BillingProviderUnavailableException($"Stripe refused the checkout: {refused.Message}", refused);
        }
    }

    public async Task ExpireCheckoutAsync(string checkoutId, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendAsync(HttpMethod.Post, $"v1/checkout/sessions/{Uri.EscapeDataString(checkoutId)}/expire", [], null, cancellationToken);
        }
        catch (Exception exception) when (exception is BillingProviderUnavailableException or StripeRefusedException)
        {
            // The page expires by itself, and a payment on it still counts.
            LogNotExpired(logger, checkoutId, exception);
        }
    }

    public async Task<ChargeOutcome> ChargeAsync(SavedCard card, ChargeToPay charge, int attempt, CancellationToken cancellationToken)
    {
        List<KeyValuePair<string, string>> form =
        [
            new("amount", StripeClient.MinorUnits(charge.Amount)),
            new("currency", charge.Currency.ToLowerInvariant()),
            new("customer", card.CustomerId ?? ""),
            new("payment_method", card.PaymentMethodId),
            new("off_session", "true"),
            new("confirm", "true"),
            new("description", charge.Description),
            new("metadata[charge_id]", charge.Id.ToString()),
            new("metadata[source]", CardOnFile),
        ];
        try
        {
            using var intent = await SendAsync(HttpMethod.Post, "v1/payment_intents", form, $"charge-{charge.Id}-{attempt}-{card.PaymentMethodId}", cancellationToken);
            var root = intent.RootElement;
            var id = root.GetProperty("id").GetString()!;
            return root.GetProperty("status").GetString() is "succeeded" or "processing"
                ? new ChargeOutcome.Paid(id)
                : new ChargeOutcome.Declined("The card needs you to confirm the payment. Pay it on the payment page.", id);
        }
        catch (StripeRefusedException refused)
        {
            return new ChargeOutcome.Declined(refused.Message, refused.PaymentIntent);
        }
    }

    /// <summary>The card a completed payment was made with, which is saved for later charges.</summary>
    public async Task<(SavedCard Card, decimal Amount, string Currency)> ReadPaymentAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        using var intent = await SendAsync(HttpMethod.Get, $"v1/payment_intents/{Uri.EscapeDataString(paymentIntentId)}?expand[]=payment_method", null, null, cancellationToken);
        var root = intent.RootElement;
        return (
            CardOf(root),
            StripeClient.FromMinorUnits(root.GetProperty("amount").GetInt64()),
            root.GetProperty("currency").GetString()!.ToUpperInvariant());
    }

    /// <summary>The card saved on a completed checkout page in setup mode.</summary>
    public async Task<SavedCard> ReadSetupAsync(string setupIntentId, CancellationToken cancellationToken)
    {
        using var intent = await SendAsync(HttpMethod.Get, $"v1/setup_intents/{Uri.EscapeDataString(setupIntentId)}?expand[]=payment_method", null, null, cancellationToken);
        return CardOf(intent.RootElement);
    }

    private static SavedCard CardOf(JsonElement intent)
    {
        var method = intent.GetProperty("payment_method");
        var card = method.GetProperty("card");
        return new SavedCard(
            intent.TryGetProperty("customer", out var customer) && customer.ValueKind == JsonValueKind.String ? customer.GetString() : null,
            method.GetProperty("id").GetString()!,
            card.GetProperty("brand").GetString()!,
            card.GetProperty("last4").GetString()!,
            card.GetProperty("exp_month").GetInt32(),
            card.GetProperty("exp_year").GetInt32());
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string path,
        IEnumerable<KeyValuePair<string, string>>? form,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.StripeSecretKey);
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        try
        {
            using var response = await httpClients.CreateClient(StripeClient.HttpClientName).SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            // Stripe keeps its answer to a request with an idempotency key, also an error, but not a refusal for too many requests.
            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new BillingProviderUnavailableException($"Stripe answered {(int)response.StatusCode}.") { Answered = (int)response.StatusCode >= 500 };
            }

            var document = JsonDocument.Parse(body);
            if (!response.IsSuccessStatusCode)
            {
                using (document)
                {
                    throw StripeRefusedException.From(response.StatusCode, document.RootElement);
                }
            }

            return document;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderUnavailableException("Stripe could not be reached.", exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stripe checkout {CheckoutId} could not be expired; it expires by itself")]
    private static partial void LogNotExpired(ILogger logger, string checkoutId, Exception exception);
}

/// <summary>Stripe refused the request, for example a declined card. The message is Stripe's own and safe to show the firm.</summary>
internal sealed class StripeRefusedException : Exception
{
    public StripeRefusedException()
    {
    }

    public StripeRefusedException(string message)
        : base(message)
    {
    }

    public StripeRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    private StripeRefusedException(string message, string? paymentIntent)
        : base(message)
    {
        PaymentIntent = paymentIntent;
    }

    /// <summary>The payment that was declined, when Stripe made one.</summary>
    public string? PaymentIntent { get; }

    public static StripeRefusedException From(HttpStatusCode status, JsonElement body)
    {
        var error = body.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object ? e : default;
        var message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()!
            : $"Stripe refused the request ({(int)status}).";
        var paymentIntent = error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("payment_intent", out var intent)
            && intent.ValueKind == JsonValueKind.Object
            && intent.TryGetProperty("id", out var id)
                ? id.GetString()
                : null;
        return new StripeRefusedException(message, paymentIntent);
    }
}
