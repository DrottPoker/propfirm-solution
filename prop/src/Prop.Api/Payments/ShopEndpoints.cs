using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Prop.Api.Api;
using Prop.Api.Billing;
using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Firms;
using Prop.Api.Portal;

namespace Prop.Api.Payments;

/// <summary>
/// Buying challenges in the firm's portal (ADR 0019): the shop and the buyer's order for anyone on the portal,
/// and the orders, prices and payment settings in the admin panel.
/// </summary>
internal static class ShopEndpoints
{
    /// <summary>Maps the shop on the portal, where the buyer may not be logged in.</summary>
    public static RouteGroupBuilder MapShop(this RouteGroupBuilder portal)
    {
        portal.MapGet("/shop", GetShopAsync);
        portal.MapPost("/shop/discount", QuoteDiscountAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/orders", CreateOrderAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapGet("/orders/{orderId:guid}", GetBuyerOrderAsync);
        portal.MapPost("/orders/{orderId:guid}/invite", ResendInviteAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/orders/{orderId:guid}/password", ChoosePasswordAsync).RequireRateLimiting(PortalAuth.LoginRateLimit);
        portal.MapPost("/orders/{orderId:guid}/test-payment", PayTestOrderAsync);
        return portal;
    }

    /// <summary>Maps the orders, prices and payment settings on the admin group.</summary>
    public static RouteGroupBuilder MapAdminOrders(this RouteGroupBuilder admin)
    {
        admin.MapGet("/orders", ListOrdersAsync);
        admin.MapGet("/orders/{orderId:guid}", (Guid orderId, HttpContext context, OrderStore orders, TimeProvider time, CancellationToken cancellationToken) =>
            OrderActions.GetAsync(PortalFirmFilter.FirmOf(context), orderId, orders, time, cancellationToken));
        admin.MapPost("/orders/{orderId:guid}/mark-paid", (Guid orderId, MarkOrderRequest request, HttpContext context, OrderService service, CancellationToken cancellationToken) =>
            OrderActions.MarkPaidAsync(PortalFirmFilter.FirmOf(context), orderId, request, OrderSources.Admin, service, cancellationToken));
        admin.MapPost(
            "/orders/{orderId:guid}/mark-refunded",
            (Guid orderId, MarkOrderRequest request, HttpContext context, OrderService service, OrderStore orders, TimeProvider time, CancellationToken cancellationToken) =>
                OrderActions.MarkRefundedAsync(PortalFirmFilter.FirmOf(context), orderId, request, OrderSources.Admin, service, orders, time, cancellationToken));
        admin.MapGet("/prices", (HttpContext context, PriceCatalog prices, CancellationToken cancellationToken) =>
            OrderActions.ListPricesAsync(PortalFirmFilter.FirmOf(context), prices, cancellationToken));
        admin.MapPut("/challenges/{challengeId}/price", (string challengeId, PriceRequest request, HttpContext context, PriceCatalog prices, CancellationToken cancellationToken) =>
            OrderActions.SavePriceAsync(PortalFirmFilter.FirmOf(context), challengeId, request, prices, cancellationToken));
        admin.MapPut("/firm/payments", SavePaymentsAsync);
        return admin;
    }

    /// <summary>The firm's payment settings as the admin panel shows them. Keys are never shown, only whether they are there.</summary>
    public static PaymentSettingsResponse SettingsOf(Firm firm, OrderService service, PlatformOptions platform)
    {
        var payments = firm.Payments;
        return new PaymentSettingsResponse(
            payments.Provider,
            service.ProviderOf(firm) is not null,
            service.TestPaymentsAllowed(firm),
            payments.Stripe is not null,
            payments.Stripe?.IsTestMode,
            WebhookUrlOf(firm, platform),
            StripeClient.WebhookEvents,
            payments.CheckoutUrl,
            payments.TermsUrl);
    }

    /// <summary>Where Stripe sends the firm's webhooks.</summary>
    private static Uri WebhookUrlOf(Firm firm, PlatformOptions platform) => new(platform.ApiUrl!, $"api/payments/v1/stripe/{firm.Id}");

    // A shop whose slots are all taken stops selling, so no buyer pays for a challenge that cannot start.
    private static async Task<Ok<ShopResponse>> GetShopAsync(HttpContext context, OrderService service, SlotService slots, CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var items = await service.ShopAsync(firm, cancellationToken);
        var hasRoom = items.Count > 0 && (await slots.UsageAsync(firm, cancellationToken)).HasRoom;
        return TypedResults.Ok(new ShopResponse(
            hasRoom,
            items.Count > 0 && !hasRoom,
            service.ProviderOf(firm) == PaymentProvider.Test,
            firm.Payments.TermsUrl,
            [.. items.Select(i => new ShopItemResponse(i.Challenge, i.Price.Amount, i.Price.Currency))]));
    }

    /// <summary>
    /// Makes an order and answers with where to pay. A trader logged in to the portal buys with their own
    /// email, so the new account shows up among theirs.
    /// </summary>
    private static async Task<Results<Created<CreatedOrderResponse>, ProblemHttpResult>> CreateOrderAsync(
        CreateOrderRequest request,
        HttpContext context,
        OrderService service,
        PortalUsers users,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var (email, name, country) = (request.Email, request.Name, request.Country);
        var session = await context.AuthenticateAsync(PortalAuth.TraderScheme);
        if (session.Principal is { } principal
            && principal.FindFirstValue(PortalAuth.FirmIdClaim) == firm.Id
            && await users.FindByIdAsync(PortalAuth.UserIdOf(principal), PortalRoles.Trader, cancellationToken) is { } trader)
        {
            (email, name, country) = (trader.Email, string.IsNullOrWhiteSpace(name) ? trader.Name : name, string.IsNullOrWhiteSpace(country) ? trader.Country : country);
        }

        return await service.CreateAsync(firm, new Buyer(email, name, country), request.ChallengeId, request.AcceptTerms, request.DiscountCode, cancellationToken) switch
        {
            NewOrder.Created created => TypedResults.Created(
                $"/api/portal/orders/{created.Order.Id}",
                new CreatedOrderResponse(created.Order.Id, created.Order.Number, created.Order.CheckoutUrl)),
            NewOrder.Refused refused => AccountActions.Problem(refused.StatusCode, refused.Problem),
            _ => throw new InvalidOperationException("Unknown order outcome."),
        };
    }

    /// <summary>
    /// What the challenge costs with the code the buyer typed. A logged-in trader's own email decides whether a code
    /// for retries can be used. Limited like logins, so codes cannot be guessed quickly.
    /// </summary>
    private static async Task<Results<Ok<DiscountQuoteResponse>, ProblemHttpResult>> QuoteDiscountAsync(
        DiscountQuoteRequest request,
        HttpContext context,
        OrderService service,
        PortalUsers users,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var email = request.Email;
        var session = await context.AuthenticateAsync(PortalAuth.TraderScheme);
        if (session.Principal is { } principal
            && principal.FindFirstValue(PortalAuth.FirmIdClaim) == firm.Id
            && await users.FindByIdAsync(PortalAuth.UserIdOf(principal), PortalRoles.Trader, cancellationToken) is { } trader)
        {
            email = trader.Email;
        }

        var (price, code, refusal) = await service.QuoteDiscountAsync(firm, request.ChallengeId, request.Code, email, cancellationToken);
        return price is not null && code is not null
            ? TypedResults.Ok(new DiscountQuoteResponse(code.Code, request.ChallengeId!, price.ListAmount, price.Discount, price.Amount, price.Currency, code.ForRetries))
            : AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, refusal ?? "There is no such code.");
    }

    /// <summary>The order for whoever has the link to it, which the provider sends the buyer back to.</summary>
    private static async Task<Results<Ok<BuyerOrderResponse>, ProblemHttpResult>> GetBuyerOrderAsync(
        Guid orderId,
        string? token,
        HttpContext context,
        OrderStore orders,
        ChallengeCatalog challenges,
        PortalUsers users,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        return await BuyerOrderAsync(firm, orderId, token, orders, time, cancellationToken) is { } order
            ? TypedResults.Ok(await BuyerViewAsync(firm, order, challenges, users, cancellationToken))
            : UnknownOrder();
    }

    /// <summary>Emails the invitation to the portal again, for a buyer who did not get it.</summary>
    private static async Task<Results<Accepted, ProblemHttpResult>> ResendInviteAsync(
        Guid orderId,
        OrderTokenRequest request,
        HttpContext context,
        OrderService service,
        OrderStore orders,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        if (await BuyerOrderAsync(firm, orderId, request.Token, orders, time, cancellationToken) is not { } order)
        {
            return UnknownOrder();
        }

        return await service.SendInviteAsync(firm, order, OrderSources.Buyer, cancellationToken) switch
        {
            InviteOutcome.Sent => TypedResults.Accepted((string?)null),
            InviteOutcome.NotNeeded => AccountActions.Problem(StatusCodes.Status409Conflict, "You already have a password. Log in with it."),
            InviteOutcome.NotPaid => AccountActions.Problem(StatusCodes.Status409Conflict, "The order has not started a challenge yet."),
            InviteOutcome.TooSoon => AccountActions.Problem(StatusCodes.Status429TooManyRequests, "The email was just sent. Wait a minute before asking again."),
            _ => AccountActions.Problem(StatusCodes.Status503ServiceUnavailable, "The email could not be sent. Try again shortly."),
        };
    }

    /// <summary>
    /// The buyer chooses a password for the portal right on the order's page, and is logged in. Only when the order started
    /// the trader's only account and the trader has no password, so the page can never open accounts from before. The
    /// email is confirmed later, with the link the buyer was emailed.
    /// </summary>
    private static async Task<Results<Ok<PortalMeResponse>, ProblemHttpResult>> ChoosePasswordAsync(
        Guid orderId,
        OrderPasswordRequest request,
        HttpContext context,
        OrderStore orders,
        PortalUsers users,
        IPasswordHasher<PortalUser> hasher,
        IOptions<LoginOptions> login,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        if (await BuyerOrderAsync(firm, orderId, request.Token, orders, time, cancellationToken) is not { } order)
        {
            return UnknownOrder();
        }

        if (PasswordResetEndpoints.PasswordProblem(request.Password, login.Value) is { } problem)
        {
            return problem;
        }

        if (await users.FindTraderAsync(firm.Id, order.Email, cancellationToken) is not { } trader || !await CanChoosePasswordAsync(order, trader, users, cancellationToken))
        {
            return AccountActions.Problem(StatusCodes.Status409Conflict, "Use the link we emailed you to get into the portal.");
        }

        await users.SetTraderPasswordAsync(trader.Id, hasher.HashPassword(trader, request.Password!), time.GetUtcNow(), cancellationToken);

        // Read again, so the session carries the password's time as it is stored.
        var changed = (await users.FindByIdAsync(trader.Id, PortalRoles.Trader, cancellationToken))!;
        await PortalAuth.SignInAsync(context, changed);
        return TypedResults.Ok(PortalMeResponse.Of(changed, firm.Name));
    }

    /// <summary>Pays a test order without money, as the provider's message would. Only while the firm may take test payments.</summary>
    private static async Task<Results<Ok<BuyerOrderResponse>, ProblemHttpResult>> PayTestOrderAsync(
        Guid orderId,
        OrderTokenRequest request,
        HttpContext context,
        OrderService service,
        OrderStore orders,
        ChallengeCatalog challenges,
        PortalUsers users,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        if (await BuyerOrderAsync(firm, orderId, request.Token, orders, time, cancellationToken) is not { } order)
        {
            return UnknownOrder();
        }

        if (order.Provider != PaymentProvider.Test || !service.TestPaymentsAllowed(firm))
        {
            return AccountActions.Problem(StatusCodes.Status409Conflict, "This order cannot be paid with a test payment.");
        }

        if (order.Status == OrderStatus.Expired)
        {
            return AccountActions.Problem(StatusCodes.Status409Conflict, "The order has expired. Start a new one.");
        }

        var change = await service.MarkPaidAsync(firm, order.Id, new PaymentConfirmation(PaymentProvider.Test, OrderSources.Buyer, null, null, null, null), cancellationToken);
        return TypedResults.Ok(await BuyerViewAsync(firm, change.Order ?? order, challenges, users, cancellationToken));
    }

    private static Task<Results<Ok<List<OrderResponse>>, ProblemHttpResult>> ListOrdersAsync(
        HttpContext context,
        OrderStore orders,
        TimeProvider time,
        CancellationToken cancellationToken,
        OrderStatus? status = null,
        int limit = 100) =>
        OrderActions.ListAsync(PortalFirmFilter.FirmOf(context), status, limit, orders, time, cancellationToken);

    /// <summary>
    /// Chooses how the portal takes payment. Stripe's keys are checked and kept encrypted. Live keys can be saved in the
    /// sandbox, so the shop takes real money from the moment the firm goes live, but they take payment only from then.
    /// </summary>
    private static async Task<Results<Ok<FirmSettingsResponse>, ProblemHttpResult>> SavePaymentsAsync(
        PaymentSettingsRequest request,
        HttpContext context,
        FirmStore store,
        FirmCatalog firms,
        OrderService service,
        StripeClient stripe,
        IOptions<SandboxOptions> sandbox,
        IOptions<PlatformOptions> platform,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        if (!TryHttpsUrl(request.CheckoutUrl, out var checkoutUrl))
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "The checkout page must be an https address.");
        }

        if (!TryHttpsUrl(request.TermsUrl, out var termsUrl))
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "The terms must be an https address.");
        }

        // A secret key alone sets up the webhook in the firm's Stripe account. With a signing secret too, the firm set it up itself.
        StripeKeys? keys = null;
        if (!string.IsNullOrWhiteSpace(request.StripeSecretKey) || !string.IsNullOrWhiteSpace(request.StripeWebhookSecret))
        {
            var secretKey = request.StripeSecretKey?.Trim();
            var webhookSecret = request.StripeWebhookSecret?.Trim();
            if (!StripeKeyRules.IsValidSecretKey(secretKey))
            {
                return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "The Stripe secret key starts with sk_ or rk_.");
            }

            // Checked before the webhook is set up, so a refused key leaves nothing behind in Stripe.
            if (StripeKeyRules.IsTestKey(secretKey!) && !service.TestPaymentsAllowed(firm))
            {
                return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "Your firm is live, so use your live secret key (sk_live_). Test keys take no real money.");
            }

            if (string.IsNullOrEmpty(webhookSecret))
            {
                try
                {
                    webhookSecret = await stripe.SetUpWebhookAsync(secretKey!, WebhookUrlOf(firm, platform.Value), firm.Id, cancellationToken);
                }
                catch (StripeSetupException exception)
                {
                    return AccountActions.Problem(
                        StatusCodes.Status422UnprocessableEntity,
                        $"Stripe did not let us set up the webhook: {exception.Message} You can add the webhook in Stripe yourself and paste its signing secret.");
                }
            }
            else if (!StripeKeyRules.IsValidWebhookSecret(webhookSecret))
            {
                return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "The Stripe webhook signing secret starts with whsec_.");
            }

            keys = new StripeKeys(secretKey!, webhookSecret!);
        }

        var problem = request.Provider switch
        {
            PaymentProvider.Test when !service.TestPaymentsAllowed(firm) => "Test payments are only for the sandbox.",
            PaymentProvider.Stripe when (keys ?? firm.Payments.Stripe) is null => "Add your Stripe keys to take payments with Stripe.",
            PaymentProvider.External when checkoutUrl is null => "Add the address of your checkout page.",
            _ => null,
        };
        if (problem is not null)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, problem);
        }

        await store.SetPaymentsAsync(firm.Id, new FirmPayments(request.Provider, keys, checkoutUrl, termsUrl), time.GetUtcNow(), cancellationToken);
        var saved = await AdminSettingsEndpoints.ReloadAsync(firm, store, firms, cancellationToken);
        return TypedResults.Ok(FirmSettingsResponse.From(saved, sandbox.Value, platform.Value, SettingsOf(saved, service, platform.Value)));
    }

    // The firm's order, for whoever has the token from the buyer's link. Others get nothing, as if it did not exist.
    private static async Task<Order?> BuyerOrderAsync(Firm firm, Guid orderId, string? token, OrderStore orders, TimeProvider time, CancellationToken cancellationToken) =>
        await orders.GetAsync(firm.Id, orderId, time.GetUtcNow(), cancellationToken) is { } order && order.HasAccessToken(token) ? order : null;

    private static async Task<BuyerOrderResponse> BuyerViewAsync(Firm firm, Order order, ChallengeCatalog challenges, PortalUsers users, CancellationToken cancellationToken)
    {
        var challengeName = (await challenges.GetAsync(firm.Id, order.ChallengeId, cancellationToken))?.Name ?? order.ChallengeId;
        var trader = await users.FindTraderAsync(firm.Id, order.Email, cancellationToken);
        var canChoosePassword = trader is not null && await CanChoosePasswordAsync(order, trader, users, cancellationToken);
        return new BuyerOrderResponse(
            order.Id,
            order.Number,
            order.Status,
            order.Email,
            order.ChallengeId,
            challengeName,
            order.Amount,
            order.Currency,
            order.Provider,
            order.Status == OrderStatus.Pending ? order.CheckoutUrl : null,
            order.AccountId,
            order.Problem,
            trader?.PasswordHash is not null,
            order.InviteSentAt,
            canChoosePassword,
            order.DiscountCode,
            order.ListAmount);
    }

    // The order's page may set the password only for a trader who has nothing at the firm but the account the order paid for.
    private static async Task<bool> CanChoosePasswordAsync(Order order, PortalUser trader, PortalUsers users, CancellationToken cancellationToken) =>
        order is { Status: OrderStatus.Paid, AccountId: { } accountId }
        && trader.PasswordHash is null
        && await users.IsOnlyAccountAsync(trader.Id, accountId, cancellationToken);

    private static ProblemHttpResult UnknownOrder() => AccountActions.Problem(StatusCodes.Status404NotFound, "No such order.");

    // Empty means none. Anything else must be an absolute https address.
    private static bool TryHttpsUrl(string? value, out Uri? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out url) && url.Scheme == Uri.UriSchemeHttps;
    }
}
