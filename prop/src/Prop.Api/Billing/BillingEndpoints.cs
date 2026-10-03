using System.Security.Claims;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Prop.Api.Api;
using Prop.Api.Configuration;
using Prop.Api.Firms;
using Prop.Api.Payments;
using Prop.Api.Portal;

namespace Prop.Api.Billing;

/// <summary>
/// The firm's slots and its payments to us (ADR 0020): the admin panel's billing page, the test checkout page,
/// the firm API's slots and Stripe's webhook for our own payments.
/// </summary>
internal static partial class BillingEndpoints
{
    public const int ChargesShown = 24;

    /// <summary>Maps the billing on the admin group.</summary>
    public static RouteGroupBuilder MapAdminBilling(this RouteGroupBuilder admin)
    {
        admin.MapGet("/billing", GetAsync);
        admin.MapGet("/billing/quote", QuoteAsync);
        admin.MapPost("/billing/activate", ActivateAsync);
        admin.MapPut("/billing/slots", SetSlotsAsync);
        admin.MapPut("/billing/auto-expand", SetAutoExpandAsync);
        admin.MapPost("/billing/card", ChangeCardAsync);
        admin.MapPost("/billing/charges/{chargeId:guid}/checkout", PayChargeAsync);
        admin.MapPost("/billing/charges/{chargeId:guid}/retry", RetryChargeAsync);
        admin.MapGet("/billing/checkouts/{checkoutId}", GetTestCheckoutAsync);
        admin.MapPost("/billing/checkouts/{checkoutId}/complete", CompleteTestCheckoutAsync);
        return admin;
    }

    /// <summary>Maps the firm's slots on the firm API's group, so its own shop can stop selling when they run out.</summary>
    public static RouteGroupBuilder MapFirmSlots(this RouteGroupBuilder firm)
    {
        firm.MapGet("/slots", async (HttpContext context, SlotService slots, IOptions<BillingOptions> options, CancellationToken cancellationToken) =>
            TypedResults.Ok(SlotsResponse.From(await slots.UsageAsync(FirmApiKeyFilter.FirmOf(context), cancellationToken), options.Value.WarningPercent)));
        return firm;
    }

    public static IEndpointRouteBuilder MapBillingWebhooks(this IEndpointRouteBuilder app)
    {
        // Not part of the API anyone calls: Stripe does, signed with our own secret.
        app.MapPost("/api/payments/v1/billing/stripe", StripeWebhookAsync).ExcludeFromDescription();
        return app;
    }

    /// <summary>The firm's slots, card, charges and prices.</summary>
    private static async Task<Ok<BillingResponse>> GetAsync(
        HttpContext context,
        BillingService billing,
        BillingStore store,
        SlotService slots,
        IOptions<BillingOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await ViewAsync(PortalFirmFilter.FirmOf(context), billing, store, slots, options.Value, time, cancellationToken));

    /// <summary>What choosing this many slots would cost now and each month.</summary>
    private static async Task<Ok<QuoteResponse>> QuoteAsync(
        int slots,
        HttpContext context,
        BillingService billing,
        BillingStore store,
        SlotService slotService,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var terms = billing.Terms;
        var now = time.GetUtcNow();
        var monthly = BillingRules.MonthlyPrice(slots, terms.SlotPrices);
        var month = BillingRules.MonthOf(now);
        if (firm.Status != FirmStatus.Live)
        {
            var lines = BillingRules.Activation(now, slots, terms);
            var from = BillingRules.IsNextMonthDue(now, terms) ? month.AddMonths(2) : month.AddMonths(1);
            return Quote(QuoteKind.Activation, slots, lines, monthly, from, terms, billing.GoLiveProblem(firm) ?? billing.SlotsProblem(slots));
        }

        await using var connection = await store.OpenAsync(cancellationToken);
        var plan = await BillingStore.GetBillingAsync(connection, firm.Id, forUpdate: false, cancellationToken);
        if (plan is not { Plan: BillingPlan.Paid })
        {
            return Quote(QuoteKind.Unchanged, slots, [], monthly, null, terms, "The firm's slots are complimentary.");
        }

        var usage = await slotService.UsageAsync(connection, firm, null, cancellationToken);
        var current = usage.Slots ?? 0;
        var nextPaid = await BillingStore.GetPeriodAsync(connection, firm.Id, month.AddMonths(1), cancellationToken);

        // A new monthly price applies from the first month that is not paid or charged yet.
        var nextUnpaid = await BillingStore.FirstUnchargedMonthAsync(connection, firm.Id, month.AddMonths(1), cancellationToken);
        if (slots > current)
        {
            return Quote(
                QuoteKind.MoreSlots,
                slots,
                BillingRules.MoreSlots(now, current, slots, nextPaid?.Slots, terms),
                monthly,
                nextUnpaid,
                terms,
                billing.SlotsProblem(slots) ?? (usage.Paid ? null : "This month is not paid yet. Pay it first, then change your slots."));
        }

        var taken = usage.Used + usage.Reserved;
        var problem = billing.SlotsProblem(slots)
            ?? (slots < taken ? FormattableString.Invariant($"{taken} slots are taken by open challenges and orders. Choose at least that many.") : null);
        return Quote(slots == current && slots == plan.Slots ? QuoteKind.Unchanged : QuoteKind.FewerSlots, slots, [], monthly, nextUnpaid, terms, problem);
    }

    /// <summary>Starts going live: the firm pays the startup fee and its first month on a checkout page, which also saves its card.</summary>
    private static async Task<Results<Ok<CheckoutResponse>, ProblemHttpResult>> ActivateAsync(
        ActivateRequest request,
        HttpContext context,
        ClaimsPrincipal principal,
        BillingService billing,
        CancellationToken cancellationToken) =>
        CheckoutOf(await billing.StartActivationAsync(PortalFirmFilter.FirmOf(context), request.Slots, request.AutoExpandStep, EmailOf(principal), cancellationToken));

    /// <summary>More slots are charged to the card now; fewer apply from the next unpaid month.</summary>
    private static async Task<Results<Ok<BillingResponse>, ProblemHttpResult>> SetSlotsAsync(
        SlotsRequest request,
        HttpContext context,
        BillingService billing,
        BillingStore store,
        SlotService slots,
        IOptions<BillingOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        return await billing.ChangeSlotsAsync(firm, request.Slots, cancellationToken) is BillingResult.Refused refused
            ? AccountActions.Problem(refused.StatusCode, refused.Problem)
            : TypedResults.Ok(await ViewAsync(firm, billing, store, slots, options.Value, time, cancellationToken));
    }

    private static async Task<Results<Ok<BillingResponse>, ProblemHttpResult>> SetAutoExpandAsync(
        AutoExpandRequest request,
        HttpContext context,
        BillingService billing,
        BillingStore store,
        SlotService slots,
        IOptions<BillingOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        return await billing.SetAutoExpandAsync(firm, request.Step, cancellationToken) is BillingResult.Refused refused
            ? AccountActions.Problem(refused.StatusCode, refused.Problem)
            : TypedResults.Ok(await ViewAsync(firm, billing, store, slots, options.Value, time, cancellationToken));
    }

    /// <summary>A checkout page that saves a new card. Open monthly charges are tried with it at once.</summary>
    private static async Task<Results<Ok<CheckoutResponse>, ProblemHttpResult>> ChangeCardAsync(
        HttpContext context,
        ClaimsPrincipal principal,
        BillingService billing,
        CancellationToken cancellationToken) =>
        CheckoutOf(await billing.StartCardChangeAsync(PortalFirmFilter.FirmOf(context), EmailOf(principal), cancellationToken));

    /// <summary>A checkout page where the firm pays an unpaid monthly charge, with any card.</summary>
    private static async Task<Results<Ok<CheckoutResponse>, ProblemHttpResult>> PayChargeAsync(
        Guid chargeId,
        HttpContext context,
        ClaimsPrincipal principal,
        BillingService billing,
        CancellationToken cancellationToken) =>
        CheckoutOf(await billing.StartChargePaymentAsync(PortalFirmFilter.FirmOf(context), chargeId, EmailOf(principal), cancellationToken));

    /// <summary>Tries an unpaid monthly charge on the saved card now.</summary>
    private static async Task<Results<Ok<BillingResponse>, ProblemHttpResult>> RetryChargeAsync(
        Guid chargeId,
        HttpContext context,
        BillingService billing,
        BillingStore store,
        SlotService slots,
        IOptions<BillingOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        return await billing.RetryChargeAsync(firm, chargeId, cancellationToken) is BillingResult.Refused refused
            ? AccountActions.Problem(refused.StatusCode, refused.Problem)
            : TypedResults.Ok(await ViewAsync(firm, billing, store, slots, options.Value, time, cancellationToken));
    }

    /// <summary>A test checkout page of the firm, while payments to us are test payments.</summary>
    private static async Task<Results<Ok<TestCheckoutResponse>, ProblemHttpResult>> GetTestCheckoutAsync(
        string checkoutId,
        HttpContext context,
        BillingStore store,
        BillingService billing,
        CancellationToken cancellationToken)
    {
        if (await TestCheckoutAsync(PortalFirmFilter.FirmOf(context), checkoutId, store, billing, cancellationToken) is not { } checkout)
        {
            return UnknownCheckout();
        }

        await using var connection = await store.OpenAsync(cancellationToken);
        var charge = checkout.ChargeId is { } chargeId ? await BillingStore.GetChargeAsync(connection, chargeId, forUpdate: false, cancellationToken) : null;
        return TypedResults.Ok(new TestCheckoutResponse(
            checkout.Id,
            checkout.Purpose,
            checkout.Status,
            charge?.Lines ?? [],
            charge?.Amount ?? 0m,
            charge?.Currency ?? billing.Terms.Currency,
            checkout.ExpiresAt));
    }

    /// <summary>
    /// Pays or saves a card on a test checkout page, as the provider's message would. A test card that declines a
    /// payment leaves the page open, as Stripe's does; one saved as the card makes later charges fail.
    /// </summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> CompleteTestCheckoutAsync(
        string checkoutId,
        TestCheckoutRequest request,
        HttpContext context,
        BillingStore store,
        BillingService billing,
        CancellationToken cancellationToken)
    {
        if (await TestCheckoutAsync(PortalFirmFilter.FirmOf(context), checkoutId, store, billing, cancellationToken) is not { } checkout)
        {
            return UnknownCheckout();
        }

        if (checkout.Status != CheckoutStatus.Open)
        {
            return AccountActions.Problem(StatusCodes.Status409Conflict, "This page is no longer open. Start again from the billing page.");
        }

        CheckoutCompletion completion;
        if (checkout.Purpose == CheckoutPurpose.Payment)
        {
            if (request.Declines)
            {
                return AccountActions.Problem(StatusCodes.Status402PaymentRequired, "The test card was declined.");
            }

            await using var connection = await store.OpenAsync(cancellationToken);
            var charge = (await BillingStore.GetChargeAsync(connection, checkout.ChargeId!.Value, forUpdate: false, cancellationToken))!;
            completion = new CheckoutCompletion(TestBillingGateway.Card(declines: false), $"test_payment_{checkout.Id}", charge.Amount, charge.Currency, BillingSources.Test, null);
        }
        else
        {
            completion = new CheckoutCompletion(TestBillingGateway.Card(request.Declines), null, null, null, BillingSources.Test, null);
        }

        return await billing.CompleteCheckoutAsync(checkout.Id, completion, cancellationToken) == CompletionOutcome.AmountMismatch
            ? AccountActions.Problem(StatusCodes.Status409Conflict, "The payment does not match the charge.")
            : TypedResults.NoContent();
    }

    /// <summary>
    /// A Stripe event about our own payments. Only a valid signature with our webhook secret is accepted. Other
    /// events are answered with 200 and ignored, so Stripe does not send them again.
    /// </summary>
    private static async Task<IResult> StripeWebhookAsync(
        HttpContext context,
        IBillingGateway gateway,
        BillingService billing,
        IOptions<BillingOptions> options,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (gateway is not StripeBillingGateway stripe || options.Value.StripeWebhookSecret.Length == 0)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Payments to the platform do not go through Stripe here.");
        }

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);
        if (!StripeSignature.IsValid(options.Value.StripeWebhookSecret, context.Request.Headers[StripeSignature.Header], body, time.GetUtcNow()))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The Stripe signature is not valid.");
        }

        StripeBillingEvent stripeEvent;
        try
        {
            stripeEvent = StripeBillingEvent.Parse(body);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The event is not a Stripe event.");
        }

        var logger = loggers.CreateLogger(typeof(BillingEndpoints));
        try
        {
            switch (stripeEvent.Type)
            {
                case "checkout.session.completed" or "checkout.session.async_payment_succeeded" when stripeEvent is { Mode: "setup", SetupIntent: { } setupIntent }:
                    var card = await stripe.ReadSetupAsync(setupIntent, cancellationToken);
                    await billing.CompleteCheckoutAsync(stripeEvent.ObjectId, new CheckoutCompletion(card, null, null, null, BillingSources.Stripe, body), cancellationToken);
                    break;
                case "checkout.session.completed" or "checkout.session.async_payment_succeeded" when stripeEvent is { PaymentStatus: "paid", PaymentIntent: { } paymentIntent }:
                    var (paidWith, amount, currency) = await stripe.ReadPaymentAsync(paymentIntent, cancellationToken);
                    await billing.CompleteCheckoutAsync(
                        stripeEvent.ObjectId, new CheckoutCompletion(paidWith, paymentIntent, amount, currency, BillingSources.Stripe, body), cancellationToken);
                    break;
                case "checkout.session.expired":
                    await billing.ExpireCheckoutAsync(stripeEvent.ObjectId, BillingSources.Stripe, cancellationToken);
                    break;
                case "payment_intent.succeeded" when stripeEvent is { Source: StripeBillingGateway.CardOnFile, ChargeId: { } chargeId, Amount: { } paid, Currency: { } paidIn }:
                    await billing.ConfirmChargePaidAsync(chargeId, stripeEvent.ObjectId, StripeClient.FromMinorUnits(paid), paidIn.ToUpperInvariant(), BillingSources.Stripe, body, cancellationToken);
                    break;
                default:
                    LogIgnored(logger, stripeEvent.Type);
                    break;
            }
        }
        catch (Exception exception) when (exception is BillingProviderUnavailableException or StripeRefusedException)
        {
            // Stripe sends the event again later.
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Stripe could not be asked about the payment.");
        }

        return TypedResults.Ok();
    }

    internal static async Task<BillingResponse> ViewAsync(
        Firm firm,
        BillingService billing,
        BillingStore store,
        SlotService slots,
        BillingOptions options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var terms = billing.Terms;
        var usage = await slots.UsageAsync(firm, cancellationToken);
        await using var connection = await store.OpenAsync(cancellationToken);
        var plan = await BillingStore.GetBillingAsync(connection, firm.Id, forUpdate: false, cancellationToken);
        var paying = firm.Status == FirmStatus.Live && plan is { Plan: BillingPlan.Paid, ActivatedAt: not null };

        NextChargeResponse? next = null;
        if (paying)
        {
            var month = await BillingStore.FirstUnchargedMonthAsync(connection, firm.Id, BillingRules.MonthOf(time.GetUtcNow()).AddMonths(1), cancellationToken);

            var nextSlots = Math.Max(plan!.Slots ?? terms.MinSlots, usage.Used + usage.Reserved);
            next = new NextChargeResponse(month, BillingRules.ChargeTimeOf(month, terms.ChargeDaysBeforeMonth), nextSlots, BillingRules.MonthlyPrice(nextSlots, terms.SlotPrices));
        }

        var charges = await store.ListChargesAsync(firm.Id, ChargesShown, cancellationToken);
        return new BillingResponse(
            firm.Status,
            plan?.Plan == BillingPlan.Complimentary || paying ? plan!.Plan : null,
            billing.Provider,
            SlotsResponse.From(usage, options.WarningPercent),
            paying ? plan!.Slots : null,
            paying ? plan!.AutoExpandStep : null,
            paying && plan!.Card is { } card ? new CardResponse(card.Brand, card.Last4, card.ExpMonth, card.ExpYear) : null,
            paying ? plan!.UnpaidSince : null,
            next,
            [.. charges.Select(ChargeResponse.From)],
            new PricesResponse(
                terms.Currency,
                terms.StartupFee,
                [.. terms.SlotPrices.Select(p => new SlotPriceResponse(p.From, p.Price))],
                terms.MinSlots,
                terms.MaxSlots,
                terms.ChargeDaysBeforeMonth,
                options.WarningPercent),
            firm.Status == FirmStatus.Live ? null : billing.GoLiveProblem(firm));
    }

    private static Ok<QuoteResponse> Quote(QuoteKind kind, int slots, IReadOnlyList<ChargeLine> lines, decimal monthly, DateOnly? from, BillingTerms terms, string? problem) =>
        TypedResults.Ok(new QuoteResponse(kind, slots, lines, lines.Sum(l => l.Amount), monthly, from, terms.Currency, problem));

    private static Results<Ok<CheckoutResponse>, ProblemHttpResult> CheckoutOf(BillingResult result) => result switch
    {
        BillingResult.Checkout checkout => TypedResults.Ok(new CheckoutResponse(checkout.Url)),
        BillingResult.Refused refused => AccountActions.Problem(refused.StatusCode, refused.Problem),
        _ => throw new InvalidOperationException("A checkout was expected."),
    };

    private static async Task<BillingCheckout?> TestCheckoutAsync(Firm firm, string checkoutId, BillingStore store, BillingService billing, CancellationToken cancellationToken) =>
        billing.Provider == BillingProvider.Test
        && await store.GetCheckoutAsync(checkoutId, cancellationToken) is { Provider: BillingProvider.Test } checkout
        && checkout.FirmId == firm.Id
            ? checkout
            : null;

    private static string EmailOf(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.Email) ?? "";

    private static ProblemHttpResult UnknownCheckout() => AccountActions.Problem(StatusCodes.Status404NotFound, "No such checkout page.");

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored Stripe event {EventType} about payments to the platform")]
    private static partial void LogIgnored(ILogger logger, string eventType);

    /// <summary>The parts of a Stripe event that billing needs.</summary>
    private sealed record StripeBillingEvent(
        string Type,
        string ObjectId,
        string? Mode,
        string? PaymentStatus,
        string? PaymentIntent,
        string? SetupIntent,
        string? Source,
        Guid? ChargeId,
        long? Amount,
        string? Currency)
    {
        public static StripeBillingEvent Parse(string body)
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var data = root.GetProperty("data").GetProperty("object");
            var metadata = data.TryGetProperty("metadata", out var m) ? m : default;
            return new StripeBillingEvent(
                root.GetProperty("type").GetString()!,
                String(data, "id") ?? throw new KeyNotFoundException("The event's object has no id."),
                String(data, "mode"),
                String(data, "payment_status"),
                String(data, "payment_intent"),
                String(data, "setup_intent"),
                String(metadata, "source"),
                String(metadata, "charge_id") is { } chargeId && Guid.TryParse(chargeId, out var id) ? id : null,
                data.TryGetProperty("amount_received", out var amount) && amount.ValueKind == JsonValueKind.Number ? amount.GetInt64() : null,
                String(data, "currency"));
        }

        private static string? String(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
