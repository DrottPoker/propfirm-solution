using System.Text.RegularExpressions;

namespace Prop.Api.Payments;

/// <summary>Who takes the trader's money when a challenge is bought in the firm's portal (ADR 0019).</summary>
public enum PaymentProvider
{
    /// <summary>A page in the portal that pays without money, for firms in the sandbox.</summary>
    Test,

    /// <summary>Stripe Checkout with the firm's own Stripe account.</summary>
    Stripe,

    /// <summary>The firm's own checkout page, which tells the firm API when the order is paid.</summary>
    External,
}

/// <summary>
/// How the firm's portal takes payment. No provider means no shop. Stripe's keys are kept when the firm picks
/// another provider, so it can switch back.
/// </summary>
internal sealed record FirmPayments(PaymentProvider? Provider, StripeKeys? Stripe, Uri? CheckoutUrl, Uri? TermsUrl)
{
    public static readonly FirmPayments None = new(null, null, null, null);
}

/// <summary>The firm's own Stripe keys: the secret key that creates checkouts, and the secret that signs Stripe's webhooks.</summary>
internal sealed record StripeKeys(string SecretKey, string WebhookSecret)
{
    /// <summary>A test key never moves real money.</summary>
    public bool IsTestMode => StripeKeyRules.IsTestKey(SecretKey);
}

internal static partial class StripeKeyRules
{
    /// <summary>A secret or restricted key, in test or live mode.</summary>
    public static bool IsValidSecretKey(string? key) => key is not null && SecretKey().IsMatch(key);

    public static bool IsValidWebhookSecret(string? secret) => secret is not null && WebhookSecret().IsMatch(secret);

    public static bool IsTestKey(string key) => key.StartsWith("sk_test_", StringComparison.Ordinal) || key.StartsWith("rk_test_", StringComparison.Ordinal);

    [GeneratedRegex("^(sk|rk)_(test|live)_[A-Za-z0-9]{10,}$")]
    private static partial Regex SecretKey();

    [GeneratedRegex("^whsec_[A-Za-z0-9+/=]{10,}$")]
    private static partial Regex WebhookSecret();
}
