using System.Text.Json;

using Prop.Rules;

namespace Prop.Api.Payments;

/// <summary>
/// A purchase of a challenge in the firm's portal, with the buyer's name and country as given in the shop.
/// <paramref name="AccountId"/> is the account the payment started, and <paramref name="Problem"/> says why a paid order has none. <paramref name="RefundedAt"/> and
/// <paramref name="DisputedAt"/> are set when the provider reports a refund or a dispute. The account is not
/// touched then: the firm decides whether to cancel it. With a <paramref name="DiscountCode"/>, <paramref name="ListAmount"/>
/// is the price before it and <paramref name="Amount"/> what the buyer pays.
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
    DateTimeOffset? DisputedAt,
    string? BuyerName,
    string? BuyerCountry,
    string? DiscountCode = null,
    decimal? ListAmount = null)
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
            order.DisputedAt,
            order.BuyerName,
            order.BuyerCountry,
            order.DiscountCode,
            order.ListAmount);
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
/// What the firm's portal sells. <paramref name="Open"/> is false when the firm takes no payment, sells nothing or
/// cannot start more challenges now, which <paramref name="Full"/> says. <paramref name="TermsUrl"/> is the firm's
/// terms, which the buyer accepts. <paramref name="Test"/> means no money is taken.
/// </summary>
public sealed record ShopResponse(bool Open, bool Full, bool Test, Uri? TermsUrl, IReadOnlyList<ShopItemResponse> Items);

public sealed record ShopItemResponse(ChallengeDefinition Challenge, decimal Price, string Currency);

/// <summary>
/// Buys the challenge. A logged-in trader buys with their own email, and <paramref name="Email"/> is then not
/// used. <paramref name="Name"/> and <paramref name="Country"/>, a two-letter code, are the buyer's; a logged-in trader
/// who gave them before need not again. <paramref name="AcceptTerms"/> is needed when the firm has terms.
/// <paramref name="DiscountCode"/> is one of the firm's codes, or empty for none.
/// </summary>
public sealed record CreateOrderRequest(
    string? ChallengeId,
    string? Email,
    bool AcceptTerms,
    string? Name = null,
    string? Country = null,
    string? DiscountCode = null);

/// <summary>A discount code the buyer typed, for the challenge, and the buyer's email when it is known yet.</summary>
public sealed record DiscountQuoteRequest(string? Code, string? ChallengeId, string? Email = null);

/// <summary>
/// What the challenge costs with the code: <paramref name="ListAmount"/> before it, <paramref name="Discount"/> off and
/// <paramref name="Amount"/> to pay. <paramref name="ForRetries"/> means only a buyer whose earlier challenge at the firm
/// failed can use it.
/// </summary>
public sealed record DiscountQuoteResponse(string Code, string ChallengeId, decimal ListAmount, decimal Discount, decimal Amount, string Currency, bool ForRetries);

/// <summary>
/// A new discount code. <paramref name="PercentOff"/> or <paramref name="AmountOff"/> in <paramref name="Currency"/>,
/// for the challenges in <paramref name="ChallengeIds"/> or every one when empty, at most <paramref name="MaxUses"/>
/// times and until <paramref name="ExpiresAt"/> when given. <paramref name="ForRetries"/> keeps it for buyers whose
/// earlier challenge at the firm failed.
/// </summary>
public sealed record DiscountCodeRequest(
    string? Code,
    decimal? PercentOff,
    decimal? AmountOff,
    string? Currency,
    IReadOnlyList<string>? ChallengeIds,
    int? MaxUses,
    DateTimeOffset? ExpiresAt,
    bool ForRetries);

/// <summary>A discount code and how often it was used. <paramref name="Uses"/> counts paid orders and orders waiting for payment.</summary>
public sealed record DiscountCodeResponse(
    Guid Id,
    string Code,
    decimal? PercentOff,
    decimal? AmountOff,
    string? Currency,
    IReadOnlyList<string>? ChallengeIds,
    int? MaxUses,
    int Uses,
    DateTimeOffset? ExpiresAt,
    bool ForRetries,
    bool Active,
    DateTimeOffset CreatedAt)
{
    internal static DiscountCodeResponse From(DiscountCode code) =>
        new(code.Id, code.Code, code.PercentOff, code.AmountOff, code.Currency, code.ChallengeIds, code.MaxUses, code.Uses, code.ExpiresAt, code.ForRetries, code.Active, code.CreatedAt);
}

/// <summary>Turns a discount code on or off.</summary>
public sealed record DiscountActiveRequest(bool Active);

/// <summary>The new order, and where the buyer pays. The buyer comes back to the order's page afterwards.</summary>
public sealed record CreatedOrderResponse(Guid OrderId, long Number, Uri CheckoutUrl);

/// <summary>The token from the buyer's link to the order.</summary>
public sealed record OrderTokenRequest(string? Token);

/// <summary>
/// The order as its buyer sees it, with the price before its <paramref name="DiscountCode"/> as <paramref name="ListAmount"/>.
/// <paramref name="CheckoutUrl"/> is set while it waits for payment.
/// <paramref name="CanLogIn"/> means the trader already has a password for the portal.
/// <paramref name="InviteSentAt"/> is when the platform last emailed an invitation to choose one.
/// <paramref name="CanChoosePassword"/> means the buyer can choose the password right on the order's page: the order
/// started the trader's only account, and the trader has no password yet.
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
    DateTimeOffset? InviteSentAt,
    bool CanChoosePassword,
    string? DiscountCode = null,
    decimal? ListAmount = null);

/// <summary>The password the buyer chooses on the order's page, with the token from the link to the order.</summary>
public sealed record OrderPasswordRequest(string? Token, string? Password);

/// <summary>
/// How the firm's portal takes payment. <paramref name="Provider"/> is what the firm chose, and
/// <paramref name="Active"/> whether it works now: Stripe's live keys work only once the firm is live, and its test
/// keys only where test payments are allowed. <paramref name="StripeWebhookUrl"/> and <paramref name="StripeWebhookEvents"/>
/// are the webhook we set up in the firm's Stripe account, or that the firm adds itself.
/// </summary>
public sealed record PaymentSettingsResponse(
    PaymentProvider? Provider,
    bool Active,
    bool TestPaymentsAllowed,
    bool HasStripeKeys,
    bool? StripeTestMode,
    Uri StripeWebhookUrl,
    IReadOnlyList<string> StripeWebhookEvents,
    Uri? CheckoutUrl,
    Uri? TermsUrl);

/// <summary>
/// The firm's choice of payment provider. The Stripe keys are kept when they are left empty, and are never shown
/// again. With only <paramref name="StripeSecretKey"/>, we set up the webhook in the firm's Stripe account; with
/// <paramref name="StripeWebhookSecret"/> too, the firm added it itself. <paramref name="CheckoutUrl"/> is the firm's
/// own checkout page, for External. Empty URLs mean none.
/// </summary>
public sealed record PaymentSettingsRequest(PaymentProvider? Provider, string? StripeSecretKey, string? StripeWebhookSecret, string? CheckoutUrl, string? TermsUrl);
