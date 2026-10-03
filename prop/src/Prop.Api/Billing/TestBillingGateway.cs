namespace Prop.Api.Billing;

/// <summary>
/// Payments without money, for development: the checkout page is in the portal, and its test cards either pay or
/// decline, like Stripe's test cards. A saved card that declines makes every later charge fail, to try what
/// happens when a month is not paid.
/// </summary>
internal sealed class TestBillingGateway : IBillingGateway
{
    public const string PayingCard = "pm_test_pays";

    public const string DecliningCard = "pm_test_declines";

    public BillingProvider Provider => BillingProvider.Test;

    public static SavedCard Card(bool declines) =>
        declines ? new SavedCard(null, DecliningCard, "Test", "0002", 12, 2034) : new SavedCard(null, PayingCard, "Test", "4242", 12, 2034);

    public Task<CheckoutPage> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        var id = $"test_{Guid.NewGuid():N}";
        return Task.FromResult(new CheckoutPage(id, new Uri(request.PortalUrl, $"admin/billing/checkout/{id}")));
    }

    public Task ExpireCheckoutAsync(string checkoutId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ChargeOutcome> ChargeAsync(SavedCard card, ChargeToPay charge, int attempt, CancellationToken cancellationToken) =>
        Task.FromResult<ChargeOutcome>(
            card.PaymentMethodId == DecliningCard
                ? new ChargeOutcome.Declined("The test card was declined.", null)
                : new ChargeOutcome.Paid($"test_payment_{charge.Id:N}_{attempt}"));
}
