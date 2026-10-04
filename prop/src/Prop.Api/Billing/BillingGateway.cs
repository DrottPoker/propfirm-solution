namespace Prop.Api.Billing;

/// <summary>Who takes the firm's payments to us.</summary>
public enum BillingProvider
{
    /// <summary>A page in the portal that pays without money, with test cards that pay or decline. For development only.</summary>
    Test,

    /// <summary>Stripe with our own account. The card is saved there and charged in advance each month.</summary>
    Stripe,
}

/// <summary>What a checkout page is for: paying a charge, which also saves the card, or only saving a new card.</summary>
public enum CheckoutPurpose
{
    Payment,
    Card,
}

/// <summary>A card saved at the provider, as the admin panel shows it. The number itself is never seen by us.</summary>
internal sealed record SavedCard(string? CustomerId, string PaymentMethodId, string Brand, string Last4, int ExpMonth, int ExpYear);

/// <summary>
/// A checkout page to make for the firm. <paramref name="Charge"/> is what it pays, for a payment. The firm's
/// customer at the provider is reused when it has one. <paramref name="ReturnPath"/> is the page in the firm's
/// admin panel the firm comes back to.
/// </summary>
internal sealed record CheckoutRequest(
    string FirmId,
    string Email,
    CheckoutPurpose Purpose,
    ChargeToPay? Charge,
    string? CustomerId,
    Uri PortalUrl,
    string ReturnPath,
    DateTimeOffset ExpiresAt);

/// <summary>A charge as the provider needs it: its id, a description and the total.</summary>
internal sealed record ChargeToPay(Guid Id, long Number, string Description, IReadOnlyList<ChargeLine> Lines, decimal Amount, string Currency);

/// <summary>The checkout page: the provider's id for it and where the firm goes to pay.</summary>
internal sealed record CheckoutPage(string Id, Uri Url);

/// <summary>What happened when the saved card was charged.</summary>
internal abstract record ChargeOutcome
{
    /// <summary>The money was taken. <paramref name="Reference"/> is the provider's id of the payment.</summary>
    public sealed record Paid(string Reference) : ChargeOutcome;

    /// <summary>The card was declined or needs the firm to confirm the payment. Worth asking the firm about.</summary>
    public sealed record Declined(string Reason, string? Reference) : ChargeOutcome;
}

/// <summary>
/// The provider could not be reached or failed. Trying again later can work. <see cref="Answered"/> is set when the
/// provider answered with its own error: it may have kept that answer, so a new try must be a new request.
/// </summary>
internal sealed class BillingProviderUnavailableException : Exception
{
    public BillingProviderUnavailableException()
    {
    }

    public BillingProviderUnavailableException(string message)
        : base(message)
    {
    }

    public BillingProviderUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public bool Answered { get; init; }
}

/// <summary>
/// Takes the firms' payments to us: checkout pages where the firm pays or saves a card, and charges of the saved
/// card without the firm, each month in advance. One implementation per provider.
/// </summary>
internal interface IBillingGateway
{
    BillingProvider Provider { get; }

    Task<CheckoutPage> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>Closes a checkout page that is no longer wanted, so it cannot be paid. Best effort.</summary>
    Task ExpireCheckoutAsync(string checkoutId, CancellationToken cancellationToken);

    /// <summary>
    /// Charges the saved card for the charge without the firm present. <paramref name="attempt"/> and the card make
    /// each try its own payment, while a repeated call for the same try with the same card never charges twice.
    /// </summary>
    Task<ChargeOutcome> ChargeAsync(SavedCard card, ChargeToPay charge, int attempt, CancellationToken cancellationToken);
}
