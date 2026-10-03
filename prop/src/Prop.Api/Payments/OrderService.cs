using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using Common.Postgres;

using Microsoft.Extensions.Options;

using Npgsql;

using NpgsqlTypes;

using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Email;
using Prop.Api.Firms;
using Prop.Api.Json;
using Prop.Api.Portal;
using Prop.Rules;

namespace Prop.Api.Payments;

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
}

/// <summary>
/// Purchases of challenges in the firms' portals (ADR 0019). An order is made before the buyer pays, with the
/// price as it is then. Only the provider's message, or the firm itself, marks it as paid, and the account is
/// started in the same transaction, so a paid order has exactly one account however often the payment is reported.
/// </summary>
internal sealed partial class OrderService(
    OrderStore orders,
    PriceCatalog prices,
    ChallengeCatalog challenges,
    ChallengeService accounts,
    PortalUsers users,
    IEmailSender email,
    StripeClient stripe,
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    FirmCatalog firms,
    IOptions<PaymentsOptions> options,
    TimeProvider time,
    ILogger<OrderService> logger)
{
    /// <summary>How long a buyer waits before the invitation can be sent again.</summary>
    public static readonly TimeSpan InviteResendDelay = TimeSpan.FromMinutes(1);

    /// <summary>Test payments are for the sandbox. Development may allow them for live firms too.</summary>
    public bool TestPaymentsAllowed(Firm firm) => firm.Status != FirmStatus.Live || options.Value.TestPaymentsForLiveFirms;

    /// <summary>
    /// The provider the firm's portal takes payment with now, or null when it cannot sell. Stripe's live keys work
    /// only once the firm is live, so no real money is taken in the sandbox.
    /// </summary>
    public PaymentProvider? ProviderOf(Firm firm) => firm.Payments.Provider switch
    {
        PaymentProvider.Test when TestPaymentsAllowed(firm) => PaymentProvider.Test,
        PaymentProvider.Stripe when firm.Payments.Stripe is { } keys && (firm.Status == FirmStatus.Live || keys.IsTestMode) => PaymentProvider.Stripe,
        PaymentProvider.External when firm.Payments.CheckoutUrl is not null => PaymentProvider.External,
        _ => null,
    };

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
    /// Makes an order for the challenge at its price now, and starts the payment with the firm's provider. The
    /// buyer pays on the page the order's checkout address leads to.
    /// </summary>
    public async Task<NewOrder> CreateAsync(Firm firm, string? buyerEmail, string? challengeId, bool acceptedTerms, CancellationToken cancellationToken)
    {
        if (ProviderOf(firm) is not { } provider)
        {
            return new NewOrder.Refused(StatusCodes.Status409Conflict, "The firm does not sell challenges here right now.");
        }

        if (string.IsNullOrWhiteSpace(buyerEmail) || !buyerEmail.Contains('@', StringComparison.Ordinal))
        {
            return new NewOrder.Refused(StatusCodes.Status422UnprocessableEntity, "A valid email address is required.");
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

        if (!await accounts.HasRoomAsync(firm, cancellationToken))
        {
            return new NewOrder.Refused(StatusCodes.Status409Conflict, "The firm cannot start more challenges right now. Try again later.");
        }

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
                        new StripeCheckoutRequest(id, firm.Id, email, item.Challenge.Name, item.Price.Amount, item.Price.Currency, returnUrl, new Uri(firm.Portal.Url, "buy"), expiresAt),
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

        var order = await orders.InsertAsync(id, firm.Id, email, item.Challenge.Id, item.Price, provider, token, checkoutId, checkoutUrl, now, expiresAt, cancellationToken);
        return new NewOrder.Created(order, token);
    }

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

            var started = await accounts.StartAsync(connection, firm, order.Email, order.ChallengeId, null, cancellationToken);
            var problem = started.Account is not null
                ? null
                : started.SandboxFull
                    ? "The sandbox had no room for another open challenge account, so none was started."
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
            await transaction.CommitAsync(cancellationToken);
        }

        accounts.Notify(firm);
        if (paid.Problem is { } reason)
        {
            LogPaidWithoutAccount(logger, paid.Id, firm.Id, reason);
        }

        // A buyer without a password gets an invitation. If the email cannot be sent, the buyer asks for it again.
        await SendInviteAsync(firm, paid, payment.Source, cancellationToken);
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
    /// Emails the buyer of a paid order an invitation to choose a password for the firm's portal, unless the
    /// trader already has one. A new invitation replaces the trader's older unused ones.
    /// </summary>
    public async Task<InviteOutcome> SendInviteAsync(Firm firm, Order order, string source, CancellationToken cancellationToken)
    {
        if (order.Status != OrderStatus.Paid || order.AccountId is null || await users.FindTraderAsync(firm.Id, order.Email, cancellationToken) is not { } trader)
        {
            return InviteOutcome.NotPaid;
        }

        if (trader.PasswordHash is not null)
        {
            return InviteOutcome.NotNeeded;
        }

        var now = time.GetUtcNow();
        if (order.InviteSentAt is { } sentAt && now - sentAt < InviteResendDelay)
        {
            return InviteOutcome.TooSoon;
        }

        var invite = await users.CreateInviteAsync(trader.Id, now, cancellationToken);
        var challengeName = (await challenges.GetAsync(firm.Id, order.ChallengeId, cancellationToken))?.Name ?? order.ChallengeId;
        try
        {
            await email.SendAsync(
                PlatformEmails.InviteBuyer(firm.Name, challengeName, order.Email, new Uri(firm.Portal.Url, $"invite?token={invite.Token}"), PortalUsers.InviteLifetime),
                cancellationToken);
        }
        catch (EmailNotSentException exception)
        {
            LogInviteNotSent(logger, exception, order.Id, firm.Id);
            return InviteOutcome.Failed;
        }

        await orders.SetInviteSentAsync(order.Id, source, now, cancellationToken);
        return InviteOutcome.Sent;
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
