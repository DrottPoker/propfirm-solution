using System.Text.Json;

using Prop.Rules;

namespace Prop.Api.Payments;

/// <summary>
/// A purchase of a challenge in the firm's portal. <paramref name="AccountId"/> is the account the payment
/// started, and <paramref name="Problem"/> says why a paid order has none. <paramref name="RefundedAt"/> and
/// <paramref name="DisputedAt"/> are set when the provider reports a refund or a dispute. The account is not
/// touched then: the firm decides whether to cancel it.
/// </summary>
public sealed record OrderResponse(
    Guid Id,
    long Number,
    OrderStatus Status,
    string Email,
    string ChallengeId,
    decimal Amount,
    string Currency,
    PaymentProvider Provider,
    string? PaymentReference,
    Guid? AccountId,
    string? Problem,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? RefundedAt,
    DateTimeOffset? DisputedAt)
{
    internal static OrderResponse From(Order order) =>
        new(
            order.Id,
            order.Number,
            order.Status,
            order.Email,
            order.ChallengeId,
            order.Amount,
            order.Currency,
            order.Provider,
            order.PaymentReference,
            order.AccountId,
            order.Problem,
            order.CreatedAt,
            order.ExpiresAt,
            order.PaidAt,
            order.RefundedAt,
            order.DisputedAt);
}

/// <summary>Something that happened to an order. <paramref name="Detail"/> is the provider's message, when there is one.</summary>
public sealed record OrderEventResponse(string Type, DateTimeOffset RecordedAt, string Source, JsonElement? Detail);

/// <summary>An order with everything that happened to it, oldest first.</summary>
public sealed record OrderDetailsResponse(OrderResponse Order, IReadOnlyList<OrderEventResponse> Events)
{
    internal static OrderDetailsResponse From(Order order, IReadOnlyList<OrderEvent> events) =>
        new(
            OrderResponse.From(order),
            [.. events.Select(e => new OrderEventResponse(e.Type, e.RecordedAt, e.Source, e.Detail is null ? null : JsonSerializer.Deserialize<JsonElement>(e.Detail)))]);
}

/// <summary><paramref name="Reference"/> is the firm's own, for example its payment provider's id of the payment.</summary>
public sealed record MarkOrderRequest(string? Reference);

/// <summary>The price of a challenge in the portal, and whether it is sold there.</summary>
public sealed record PriceRequest(decimal Amount, string? Currency, bool ForSale);

/// <summary>
/// What the firm's portal sells. <paramref name="Open"/> is false when the firm takes no payment or sells nothing.
/// <paramref name="TermsUrl"/> is the firm's terms, which the buyer accepts. <paramref name="Test"/> means no money is taken.
/// </summary>
public sealed record ShopResponse(bool Open, bool Test, Uri? TermsUrl, IReadOnlyList<ShopItemResponse> Items);

public sealed record ShopItemResponse(ChallengeDefinition Challenge, decimal Price, string Currency);

/// <summary>
/// Buys the challenge. A logged-in trader buys with their own email, and <paramref name="Email"/> is then not
/// used. <paramref name="AcceptTerms"/> is needed when the firm has terms.
/// </summary>
public sealed record CreateOrderRequest(string? ChallengeId, string? Email, bool AcceptTerms);

/// <summary>The new order, and where the buyer pays. The buyer comes back to the order's page afterwards.</summary>
public sealed record CreatedOrderResponse(Guid OrderId, long Number, Uri CheckoutUrl);

/// <summary>The token from the buyer's link to the order.</summary>
public sealed record OrderTokenRequest(string? Token);

/// <summary>
/// The order as its buyer sees it. <paramref name="CheckoutUrl"/> is set while it waits for payment.
/// <paramref name="CanLogIn"/> means the trader already has a password for the portal.
/// <paramref name="InviteSentAt"/> is when the platform last emailed an invitation to choose one.
/// </summary>
public sealed record BuyerOrderResponse(
    Guid Id,
    long Number,
    OrderStatus Status,
    string Email,
    string ChallengeId,
    string ChallengeName,
    decimal Amount,
    string Currency,
    PaymentProvider Provider,
    Uri? CheckoutUrl,
    Guid? AccountId,
    string? Problem,
    bool CanLogIn,
    DateTimeOffset? InviteSentAt);

/// <summary>
/// How the firm's portal takes payment. <paramref name="Provider"/> is what the firm chose, and
/// <paramref name="Active"/> whether it works now: Stripe's live keys work only once the firm is live.
/// <paramref name="StripeWebhookUrl"/> is where the firm points Stripe's webhook.
/// </summary>
public sealed record PaymentSettingsResponse(
    PaymentProvider? Provider,
    bool Active,
    bool TestPaymentsAllowed,
    bool HasStripeKeys,
    bool? StripeTestMode,
    Uri StripeWebhookUrl,
    Uri? CheckoutUrl,
    Uri? TermsUrl);

/// <summary>
/// The firm's choice of payment provider. The Stripe keys are kept when they are left empty, and are never shown
/// again. <paramref name="CheckoutUrl"/> is the firm's own checkout page, for External. Empty URLs mean none.
/// </summary>
public sealed record PaymentSettingsRequest(PaymentProvider? Provider, string? StripeSecretKey, string? StripeWebhookSecret, string? CheckoutUrl, string? TermsUrl);
