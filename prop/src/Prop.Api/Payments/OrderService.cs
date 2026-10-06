using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using Common.Postgres;

using Microsoft.Extensions.Options;

using Npgsql;

using NpgsqlTypes;

using Prop.Api.Billing;
using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Email;
using Prop.Api.Firms;
using Prop.Api.Json;
using Prop.Api.Portal;
using Prop.Api.Review;
using Prop.Rules;

namespace Prop.Api.Payments;

/// <summary>Who buys: the email, and the name and country, a two-letter code, as given in the shop.</summary>
internal sealed record Buyer(string? Email, string? Name, string? Country);

/// <summary>A challenge for sale in the firm's portal, and its price.</summary>
internal sealed record ShopItem(ChallengeDefinition Challenge, ChallengePrice Price);

/// <summary>A new order and the token for the buyer's link to it, or why no order was made.</summary>
internal abstract record NewOrder
{
    public sealed record Created(Order Order, string AccessToken) : NewOrder;

    public sealed record Refused(int StatusCode, string Problem) : NewOrder;
}

/// <summary>
/// A payment the provider or the firm confirmed. <paramref name="Amount"/> and <paramref name="Currency"/>, when
/// the provider says them, must be the order's. <paramref name="Detail"/> is the provider's message, as JSON.
/// </summary>
internal sealed record PaymentConfirmation(PaymentProvider Provider, string Source, string? Reference, decimal? Amount, string? Currency, string? Detail);

internal enum OrderChangeOutcome
{
    Done,
    AlreadyDone,
    Unknown,
    WrongProvider,
    NotAllowed,
    AmountMismatch,
}

/// <summary>What happened to an order, and the order as it is afterwards.</summary>
internal sealed record OrderChange(OrderChangeOutcome Outcome, Order? Order, string? Problem = null);

internal enum InviteOutcome
{
    Sent,
    NotNeeded,
    NotPaid,
    TooSoon,
    Failed,

    /// <summary>We have not approved the firm, and the buyer is not one of its administrators (ADR 0043).</summary>
    Withheld,
}

/// <summary>
/// Purchases of challenges in the firms' portals (ADR 0019). An order is made before the buyer pays, with the
/// price as it is then. Only the provider's message, or the firm itself, marks it as paid, and the account is
/// started in the same transaction, so a paid order has exactly one account however often the payment is reported.
/// </summary>
internal sealed partial class OrderService(
    OrderStore orders,
    PriceCatalog prices,
    DiscountStore discounts,
    ChallengeCatalog challenges,
    ChallengeService accounts,
    ChallengeQueries queries,
    SlotService slots,
    Notifications notifications,
    WorkSignals signals,
    PortalUsers users,
    IEmailSender email,
    FirmApproval approval,
    StripeClient stripe,
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    FirmCatalog firms,
    IOptions<PaymentsOptions> options,
    TimeProvider time,
    ILogger<OrderService> logger)
{
    public const int MaxBuyerNameLength = 100;

    /// <summary>How long a buyer waits before the invitation can be sent again.</summary>
    public static readonly TimeSpan InviteResendDelay = TimeSpan.FromMinutes(1);

    /// <summary>Test payments, and Stripe's test keys, are for the sandbox. Development may allow them for live firms too.</summary>
    public bool TestPaymentsAllowed(Firm firm) => TestPaymentsAllowed(firm, options.Value.TestPaymentsForLiveFirms);

    public static bool TestPaymentsAllowed(Firm firm, bool testPaymentsForLiveFirms) => firm.Status != FirmStatus.Live || testPaymentsForLiveFirms;

    /// <summary>
    /// Whether the shop sells only to the firm's own administrators now, who try it (ADR 0043): until we approve the firm,
    /// and with its own checkout page until it is live, since a firm takes real money through its portal only once it is live.
    /// </summary>
    public async Task<bool> TeamOnlyAsync(Firm firm, CancellationToken cancellationToken) =>
        (firm.Status != FirmStatus.Live && ProviderOf(firm) == PaymentProvider.External) || !await approval.IsApprovedAsync(firm.Id, cancellationToken);

    /// <summary>
    /// The provider the firm's portal takes payment with now, or null when it cannot sell. Stripe's live keys work only once
    /// the firm is live, so no real money is taken in the sandbox, and its test keys only where test payments are allowed,
    /// so a live firm never sells for test money.
    /// </summary>
    public PaymentProvider? ProviderOf(Firm firm) => ProviderOf(firm, options.Value.TestPaymentsForLiveFirms);

    public static PaymentProvider? ProviderOf(Firm firm, bool testPaymentsForLiveFirms) => firm.Payments.Provider switch
    {
        PaymentProvider.Test when TestPaymentsAllowed(firm, testPaymentsForLiveFirms) => PaymentProvider.Test,
        PaymentProvider.Stripe when firm.Payments.Stripe is { } keys && (keys.IsTestMode ? TestPaymentsAllowed(firm, testPaymentsForLiveFirms) : firm.Status == FirmStatus.Live)
            => PaymentProvider.Stripe,
        PaymentProvider.External when firm.Payments.CheckoutUrl is not null => PaymentProvider.External,
        _ => null,
    };

    /// <summary>
    /// Why the firm's shop does not take payment once the firm is live, or null when it does, or the firm chose not to sell
    /// in its portal. A firm in the sandbox cannot go live like that, so its shop is never closed or selling for test money;
    /// a live firm is told, since its shop sells nothing.
    /// </summary>
    public static string? LiveShopProblem(Firm firm, bool testPaymentsForLiveFirms)
    {
        if (firm.Payments.Provider is not { } provider || ProviderOf(firm with { Status = FirmStatus.Live }, testPaymentsForLiveFirms) is not null)
        {
            return null;
        }

        const string Choose = "or choose No sales in the portal.";
        return (provider, firm.Status == FirmStatus.Live) switch
        {
            (PaymentProvider.Test, false) =>
                $"Your shop takes test payments, which stop when you go live. Under Checkout, connect Stripe with your live keys or your own checkout page, {Choose}",
            (PaymentProvider.Test, true) =>
                $"Your shop sells nothing, since test payments stopped when you went live. Under Checkout, connect Stripe with your live keys or your own checkout page, {Choose}",
            (PaymentProvider.Stripe, false) =>
                $"Your Stripe keys are test keys, which take no real money once you are live. Under Checkout, paste your live secret key (sk_live_), {Choose}",
            (PaymentProvider.Stripe, true) =>
                $"Your shop sells nothing, since your Stripe keys are test keys. Under Checkout, paste your live secret key (sk_live_), {Choose}",
            (_, false) => $"Your shop cannot take payment yet. Under Checkout, finish how traders pay, {Choose}",
            (_, true) => $"Your shop sells nothing, since how traders pay is not finished. Under Checkout, finish it, {Choose}",
        };
    }

    /// <summary>The challenges for sale in the firm's portal. Empty when the firm cannot take payment.</summary>
    public async Task<IReadOnlyList<ShopItem>> ShopAsync(Firm firm, CancellationToken cancellationToken)
    {
        if (ProviderOf(firm) is null)
        {
            return [];
        }

        var forSale = (await prices.ListAsync(firm.Id, cancellationToken)).Where(p => p.ForSale).ToDictionary(p => p.ChallengeId, StringComparer.Ordinal);
        return
        [
            .. (await challenges.ListAsync(firm.Id, cancellationToken))
                .Where(c => forSale.ContainsKey(c.Id))
                .Select(c => new ShopItem(c, forSale[c.Id]))
                .OrderBy(i => i.Price.Amount),
        ];
    }

    /// <summary>
    /// What the challenge costs with the discount code, or why the code cannot be used on it. A code for retries needs
    /// the buyer's email, so without one it is only checked once the order is made.
    /// </summary>
    public async Task<(DiscountedPrice? Price, DiscountCode? Code, string? Refusal)> QuoteDiscountAsync(
        Firm firm,
        string? challengeId,
        string? code,
        string? email,
        CancellationToken cancellationToken)
    {
        var item = (await ShopAsync(firm, cancellationToken)).FirstOrDefault(i => i.Challenge.Id == challengeId);
        if (item is null)
        {
            return (null, null, "That challenge is not for sale.");
        }

        if (string.IsNullOrWhiteSpace(code) || await discounts.FindAsync(firm.Id, code, time.GetUtcNow(), cancellationToken) is not { } found)
        {
            return (null, null, "There is no such code.");
        }

        if (found.ForRetries && !string.IsNullOrWhiteSpace(email) && !await discounts.HasFailedAccountAsync(firm.Id, email, cancellationToken))
        {
            return (null, found, RetryOnly);
        }

        var (price, refusal) = DiscountRules.Apply(found, item.Price, time.GetUtcNow());
        return (price, found, refusal);
    }

    private const string RetryOnly = "That code is for a new try after a challenge that failed, with the email it was bought with.";

    /// <summary>
    /// Makes an order for the challenge at its price now, with the discount code when there is one, and starts the
    /// payment with the firm's provider. The buyer pays on the page the order's checkout address leads to.
    /// </summary>
    public async Task<NewOrder> CreateAsync(Firm firm, Buyer buyer, string? challengeId, bool acceptedTerms, string? discountCode, CancellationToken cancellationToken)
    {
        if (ProviderOf(firm) is not { } provider)
        {
            return new NewOrder.Refused(StatusCodes.Status409Conflict, "The firm does not sell challenges here right now.");
        }

        var buyerEmail = buyer.Email;
        if (string.IsNullOrWhiteSpace(buyerEmail) || !buyerEmail.Contains('@', StringComparison.Ordinal))
        {
            return new NewOrder.Refused(StatusCodes.Status422UnprocessableEntity, "A valid email address is required.");
        }

        if (await TeamOnlyAsync(firm, cancellationToken) && !await approval.IsAdministratorAsync(firm.Id, buyerEmail, cancellationToken))
        {
            return new NewOrder.Refused(StatusCodes.Status403Forbidden, FirmApproval.ShopClosedProblem(firm));
        }

        var buyerName = buyer.Name?.Trim();
        if (string.IsNullOrEmpty(buyerName) || buyerName.Length > MaxBuyerNameLength)
        {
            return new NewOrder.Refused(StatusCodes.Status422UnprocessableEntity, FormattableString.Invariant($"Write your name, in at most {MaxBuyerNameLength} characters."));
        }

        var buyerCountry = buyer.Country?.Trim().ToUpperInvariant();
        if (buyerCountry is not { Length: 2 } || !buyerCountry.All(char.IsAsciiLetterUpper))
        {
            return new NewOrder.Refused(StatusCodes.Status422UnprocessableEntity, "Choose your country.");
        }

        if (firm.Payments.TermsUrl is not null && !acceptedTerms)
        {
            return new NewOrder.Refused(StatusCodes.Status422UnprocessableEntity, "Accept the terms to buy.");
        }

        var item = (await ShopAsync(firm, cancellationToken)).FirstOrDefault(i => i.Challenge.Id == challengeId);
        if (item is null)
        {
            return new NewOrder.Refused(StatusCodes.Status404NotFound, "That challenge is not for sale.");
        }

        if (!(await slots.UsageAsync(firm, cancellationToken)).HasRoom)
        {
            return NoRoom();
        }

        OrderDiscount? discount = null;
        if (!string.IsNullOrWhiteSpace(discountCode))
        {
            var (discounted, code, refusal) = await QuoteDiscountAsync(firm, item.Challenge.Id, discountCode, buyerEmail, cancellationToken);
            if (discounted is null || code is null)
            {
                return new NewOrder.Refused(StatusCodes.Status422UnprocessableEntity, refusal ?? "There is no such code.");
            }

            discount = new OrderDiscount(code.Id, code.Code, discounted);
        }

        var amount = discount?.Price.Amount ?? item.Price.Amount;
        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var expiresAt = now + options.Value.OrderLifetime;
        var email = buyerEmail.Trim();
        var returnUrl = new Uri(firm.Portal.Url, $"orders/{id}?token={token}");

        string? checkoutId = null;
        Uri checkoutUrl;
        switch (provider)
        {
            case PaymentProvider.Stripe:
                try
                {
                    var checkout = await stripe.CreateCheckoutAsync(
                        firm.Payments.Stripe!.SecretKey,
                        new StripeCheckoutRequest(
                            id,
                            firm.Id,
                            email,
                            discount is null ? item.Challenge.Name : $"{item.Challenge.Name} (code {discount.Code})",
                            amount,
                            item.Price.Currency,
                            returnUrl,
                            new Uri(firm.Portal.Url, "buy"),
                            expiresAt),
                        cancellationToken);
                    (checkoutId, checkoutUrl) = (checkout.Id, checkout.Url);
                }
                catch (CheckoutNotStartedException exception)
                {
                    LogCheckoutNotStarted(logger, exception, firm.Id);
                    return new NewOrder.Refused(StatusCodes.Status503ServiceUnavailable, "Payments are not working right now. Try again later.");
                }

                break;
            case PaymentProvider.External:
                var page = firm.Payments.CheckoutUrl!;
                checkoutUrl = new Uri($"{page.AbsoluteUri}{(page.Query.Length > 0 ? "&" : "?")}order={id}&return={Uri.EscapeDataString(returnUrl.AbsoluteUri)}");
                break;
            default:
                checkoutUrl = new Uri(firm.Portal.Url, $"checkout/test?order={id}&token={token}");
                break;
        }

        // The order holds a slot while the buyer pays, so the payment always has room to start the challenge, and a use
        // of its code, so the code is never used more often than the firm allows.
        var (order, refused) = await orders.InsertAsync(
            id,
            firm.Id,
            email,
            buyerName,
            buyerCountry,
            item.Challenge.Id,
            item.Price,
            discount,
            provider,
            token,
            checkoutId,
            checkoutUrl,
            now,
            expiresAt,
            async (connection, ct) =>
            {
                if (discount is not null
                    && (await DiscountStore.LockAsync(connection, firm.Id, discount.CodeId, now, ct) is not { } code
                        || DiscountRules.Apply(code, item.Price, now).Refusal is not null))
                {
                    return "That code has just been used up.";
                }

                await SlotService.LockAsync(connection, firm.Id, ct);
                var usage = await slots.UsageAsync(connection, firm, null, ct);
                if (usage.Free == 1)
                {
                    signals.Billing.Set();
                }

                return usage.HasRoom ? null : NoRoomProblem;
            },
            cancellationToken);
        return order is not null
            ? new NewOrder.Created(order, token)
            : new NewOrder.Refused(StatusCodes.Status409Conflict, refused ?? NoRoomProblem);
    }

    private const string NoRoomProblem = "The firm cannot start more challenges right now. Try again later.";

    private static NewOrder.Refused NoRoom() => new(StatusCodes.Status409Conflict, NoRoomProblem);

    /// <summary>
    /// The order is paid: it becomes Paid and its account starts, in one transaction. A payment reported again
    /// changes nothing. A payment that does not match the order's price is recorded but starts nothing.
    /// </summary>
    public async Task<OrderChange> MarkPaidAsync(Firm firm, Guid orderId, PaymentConfirmation payment, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);

        // The firm as it is now, so the sandbox limit and webhooks are current.
        firm = firms.ById(firm.Id) ?? firm;
        var now = time.GetUtcNow();
        Order paid;
        ChallengeDefinition? challenge;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var order = await OrderStore.LockAsync(connection, firm.Id, orderId, now, cancellationToken);
            if (order is null)
            {
                return new OrderChange(OrderChangeOutcome.Unknown, null);
            }

            if (order.Provider != payment.Provider)
            {
                return new OrderChange(OrderChangeOutcome.WrongProvider, order, WrongProviderProblem(order.Provider));
            }

            if (order.Status == OrderStatus.Paid)
            {
                return new OrderChange(OrderChangeOutcome.AlreadyDone, order);
            }

            if (payment.Amount is { } amount && (amount != order.Amount || !string.Equals(payment.Currency, order.Currency, StringComparison.OrdinalIgnoreCase)))
            {
                await OrderStore.AddEventAsync(connection, order.Id, "payment_mismatch", payment.Source, payment.Detail, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                LogPaymentMismatch(logger, order.Id, firm.Id);
                return new OrderChange(OrderChangeOutcome.AmountMismatch, order, "The payment does not match the order's price.");
            }

            var started = await accounts.StartAsync(connection, firm, order.Email, order.ChallengeId, null, order.Id, cancellationToken);

            // The trader keeps the name and country from the first order that gave them. The name kept names the buyer to the firm.
            string? traderName = null;
            var hasPassword = false;
            await using (var details = new NpgsqlCommand(
                "update traders set name = coalesce(name, $3), country = coalesce(country, $4) where firm_id = $1 and normalized_email = $2 returning name, password_hash is not null",
                connection))
            {
                details.Parameters.AddWithValue(firm.Id);
                details.Parameters.AddWithValue(Emails.Normalize(order.Email));
                details.Parameters.Add(OrderStore.Text(order.BuyerName));
                details.Parameters.Add(OrderStore.Text(order.BuyerCountry));
                await using var reader = await details.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    (traderName, hasPassword) = (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetBoolean(1));
                }
            }
            var problem = started.Account is not null
                ? null
                : started.Refusal is { } refusal
                    ? SlotService.OrderProblem(refusal)
                    : "The firm no longer has the challenge, so no account was started.";
            paid = order with
            {
                Status = OrderStatus.Paid,
                PaidAt = now,
                PaymentReference = payment.Reference ?? order.PaymentReference,
                AccountId = started.Account?.Id,
                Problem = problem,
            };
            await OrderStore.UpdateAsync(
                connection,
                order.Id,
                "status = $2, paid_at = $3, payment_reference = $4, challenge_account_id = $5, problem = $6",
                [
                    paid.Status.ToString(), now, OrderStore.Text(paid.PaymentReference),
                    new NpgsqlParameter { Value = (object?)paid.AccountId ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid }, OrderStore.Text(problem),
                ],
                cancellationToken);
            await OrderStore.AddEventAsync(connection, order.Id, "paid", payment.Source, payment.Detail, now, cancellationToken);
            var account = started.Account is { } a ? WebhookOutbox.Account(a.Id, a.Number, a.Email, a.DefinitionId, a.Reference) : null;
            await WebhookOutbox.AddAsync(connection, firm, "order.paid", account, OrderData(paid), now, cancellationToken);
            await notifications.QueueSaleAsync(
                connection,
                firm,
                Notifications.Person(traderName ?? paid.BuyerName, paid.Email),
                started.Account?.State.Definition.Name ?? order.ChallengeId,
                paid.Number,
                paid.Amount,
                paid.Currency,
                paid.AccountId,
                now,
                cancellationToken);

            // A buyer who has a password already gets the receipt with the payment. One who has not gets it with the
            // invitation, below.
            challenge = started.Account?.State.Definition;
            if (started.Account is { } startedAccount && hasPassword)
            {
                await EmailOutbox.AddAsync(
                    connection,
                    TraderEmails.ChallengeBought(firm, new OrderReceipt(paid, challenge), new Uri(firm.Portal.Url, $"accounts/{startedAccount.Id}")),
                    "order_receipt",
                    firm.Id,
                    now,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        accounts.Notify(firm);
        if (paid.Problem is { } reason)
        {
            LogPaidWithoutAccount(logger, paid.Id, firm.Id, reason);
        }

        // A buyer without a password gets an invitation, with the receipt. If the email cannot be sent, the buyer asks for it again.
        await SendInviteAsync(firm, paid, challenge, payment.Source, cancellationToken);
        return new OrderChange(OrderChangeOutcome.Done, await orders.GetAsync(firm.Id, orderId, time.GetUtcNow(), cancellationToken));
    }

    /// <summary>The money went back to the buyer. The account is not touched: the firm decides whether to cancel it.</summary>
    public Task<OrderChange> MarkRefundedAsync(Firm firm, Guid orderId, PaymentProvider provider, string source, string? detail, CancellationToken cancellationToken) =>
        MarkAsync(firm, orderId, provider, "refunded_at", "refunded", "order.refunded", source, detail, cancellationToken);

    /// <summary>The buyer disputed the payment with the bank. The firm answers the dispute with its provider.</summary>
    public Task<OrderChange> MarkDisputedAsync(Firm firm, Guid orderId, PaymentProvider provider, string source, string? detail, CancellationToken cancellationToken) =>
        MarkAsync(firm, orderId, provider, "disputed_at", "disputed", "order.disputed", source, detail, cancellationToken);

    /// <summary>The provider says the order will not be paid, for example an expired Stripe checkout.</summary>
    public async Task ExpireAsync(Firm firm, Guid orderId, PaymentProvider provider, string source, string? detail, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        var now = time.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await OrderStore.LockAsync(connection, firm.Id, orderId, now, cancellationToken) is { } order && order.Provider == provider && order.Status != OrderStatus.Paid)
        {
            await OrderStore.UpdateAsync(connection, order.Id, "status = $2", [OrderStatus.Expired.ToString()], cancellationToken);
            await OrderStore.AddEventAsync(connection, order.Id, "expired", source, detail, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Emails the buyer of a paid order an invitation to choose a password for the firm's portal, with the receipt, unless
    /// the trader already has one. A new invitation replaces the trader's older unused ones. Before we approve the firm,
    /// only a buyer who is one of its administrators gets one (ADR 0043). <paramref name="challenge"/> is the challenge as
    /// the order's account started with it, which is read when it is not given.
    /// </summary>
    public async Task<InviteOutcome> SendInviteAsync(Firm firm, Order order, ChallengeDefinition? challenge, string source, CancellationToken cancellationToken)
    {
        if (order.Status != OrderStatus.Paid || order.AccountId is null || await users.FindTraderAsync(firm.Id, order.Email, cancellationToken) is not { } trader)
        {
            return InviteOutcome.NotPaid;
        }

        if (trader.PasswordHash is not null)
        {
            return InviteOutcome.NotNeeded;
        }

        if (!await approval.MayReachAsync(firm.Id, order.Email, cancellationToken))
        {
            return InviteOutcome.Withheld;
        }

        var now = time.GetUtcNow();
        if (order.InviteSentAt is { } sentAt && now - sentAt < InviteResendDelay)
        {
            return InviteOutcome.TooSoon;
        }

        var receipt = challenge is null ? await ReceiptAsync(firm, order, cancellationToken) : new OrderReceipt(order, challenge);
        var invite = await users.CreateInviteAsync(trader.Id, now, cancellationToken);
        try
        {
            await email.SendAsync(
                TraderEmails.InviteBuyer(firm, receipt, new Uri(firm.Portal.Url, $"invite?token={invite.Token}"), PortalUsers.InviteLifetime), cancellationToken);
        }
        catch (EmailNotSentException exception)
        {
            LogInviteNotSent(logger, exception, order.Id, firm.Id);
            return InviteOutcome.Failed;
        }

        await orders.SetInviteSentAsync(order.Id, source, now, cancellationToken);
        return InviteOutcome.Sent;
    }

    /// <summary>
    /// The receipt of a paid order, with the challenge as the order's account started with it, or as the firm has it now
    /// when the order started no account.
    /// </summary>
    public async Task<OrderReceipt> ReceiptAsync(Firm firm, Order order, CancellationToken cancellationToken)
    {
        var started = order.AccountId is { } accountId ? await queries.GetAsync(firm.Id, accountId, cancellationToken) : null;
        return new OrderReceipt(order, started?.Account.State.Definition ?? await challenges.GetAsync(firm.Id, order.ChallengeId, cancellationToken));
    }

    /// <summary>Why a change to an order belongs to its provider, for example a Stripe order that only Stripe marks as paid.</summary>
    public static string WrongProviderProblem(PaymentProvider provider) => provider switch
    {
        PaymentProvider.Stripe => "Stripe reports this order's payments and refunds. Refund it in Stripe, and the order follows.",
        PaymentProvider.Test => "This is a test order. It is paid on its test checkout page.",
        _ => "This order is paid through the firm's own checkout. Mark it through the firm API or the admin panel.",
    };

    private async Task<OrderChange> MarkAsync(
        Firm firm,
        Guid orderId,
        PaymentProvider provider,
        string column,
        string eventType,
        string webhookType,
        string source,
        string? detail,
        CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        firm = firms.ById(firm.Id) ?? firm;
        var now = time.GetUtcNow();
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var order = await OrderStore.LockAsync(connection, firm.Id, orderId, now, cancellationToken);
            if (order is null)
            {
                return new OrderChange(OrderChangeOutcome.Unknown, null);
            }

            if (order.Provider != provider)
            {
                return new OrderChange(OrderChangeOutcome.WrongProvider, order, WrongProviderProblem(order.Provider));
            }

            if (order.Status != OrderStatus.Paid)
            {
                return new OrderChange(OrderChangeOutcome.NotAllowed, order, "The order is not paid.");
            }

            if ((column == "refunded_at" ? order.RefundedAt : order.DisputedAt) is not null)
            {
                return new OrderChange(OrderChangeOutcome.AlreadyDone, order);
            }

            await OrderStore.UpdateAsync(connection, order.Id, $"{column} = $2", [now], cancellationToken);
            await OrderStore.AddEventAsync(connection, order.Id, eventType, source, detail, now, cancellationToken);
            var marked = column == "refunded_at" ? order with { RefundedAt = now } : order with { DisputedAt = now };
            await WebhookOutbox.AddAsync(connection, firm, webhookType, await AccountOfAsync(connection, order, cancellationToken), OrderData(marked), now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        accounts.Notify(firm);
        return new OrderChange(OrderChangeOutcome.Done, await orders.GetAsync(firm.Id, orderId, now, cancellationToken));
    }

    private static JsonObject OrderData(Order order) => new() { ["order"] = JsonSerializer.SerializeToNode(OrderResponse.From(order), PropJson.Options) };

    private static async Task<JsonObject?> AccountOfAsync(NpgsqlConnection connection, Order order, CancellationToken cancellationToken)
    {
        if (order.AccountId is not { } accountId)
        {
            return null;
        }

        await using var command = new NpgsqlCommand("select number, definition_id, reference from challenge_accounts where id = $1", connection);
        command.Parameters.AddWithValue(accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? WebhookOutbox.Account(accountId, reader.GetInt64(0), order.Email, reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2))
            : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The payment provider of firm {FirmId} did not start a checkout")]
    private static partial void LogCheckoutNotStarted(ILogger logger, Exception exception, string firmId);

    [LoggerMessage(Level = LogLevel.Error, Message = "A payment for order {OrderId} of firm {FirmId} does not match the order's price; nothing was started")]
    private static partial void LogPaymentMismatch(ILogger logger, Guid orderId, string firmId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderId} of firm {FirmId} was paid but started no account: {Reason}")]
    private static partial void LogPaidWithoutAccount(ILogger logger, Guid orderId, string firmId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The invitation for order {OrderId} of firm {FirmId} could not be emailed")]
    private static partial void LogInviteNotSent(ILogger logger, Exception exception, Guid orderId, string firmId);
}
