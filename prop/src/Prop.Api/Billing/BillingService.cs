using System.Collections.Concurrent;
using System.Globalization;

using Microsoft.Extensions.Options;

using Npgsql;

using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Email;
using Prop.Api.Firms;
using Prop.Api.Ops;
using Prop.Api.Portal;
using Prop.Api.Review;
using Prop.Rules;

namespace Prop.Api.Billing;

/// <summary>What a billing request led to: done, a checkout page to send the firm to, or a refusal with the reason.</summary>
internal abstract record BillingResult
{
    public sealed record Done : BillingResult;

    public sealed record Checkout(Uri Url) : BillingResult;

    public sealed record Refused(int StatusCode, string Problem) : BillingResult;
}

/// <summary>
/// A checkout page was completed at the provider: the card used, and for a payment the provider's reference and
/// what it says was paid. <paramref name="Detail"/> is the provider's message, as JSON.
/// </summary>
internal sealed record CheckoutCompletion(SavedCard Card, string? PaymentReference, decimal? Amount, string? Currency, string Source, string? Detail);

internal enum CompletionOutcome
{
    Done,
    AlreadyDone,
    Unknown,
    AmountMismatch,
}

/// <summary>What happened when a charge's saved card was tried.</summary>
internal abstract record AttemptOutcome
{
    public sealed record Paid : AttemptOutcome;

    public sealed record Declined(string Reason) : AttemptOutcome;

    /// <summary>The provider could not be reached. The charge is tried again shortly.</summary>
    public sealed record Unavailable : AttemptOutcome;

    /// <summary>The charge is not waiting for its card, for example because it was just paid.</summary>
    public sealed record NotDue : AttemptOutcome;
}

/// <summary>
/// What firms pay us for their slots (ADR 0020). A firm in the sandbox pays a deposit when it sends its application
/// for our review, and once approved goes live by paying the startup fee less the deposit and its first month on a
/// checkout page, which saves its card (ADR 0021). Each month is then charged to the card some days before it
/// starts. More slots are paid at once for the rest of the month, fewer apply from the next unpaid month. A month
/// that starts unpaid, or our suspension of the firm, pauses its challenges until neither holds. Every change to a
/// charge happens in one transaction with what it pays for, so a payment is never counted twice or lost.
/// </summary>
internal sealed partial class BillingService(
    BillingStore store,
    SlotService slots,
    ChallengeService challenges,
    FirmStore firmStore,
    FirmCatalog firms,
    FirmAdmins admins,
    StaffNotifier staff,
    IEmailSender email,
    IBillingGateway gateway,
    WorkSignals signals,
    IOptions<BillingOptions> options,
    IOptions<PlatformOptions> platform,
    TimeProvider time,
    ILogger<BillingService> logger)
{
    // One change of a firm's paused challenges at a time, so the last decision always wins.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _standing = new(StringComparer.Ordinal);

    /// <summary>How long after the provider could not be reached a charge is tried again.</summary>
    public static readonly TimeSpan UnavailableRetry = TimeSpan.FromMinutes(5);

    /// <summary>How long a purchase of slots has to try the card before the background work may.</summary>
    public static readonly TimeSpan PurchaseGrace = TimeSpan.FromMinutes(1);

    /// <summary>The most slots bought at a time by automatic expansion.</summary>
    public const int MaxAutoExpandStep = 1_000;

    public BillingTerms Terms => BillingTerms.From(options.Value);

    public BillingProvider Provider => gateway.Provider;

    /// <summary>Why the firm cannot go live by paying now. Null when it can: in the sandbox, approved by us and not suspended.</summary>
    public static string? GoLiveProblem(GoLiveState state) => state switch
    {
        { Status: FirmStatus.Live } => "The firm is already live.",
        { Status: FirmStatus.Provisioning } => "The firm's trading server is still being set up. Try again in a minute.",
        { Suspended: true } => "The firm is suspended, so it cannot go live.",
        { Review: ReviewStatus.Approved } => null,
        { Review: ReviewStatus.Submitted } => "We are reviewing your application. You can go live once it is approved.",
        { Review: ReviewStatus.Rejected } => "Your application was not approved, so the firm cannot go live.",
        _ => "We review your company before you go live. Send your application under Verification.",
    };

    /// <summary>Where a checkout page sends the firm back to: its application for the deposit, otherwise its billing.</summary>
    public static string ReturnPathOf(ChargeKind? kind) => kind == ChargeKind.Deposit ? "admin/verification" : "admin/billing";

    public string? SlotsProblem(int slots) =>
        slots < Terms.MinSlots || slots > Terms.MaxSlots
            ? FormattableString.Invariant($"Choose {Terms.MinSlots:N0} to {Terms.MaxSlots:N0} slots.")
            : null;

    public static string? AutoExpandProblem(int? step) =>
        step is null or (>= 1 and <= MaxAutoExpandStep) ? null : FormattableString.Invariant($"Buy 1 to {MaxAutoExpandStep:N0} slots at a time, or turn automatic expansion off.");

    /// <summary>
    /// Starts the first payment of a firm in the sandbox that we approved: the startup fee less the deposit it paid,
    /// and its slots for the rest of the month. The firm pays on a checkout page, which saves the card, and goes live
    /// once the provider says it is paid. An earlier first payment that was never finished is void.
    /// </summary>
    public async Task<BillingResult> StartActivationAsync(Firm firm, int slotCount, int? autoExpandStep, string adminEmail, CancellationToken cancellationToken)
    {
        if ((SlotsProblem(slotCount) ?? AutoExpandProblem(autoExpandStep)) is { } invalid)
        {
            return new BillingResult.Refused(StatusCodes.Status422UnprocessableEntity, invalid);
        }

        var now = time.GetUtcNow();
        var months = BillingRules.IsNextMonthDue(now, Terms) ? 2 : 1;
        Charge charge;
        string? customerId;
        List<string> abandoned = [];
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            if (GoLiveProblem(await BillingStore.GoLiveStateAsync(connection, firm.Id, forUpdate: true, cancellationToken)) is { } problem)
            {
                return new BillingResult.Refused(StatusCodes.Status409Conflict, problem);
            }

            var lines = BillingRules.Activation(now, slotCount, Terms, await BillingStore.DepositPaidAsync(connection, firm.Id, Terms.Currency, cancellationToken));
            customerId = (await BillingStore.GetBillingAsync(connection, firm.Id, forUpdate: false, cancellationToken))?.Card?.CustomerId;
            await BillingStore.SavePaidPlanAsync(connection, firm.Id, gateway.Provider, slotCount, autoExpandStep, now, cancellationToken);
            foreach (var earlier in (await BillingStore.OpenChargesAsync(connection, firm.Id, cancellationToken)).Where(c => c.Kind == ChargeKind.Activation))
            {
                abandoned.AddRange(await VoidAsync(connection, earlier, "started_again", BillingSources.Admin, now, cancellationToken) ?? []);
            }

            charge = await InsertChargeAsync(connection, firm.Id, ChargeKind.Activation, BillingRules.MonthOf(now), months, slotCount, lines, null, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var checkoutId in abandoned)
        {
            await gateway.ExpireCheckoutAsync(checkoutId, cancellationToken);
        }

        return await OpenCheckoutAsync(firm, CheckoutPurpose.Payment, charge, adminEmail, customerId, cancellationToken);
    }

    /// <summary>
    /// Starts the deposit for our review, when the firm sends its application. The firm pays on a checkout page,
    /// which saves the card, and the application is sent once the provider says it is paid. An earlier deposit
    /// that was never finished is void.
    /// </summary>
    public async Task<BillingResult> StartDepositAsync(Firm firm, string adminEmail, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        Charge charge;
        List<string> abandoned = [];
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var state = await BillingStore.GoLiveStateAsync(connection, firm.Id, forUpdate: true, cancellationToken);
            if (state.Status == FirmStatus.Live || state.Suspended)
            {
                return new BillingResult.Refused(StatusCodes.Status409Conflict, state.Suspended ? "The firm is suspended." : "The firm is already live.");
            }

            if (await BillingStore.DepositPaidAsync(connection, firm.Id, Terms.Currency, cancellationToken) > 0)
            {
                return new BillingResult.Refused(StatusCodes.Status409Conflict, "The deposit is already paid.");
            }

            await BillingStore.EnsurePaidPlanAsync(connection, firm.Id, gateway.Provider, now, cancellationToken);
            foreach (var earlier in (await BillingStore.OpenChargesAsync(connection, firm.Id, cancellationToken)).Where(c => c.Kind == ChargeKind.Deposit))
            {
                abandoned.AddRange(await VoidAsync(connection, earlier, "started_again", BillingSources.Admin, now, cancellationToken) ?? []);
            }

            charge = await InsertChargeAsync(connection, firm.Id, ChargeKind.Deposit, BillingRules.MonthOf(now), 0, 0, BillingRules.Deposit(Terms), null, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var checkoutId in abandoned)
        {
            await gateway.ExpireCheckoutAsync(checkoutId, cancellationToken);
        }

        return await OpenCheckoutAsync(firm, CheckoutPurpose.Payment, charge, adminEmail, null, cancellationToken);
    }

    /// <summary>
    /// Changes the firm's slots. More slots are charged to the card at once for the rest of the month, and for
    /// the next month too when it is already paid. Fewer slots apply from the next month that is not charged yet,
    /// and must leave room for the challenges already open.
    /// </summary>
    public Task<BillingResult> ChangeSlotsAsync(Firm firm, int slotCount, CancellationToken cancellationToken) =>
        ChangeSlotsAsync(firm, slotCount, automatic: false, cancellationToken);

    private async Task<BillingResult> ChangeSlotsAsync(Firm firm, int slotCount, bool automatic, CancellationToken cancellationToken)
    {
        if (SlotsProblem(slotCount) is { } invalid)
        {
            return new BillingResult.Refused(StatusCodes.Status422UnprocessableEntity, invalid);
        }

        var now = time.GetUtcNow();
        Charge? charge = null;
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await SlotService.LockAsync(connection, firm.Id, cancellationToken);
            if (await PaidBillingAsync(connection, firm, cancellationToken) is null)
            {
                return NotPaying();
            }

            var usage = await slots.UsageAsync(connection, firm, null, cancellationToken);
            if (!usage.Paid)
            {
                return new BillingResult.Refused(StatusCodes.Status409Conflict, "This month is not paid yet. Pay it first, then change your slots.");
            }

            if (await HasOpenSlotsChargeAsync(connection, firm.Id, cancellationToken))
            {
                return new BillingResult.Refused(StatusCodes.Status409Conflict, "Slots are being bought right now. Try again in a minute.");
            }

            var current = usage.Slots!.Value;
            if (slotCount > current)
            {
                var month = BillingRules.MonthOf(now);
                var nextPaid = (await BillingStore.GetPeriodAsync(connection, firm.Id, month.AddMonths(1), cancellationToken))?.Slots;
                var lines = BillingRules.MoreSlots(now, current, slotCount, nextPaid, Terms);

                // Tried below at once. The worker tries it only if this request never gets to.
                charge = await InsertChargeAsync(
                    connection, firm.Id, ChargeKind.Slots, month, lines.Count, slotCount, lines, now + PurchaseGrace, now, cancellationToken);
            }
            else
            {
                var taken = usage.Used + usage.Reserved;
                if (slotCount < taken)
                {
                    return new BillingResult.Refused(
                        StatusCodes.Status409Conflict,
                        FormattableString.Invariant($"{taken} slots are taken by open challenges and orders. Choose at least that many."));
                }

                await BillingStore.UpdateBillingAsync(connection, firm.Id, "slots = $2", [slotCount], now, cancellationToken);
                await BillingStore.AddEventAsync(connection, firm.Id, null, "slots_changed", BillingSources.Admin, Detail(("slots", slotCount)), now, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        if (charge is null)
        {
            return new BillingResult.Done();
        }

        var outcome = await AttemptAsync(charge.Id, notifyOnDecline: automatic, cancellationToken);
        if (outcome is AttemptOutcome.NotDue)
        {
            outcome = await SettledOutcomeAsync(charge.Id, cancellationToken);
        }

        return outcome switch
        {
            AttemptOutcome.Paid => new BillingResult.Done(),
            AttemptOutcome.Declined declined => new BillingResult.Refused(StatusCodes.Status402PaymentRequired, $"The card was declined: {declined.Reason}"),
            _ => new BillingResult.Refused(
                StatusCodes.Status503ServiceUnavailable,
                "The payment provider could not be reached. The slots are bought as soon as it answers."),
        };
    }

    /// <summary>How many slots are bought at once when the last free one is taken. Null turns it off.</summary>
    public async Task<BillingResult> SetAutoExpandAsync(Firm firm, int? step, CancellationToken cancellationToken)
    {
        if (AutoExpandProblem(step) is { } invalid)
        {
            return new BillingResult.Refused(StatusCodes.Status422UnprocessableEntity, invalid);
        }

        var now = time.GetUtcNow();
        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await PaidBillingAsync(connection, firm, cancellationToken) is null)
        {
            return NotPaying();
        }

        await BillingStore.UpdateBillingAsync(connection, firm.Id, "auto_expand_step = $2", [BillingStore.Int(step)], now, cancellationToken);
        await BillingStore.AddEventAsync(connection, firm.Id, null, "auto_expand_changed", BillingSources.Admin, Detail(("step", step)), now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        signals.Billing.Set();
        return new BillingResult.Done();
    }

    /// <summary>A checkout page that saves a new card for the coming charges.</summary>
    public async Task<BillingResult> StartCardChangeAsync(Firm firm, string adminEmail, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(cancellationToken);
        if (await PaidBillingAsync(connection, firm, cancellationToken) is not { } billing)
        {
            return NotPaying();
        }

        return await OpenCheckoutAsync(firm, CheckoutPurpose.Card, null, adminEmail, billing.Card?.CustomerId, cancellationToken);
    }

    /// <summary>A checkout page where the firm pays an open monthly charge, with the saved card or another one.</summary>
    public async Task<BillingResult> StartChargePaymentAsync(Firm firm, Guid chargeId, string adminEmail, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(cancellationToken);
        var charge = await BillingStore.GetChargeAsync(connection, chargeId, forUpdate: false, cancellationToken);
        if (charge is null || charge.FirmId != firm.Id)
        {
            return UnknownCharge();
        }

        if (charge is not { IsOpen: true, Kind: ChargeKind.Renewal })
        {
            return new BillingResult.Refused(StatusCodes.Status409Conflict, "Only an unpaid monthly charge can be paid here.");
        }

        var billing = await BillingStore.GetBillingAsync(connection, firm.Id, forUpdate: false, cancellationToken);
        return await OpenCheckoutAsync(firm, CheckoutPurpose.Payment, charge, adminEmail, billing?.Card?.CustomerId, cancellationToken);
    }

    /// <summary>Tries an open monthly charge on the saved card now.</summary>
    public async Task<BillingResult> RetryChargeAsync(Firm firm, Guid chargeId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var charge = await BillingStore.GetChargeAsync(connection, chargeId, forUpdate: true, cancellationToken);
            if (charge is null || charge.FirmId != firm.Id)
            {
                return UnknownCharge();
            }

            if (charge is not { IsOpen: true, Kind: ChargeKind.Renewal })
            {
                return new BillingResult.Refused(StatusCodes.Status409Conflict, "Only an unpaid monthly charge can be tried again.");
            }

            await BillingStore.UpdateChargeAsync(connection, charge.Id, "status = 'Pending', next_attempt_at = $2", [now], cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await AttemptAsync(chargeId, notifyOnDecline: false, cancellationToken) switch
        {
            AttemptOutcome.Paid or AttemptOutcome.NotDue => new BillingResult.Done(),
            AttemptOutcome.Declined declined => new BillingResult.Refused(StatusCodes.Status402PaymentRequired, $"The card was declined: {declined.Reason}"),
            _ => new BillingResult.Refused(StatusCodes.Status503ServiceUnavailable, "The payment provider could not be reached. The charge is tried again shortly."),
        };
    }

    /// <summary>
    /// The provider says the checkout page was completed. A payment marks its charge as paid and applies what it
    /// pays for, a saved card replaces the old one, and either way the card is used from now on. Completing the
    /// same page again changes nothing.
    /// </summary>
    public async Task<CompletionOutcome> CompleteCheckoutAsync(string checkoutId, CheckoutCompletion completion, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        Firm firm;
        PaidEffects effects;
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var checkout = await BillingStore.GetCheckoutAsync(connection, checkoutId, forUpdate: true, cancellationToken);
            if (checkout is null || firms.ById(checkout.FirmId) is not { } found)
            {
                return CompletionOutcome.Unknown;
            }

            firm = found;
            if (checkout.Status == CheckoutStatus.Completed)
            {
                return CompletionOutcome.AlreadyDone;
            }

            if (checkout.Purpose == CheckoutPurpose.Card)
            {
                await BillingStore.SaveCardAsync(connection, firm.Id, checkout.Provider, completion.Card, now, cancellationToken);
                await BillingStore.SetCheckoutStatusAsync(connection, checkout.Id, CheckoutStatus.Completed, now, cancellationToken);
                await BillingStore.AddEventAsync(connection, firm.Id, null, "card_saved", completion.Source, completion.Detail, now, cancellationToken);

                // Monthly charges that wait for a working card are tried with the new one at once.
                await BillingStore.ExecuteAsync(
                    connection,
                    "update billing_charges set status = 'Pending', next_attempt_at = $2 where firm_id = $1 and kind = 'Renewal' and status in ('Pending', 'Failed')",
                    [firm.Id, now],
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                signals.Billing.Set();
                return CompletionOutcome.Done;
            }

            var charge = (await BillingStore.GetChargeAsync(connection, checkout.ChargeId!.Value, forUpdate: true, cancellationToken))!;
            if (completion.Amount is { } amount && (amount != charge.Amount || !string.Equals(completion.Currency, charge.Currency, StringComparison.OrdinalIgnoreCase)))
            {
                await BillingStore.AddEventAsync(connection, firm.Id, charge.Id, "payment_mismatch", completion.Source, completion.Detail, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                LogPaymentMismatch(logger, charge.Number, firm.Id);
                return CompletionOutcome.AmountMismatch;
            }

            await BillingStore.SetCheckoutStatusAsync(connection, checkout.Id, CheckoutStatus.Completed, now, cancellationToken);
            await BillingStore.SaveCardAsync(connection, firm.Id, checkout.Provider, completion.Card, now, cancellationToken);
            if (charge.Status == ChargeStatus.Paid && charge.PaymentReference is not null && charge.PaymentReference == completion.PaymentReference)
            {
                await transaction.CommitAsync(cancellationToken);
                return CompletionOutcome.AlreadyDone;
            }

            // A charge paid in another way, a void one, a first payment for a firm that is already live, or a second
            // deposit, was paid twice. The extra payment is refunded by hand. A first payment or a deposit started
            // again still counts.
            var paidAlready = charge.Kind switch
            {
                ChargeKind.Activation => await BillingStore.LockFirmStatusAsync(connection, firm.Id, cancellationToken) == FirmStatus.Live,
                ChargeKind.Deposit => await BillingStore.DepositPaidAsync(connection, firm.Id, charge.Currency, cancellationToken) > 0,
                _ => false,
            };
            if (charge.Status == ChargeStatus.Paid || (charge.Status == ChargeStatus.Void && !IsStartedByFirm(charge.Kind)) || paidAlready)
            {
                await BillingStore.AddEventAsync(connection, firm.Id, charge.Id, "paid_twice", completion.Source, completion.Detail, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                LogPaidTwice(logger, charge.Number, firm.Id, completion.PaymentReference);
                return CompletionOutcome.AlreadyDone;
            }

            effects = await MarkPaidAsync(connection, firm, charge, completion.PaymentReference, completion.Source, completion.Detail, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await AfterPaidAsync(firm, effects, cancellationToken);
        return CompletionOutcome.Done;
    }

    /// <summary>The checkout page was not completed in time. A first payment or a deposit on it is void, so the firm starts again.</summary>
    public async Task ExpireCheckoutAsync(string checkoutId, string source, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await BillingStore.GetCheckoutAsync(connection, checkoutId, forUpdate: true, cancellationToken) is not { Status: CheckoutStatus.Open } checkout)
        {
            return;
        }

        await BillingStore.SetCheckoutStatusAsync(connection, checkout.Id, CheckoutStatus.Expired, now, cancellationToken);
        await BillingStore.AddEventAsync(connection, checkout.FirmId, checkout.ChargeId, "checkout_expired", source, null, now, cancellationToken);
        if (checkout.ChargeId is { } chargeId
            && await BillingStore.GetChargeAsync(connection, chargeId, forUpdate: true, cancellationToken) is { Status: ChargeStatus.Pending } started
            && IsStartedByFirm(started.Kind))
        {
            await VoidAsync(connection, started, "not_paid_in_time", source, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// A payment the provider reports outside a checkout page, for a charge of the saved card. A backup for when
    /// the service stopped before it recorded the answer to its own charge.
    /// </summary>
    public async Task ConfirmChargePaidAsync(Guid chargeId, string reference, decimal amount, string currency, string source, string? detail, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        Firm firm;
        PaidEffects effects;
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var charge = await BillingStore.GetChargeAsync(connection, chargeId, forUpdate: true, cancellationToken);
            if (charge is null || IsStartedByFirm(charge.Kind) || firms.ById(charge.FirmId) is not { } found)
            {
                return;
            }

            if (!charge.IsOpen || amount != charge.Amount || !string.Equals(currency, charge.Currency, StringComparison.OrdinalIgnoreCase))
            {
                // The same payment again changes nothing. Another one was taken twice, or for the wrong amount.
                if (charge.PaymentReference != reference)
                {
                    var problem = charge.IsOpen ? "payment_mismatch" : "paid_twice";
                    await BillingStore.AddEventAsync(connection, charge.FirmId, charge.Id, problem, source, detail, now, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    LogPaidTwice(logger, charge.Number, charge.FirmId, reference);
                }

                return;
            }

            firm = found;
            effects = await MarkPaidAsync(connection, firm, charge, reference, source, detail, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await AfterPaidAsync(firm, effects, cancellationToken);
    }

    /// <summary>
    /// Charges the saved card for a pending charge, with the charge locked so it is never charged twice at once. A
    /// declined monthly charge is tried again later, until it has been tried <see cref="BillingOptions.MaxAttempts"/>
    /// times. Declined slots are not bought. The administrators are emailed about a decline when
    /// <paramref name="notifyOnDecline"/> is set, since nobody is looking at the admin panel then.
    /// </summary>
    public async Task<AttemptOutcome> AttemptAsync(Guid chargeId, bool notifyOnDecline, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        Firm firm;
        Charge charge;
        string reason;
        DateTimeOffset? nextAttempt;
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var locked = await BillingStore.GetChargeAsync(connection, chargeId, forUpdate: true, cancellationToken);
            if (locked is not { Status: ChargeStatus.Pending } || firms.ById(locked.FirmId) is not { } found)
            {
                return new AttemptOutcome.NotDue();
            }

            (firm, charge) = (found, locked);
            var card = (await BillingStore.GetBillingAsync(connection, firm.Id, forUpdate: false, cancellationToken))?.Card;
            ChargeOutcome outcome;
            try
            {
                outcome = card is null
                    ? new ChargeOutcome.Declined("No card is saved.", null)
                    : await gateway.ChargeAsync(card, charge.ToPay(), charge.Attempts + 1, cancellationToken);
            }
            catch (BillingProviderUnavailableException exception)
            {
                LogUnavailable(logger, exception, charge.Number, firm.Id);
                await BillingStore.UpdateChargeAsync(
                    connection,
                    charge.Id,
                    exception.Answered ? "attempts = attempts + 1, next_attempt_at = $2" : "next_attempt_at = $2",
                    [now + UnavailableRetry],
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new AttemptOutcome.Unavailable();
            }

            if (outcome is ChargeOutcome.Paid paid)
            {
                await BillingStore.UpdateChargeAsync(connection, charge.Id, "attempts = attempts + 1", [], cancellationToken);
                var effects = await MarkPaidAsync(connection, firm, charge, paid.Reference, BillingSources.Platform, null, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                await AfterPaidAsync(firm, effects, cancellationToken);
                return new AttemptOutcome.Paid();
            }

            var declined = (ChargeOutcome.Declined)outcome;
            var attempts = charge.Attempts + 1;
            var retry = charge.Kind == ChargeKind.Renewal && attempts < options.Value.MaxAttempts;
            (reason, nextAttempt) = (declined.Reason, retry ? now + options.Value.RetryInterval : null);
            var status = retry ? ChargeStatus.Pending : charge.Kind == ChargeKind.Renewal ? ChargeStatus.Failed : ChargeStatus.Void;
            await BillingStore.UpdateChargeAsync(
                connection,
                charge.Id,
                "status = $2, attempts = $3, failure = $4, payment_reference = coalesce($5, payment_reference), next_attempt_at = $6, failed_at = $7",
                [status.ToString(), attempts, reason, BillingStore.Text(declined.Reference), BillingStore.NullableTimestamp(nextAttempt), now],
                cancellationToken);
            await BillingStore.AddEventAsync(connection, firm.Id, charge.Id, "declined", BillingSources.Platform, Detail(("reason", reason)), now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        LogDeclined(logger, charge.Number, firm.Id, reason);
        if (!notifyOnDecline)
        {
            return new AttemptOutcome.Declined(reason);
        }

        // Automatic expansion buys again after the retry interval, with a new charge.
        var tryAgain = nextAttempt ?? (charge.Kind == ChargeKind.Slots ? now + options.Value.RetryInterval : null);
        var pausedFrom = charge.Kind == ChargeKind.Renewal && charge.Month > BillingRules.MonthOf(now) ? BillingRules.StartOf(charge.Month) : (DateTimeOffset?)null;
        await EmailAdminsAsync(
            firm,
            to => PlatformEmails.PaymentDeclined(
                platform.Value.Name, firm.Name, to, charge.Description, Money(charge.Amount, charge.Currency), reason, tryAgain, pausedFrom, BillingUrl(firm)),
            cancellationToken);
        return new AttemptOutcome.Declined(reason);
    }

    /// <summary>
    /// One round of the background work for a firm that pays: charge the coming month when it is due, pause or
    /// resume its challenges after whether this month is paid, buy more slots when the last one is taken and
    /// automatic expansion is on, and warn when most slots are taken.
    /// </summary>
    public async Task RunFirmAsync(Firm firm, CancellationToken cancellationToken)
    {
        await CreateRenewalIfDueAsync(firm, cancellationToken);

        var usage = await slots.UsageAsync(firm, cancellationToken);
        FirmBilling billing;
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            billing = (await BillingStore.GetBillingAsync(connection, firm.Id, forUpdate: false, cancellationToken))!;
        }

        await KeepStandingAsync(firm, billing, usage.Paid, cancellationToken);
        if (usage.Paid && usage.Free == 0 && billing.AutoExpandStep is { } step && usage.Slots < Terms.MaxSlots)
        {
            await ExpandAsync(firm, Math.Min(usage.Slots!.Value + step, Terms.MaxSlots), cancellationToken);
            usage = await slots.UsageAsync(firm, cancellationToken);
        }

        await WarnIfNearlyFullAsync(firm, billing, usage, cancellationToken);
    }

    /// <summary>The month that is due next is charged to the saved card, with the slots the firm has chosen, or as many as are taken.</summary>
    private async Task CreateRenewalIfDueAsync(Firm firm, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = BillingRules.MonthOf(now).AddMonths(1);
        if (now < BillingRules.ChargeTimeOf(due, Terms.ChargeDaysBeforeMonth))
        {
            due = BillingRules.MonthOf(now);
        }

        await using var connection = await store.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SlotService.LockAsync(connection, firm.Id, cancellationToken);
        var billing = await BillingStore.GetBillingAsync(connection, firm.Id, forUpdate: true, cancellationToken);
        if (billing is not { Plan: BillingPlan.Paid, ActivatedAt: not null }
            || await BillingStore.GetPeriodAsync(connection, firm.Id, due, cancellationToken) is not null
            || (await BillingStore.OpenChargesAsync(connection, firm.Id, cancellationToken)).Any(c => c.Kind == ChargeKind.Renewal && c.Month == due))
        {
            return;
        }

        var usage = await slots.UsageAsync(connection, firm, null, cancellationToken);
        var slotCount = Math.Max(billing.Slots ?? Terms.MinSlots, usage.Used + usage.Reserved);
        await InsertChargeAsync(connection, firm.Id, ChargeKind.Renewal, due, 1, slotCount, BillingRules.Renewal(due, slotCount, Terms), now, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Marks when this month began unpaid and emails the administrators, then pauses or resumes the firm's
    /// challenges. Challenges that started or ended in between are caught up the next time.
    /// </summary>
    private async Task KeepStandingAsync(Firm firm, FirmBilling billing, bool paid, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        // A payment may have come since the usage was read.
        paid = paid || await IsMonthPaidAsync(firm, cancellationToken);
        if (!paid && billing.UnpaidSince is null)
        {
            await using (var connection = await store.OpenAsync(cancellationToken))
            {
                await BillingStore.UpdateBillingAsync(connection, firm.Id, "unpaid_since = $2", [now], now, cancellationToken);
                await BillingStore.AddEventAsync(connection, firm.Id, null, "paused", BillingSources.Platform, null, now, cancellationToken);
            }

            LogPaused(logger, firm.Id);
            await EmailAdminsAsync(firm, to => PlatformEmails.FirmPaused(platform.Value.Name, firm.Name, to, BillingUrl(firm)), cancellationToken);
        }
        else if (paid && billing.UnpaidSince is not null)
        {
            await using var connection = await store.OpenAsync(cancellationToken);
            await BillingStore.UpdateBillingAsync(connection, firm.Id, "unpaid_since = null", [], now, cancellationToken);
            await BillingStore.AddEventAsync(connection, firm.Id, null, "resumed", BillingSources.Platform, null, now, cancellationToken);
        }

        await KeepChallengesStandingAsync(firm, cancellationToken);
    }

    /// <summary>
    /// Pauses the firm's challenges while we have suspended it or its month is unpaid, and resumes them once neither
    /// holds. One firm at a time, and what holds is read again before each challenge, so the latest decision wins.
    /// </summary>
    public async Task KeepChallengesStandingAsync(Firm firm, CancellationToken cancellationToken)
    {
        var gate = _standing.GetOrAdd(firm.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            List<(Guid Id, bool Paused)> accounts;
            bool run;
            await using (var connection = await store.OpenAsync(cancellationToken))
            {
                run = await ShouldRunAsync(connection, firm.Id, cancellationToken);
                accounts = await BillingStore.OpenAccountsAsync(connection, firm.Id, cancellationToken);
            }

            foreach (var (accountId, _) in accounts.Where(a => a.Paused == run))
            {
                await using (var connection = await store.OpenAsync(cancellationToken))
                {
                    if (await ShouldRunAsync(connection, firm.Id, cancellationToken) != run)
                    {
                        // Decided again meanwhile. Whoever changed it runs this again.
                        return;
                    }
                }

                var now = time.GetUtcNow();
                await challenges.ApplyAsync(
                    firm,
                    accountId,
                    state =>
                    {
                        var day = TradingDays.DayOf(now, state.Definition.TradingDay);
                        return run ? new ResumeChallenge(now, day) : new PauseChallenge(now, day);
                    },
                    cancellationToken);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    // Whether the firm's challenges may run: it is not suspended, and a firm that pays by card has paid this month.
    private async Task<bool> ShouldRunAsync(NpgsqlConnection connection, string firmId, CancellationToken cancellationToken)
    {
        var state = await BillingStore.GoLiveStateAsync(connection, firmId, forUpdate: false, cancellationToken);
        if (state.Suspended)
        {
            return false;
        }

        return state.Status != FirmStatus.Live
            || await BillingStore.GetBillingAsync(connection, firmId, forUpdate: false, cancellationToken) is not { Plan: BillingPlan.Paid, ActivatedAt: not null }
            || await BillingStore.GetPeriodAsync(connection, firmId, BillingRules.MonthOf(time.GetUtcNow()), cancellationToken) is not null;
    }

    /// <summary>
    /// Buys more slots for a firm with automatic expansion, unless a purchase is under way, or one was declined
    /// within the retry interval.
    /// </summary>
    private async Task ExpandAsync(Firm firm, int slotCount, CancellationToken cancellationToken)
    {
        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            if (await HasOpenSlotsChargeAsync(connection, firm.Id, cancellationToken)
                || await BillingStore.LastDeclinedSlotsAsync(connection, firm.Id, cancellationToken) is { } declined
                    && declined > time.GetUtcNow() - options.Value.RetryInterval)
            {
                return;
            }
        }

        if (await ChangeSlotsAsync(firm, slotCount, automatic: true, cancellationToken) is BillingResult.Refused refused)
        {
            LogExpansionFailed(logger, firm.Id, refused.Problem);
        }
    }

    /// <summary>Emails the administrators once when most slots are taken, and again only after usage fell below it.</summary>
    private async Task WarnIfNearlyFullAsync(Firm firm, FirmBilling billing, SlotUsage usage, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var nearlyFull = usage.IsNearlyFull(options.Value.WarningPercent);
        if (nearlyFull == billing.SlotsWarnedAt is not null)
        {
            return;
        }

        await using (var connection = await store.OpenAsync(cancellationToken))
        {
            await BillingStore.UpdateBillingAsync(connection, firm.Id, "slots_warned_at = $2", [BillingStore.NullableTimestamp(nearlyFull ? now : null)], now, cancellationToken);
        }

        if (nearlyFull)
        {
            await EmailAdminsAsync(
                firm,
                to => PlatformEmails.SlotsNearlyFull(platform.Value.Name, firm.Name, to, usage.Used + usage.Reserved, usage.Slots!.Value, billing.AutoExpandStep is not null, BillingUrl(firm)),
                cancellationToken);
        }
    }

    /// <summary>
    /// Marks the charge as paid in the caller's transaction and applies what it pays for. A deposit sends the firm's
    /// application. A first payment takes the firm live, ends its sandbox accounts, lets its unpaid sandbox orders
    /// expire and voids other first payments that were started.
    /// </summary>
    private async Task<PaidEffects> MarkPaidAsync(
        NpgsqlConnection connection,
        Firm firm,
        Charge charge,
        string? reference,
        string source,
        string? detail,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await BillingStore.UpdateChargeAsync(
            connection,
            charge.Id,
            "status = 'Paid', paid_at = $2, payment_reference = coalesce($3, payment_reference), next_attempt_at = null",
            [now, BillingStore.Text(reference)],
            cancellationToken);
        await BillingStore.AddEventAsync(connection, firm.Id, charge.Id, "paid", source, detail, now, cancellationToken);
        if (charge.Months > 0)
        {
            await BillingStore.PayMonthsAsync(connection, firm.Id, charge.Month, charge.Months, charge.Slots, now, cancellationToken);
        }

        switch (charge.Kind)
        {
            case ChargeKind.Deposit:
                return new PaidEffects(WentLive: false, [], await ReviewStore.SubmitPaidDraftAsync(connection, firm.Id, charge.Number, now, cancellationToken));
            case ChargeKind.Activation:
                await SlotService.LockAsync(connection, firm.Id, cancellationToken);
                if (await BillingStore.LockFirmStatusAsync(connection, firm.Id, cancellationToken) == FirmStatus.Live)
                {
                    break;
                }

                await BillingStore.UpdateBillingAsync(connection, firm.Id, "activated_at = $2, unpaid_since = null", [now], now, cancellationToken);
                await FirmStore.SetLiveAsync(connection, firm.Id, now, cancellationToken);
                foreach (var (accountId, _) in await BillingStore.OpenAccountsAsync(connection, firm.Id, cancellationToken))
                {
                    await challenges.ApplyAsync(
                        connection, firm, accountId, _ => new CancelChallenge(now, "The firm went live, so its accounts from the sandbox end."), null, cancellationToken);
                }

                await BillingStore.ExecuteAsync(
                    connection,
                    """
                    with expired as (update orders set status = 'Expired' where firm_id = $1 and status = 'Pending' returning id)
                    insert into order_events (order_id, type, recorded_at, source, detail) select id, 'expired', $2, 'platform', null from expired
                    """,
                    [firm.Id, now],
                    cancellationToken);
                List<string> abandoned = [];
                foreach (var other in (await BillingStore.OpenChargesAsync(connection, firm.Id, cancellationToken)).Where(c => c.Kind == ChargeKind.Activation))
                {
                    abandoned.AddRange(await VoidAsync(connection, other, "another_went_live", source, now, cancellationToken) ?? []);
                }

                return new PaidEffects(WentLive: true, abandoned);
            case ChargeKind.Slots:
                await BillingStore.UpdateBillingAsync(connection, firm.Id, "slots = greatest(slots, $2)", [charge.Slots], now, cancellationToken);
                return new PaidEffects(WentLive: false, await CarrySlotsForwardAsync(connection, firm, charge, now, cancellationToken));
        }

        return new PaidEffects(WentLive: false, []);
    }

    /// <summary>
    /// Bought slots also hold in the later months. A monthly charge that waits to be paid with fewer is made again
    /// with the new slots. A later month that was paid with fewer meanwhile gets a charge for the rest. Returns
    /// the checkout pages of charges made again, to close at the provider too.
    /// </summary>
    private async Task<List<string>> CarrySlotsForwardAsync(NpgsqlConnection connection, Firm firm, Charge bought, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<string> abandoned = [];
        foreach (var renewal in (await BillingStore.OpenChargesAsync(connection, firm.Id, cancellationToken))
                     .Where(c => c.Kind == ChargeKind.Renewal && c.Month > bought.Month && c.Slots < bought.Slots))
        {
            // A charge being paid right now is not made again; it is caught below once it is paid.
            if (await VoidAsync(connection, renewal, "replaced", BillingSources.Platform, now, cancellationToken) is { } pages)
            {
                abandoned.AddRange(pages);
                await InsertChargeAsync(
                    connection, firm.Id, ChargeKind.Renewal, renewal.Month, 1, bought.Slots, BillingRules.Renewal(renewal.Month, bought.Slots, Terms), now, now, cancellationToken);
            }
        }

        var later = bought.Month.AddMonths(bought.Months);
        if (await BillingStore.GetPeriodAsync(connection, firm.Id, later, cancellationToken) is { } paid && paid.Slots < bought.Slots)
        {
            await InsertChargeAsync(
                connection, firm.Id, ChargeKind.Slots, later, 1, bought.Slots, BillingRules.MoreSlotsInMonth(later, paid.Slots, bought.Slots, Terms), now, now, cancellationToken);
        }

        return abandoned;
    }

    // After the payment is saved: the firm as saved, so every request sees that it is live, our staff told about a
    // new application, and the workers woken.
    private async Task AfterPaidAsync(Firm firm, PaidEffects effects, CancellationToken cancellationToken)
    {
        if (effects.WentLive)
        {
            firms.Put(await firmStore.GetAsync(firm.Id, cancellationToken) ?? firm);
            challenges.Notify(firm);
        }

        if (effects.Submitted)
        {
            await staff.ApplicationSubmittedAsync(firm, cancellationToken);
        }

        foreach (var checkoutId in effects.Abandoned)
        {
            await gateway.ExpireCheckoutAsync(checkoutId, cancellationToken);
        }

        signals.Billing.Set();
    }

    private async Task<BillingResult> OpenCheckoutAsync(Firm firm, CheckoutPurpose purpose, Charge? charge, string adminEmail, string? customerId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var expiresAt = now + options.Value.CheckoutLifetime;
        CheckoutPage page;
        try
        {
            page = await gateway.CreateCheckoutAsync(
                new CheckoutRequest(firm.Id, adminEmail, purpose, charge?.ToPay(), customerId, firm.Portal.Url, ReturnPathOf(charge?.Kind), expiresAt),
                cancellationToken);
        }
        catch (BillingProviderUnavailableException exception)
        {
            LogCheckoutNotStarted(logger, exception, firm.Id);
            if (charge is not null && IsStartedByFirm(charge.Kind))
            {
                await using var voiding = await store.OpenAsync(cancellationToken);
                await VoidAsync(voiding, charge, "checkout_not_started", BillingSources.Platform, now, cancellationToken);
            }

            return new BillingResult.Refused(StatusCodes.Status503ServiceUnavailable, "Payments are not working right now. Try again shortly.");
        }

        await using var connection = await store.OpenAsync(cancellationToken);
        await BillingStore.InsertCheckoutAsync(
            connection,
            new BillingCheckout(page.Id, firm.Id, gateway.Provider, purpose, charge?.Id, page.Url, CheckoutStatus.Open, now, expiresAt, null),
            cancellationToken);
        await BillingStore.AddEventAsync(connection, firm.Id, charge?.Id, "checkout_opened", BillingSources.Admin, Detail(("checkoutId", page.Id)), now, cancellationToken);
        return new BillingResult.Checkout(page.Url);
    }

    /// <summary>
    /// Voids the charge, if it is still open, and closes its open checkout pages. Returns the pages to expire at the
    /// provider too, or null when the charge was no longer open, for example because it was paid meanwhile.
    /// </summary>
    private static async Task<List<string>?> VoidAsync(NpgsqlConnection connection, Charge charge, string why, string source, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await BillingStore.ExecuteAsync(
                connection,
                "update billing_charges set status = 'Void', next_attempt_at = null where id = $1 and status in ('Pending', 'Failed')",
                [charge.Id],
                cancellationToken) == 0)
        {
            return null;
        }

        await BillingStore.AddEventAsync(connection, charge.FirmId, charge.Id, "void", source, Detail(("reason", why)), now, cancellationToken);
        var open = await BillingStore.OpenCheckoutsAsync(connection, charge.FirmId, charge.Id, cancellationToken);
        foreach (var checkout in open)
        {
            await BillingStore.SetCheckoutStatusAsync(connection, checkout.Id, CheckoutStatus.Expired, now, cancellationToken);
        }

        return [.. open.Select(c => c.Id)];
    }

    private async Task<Charge> InsertChargeAsync(
        NpgsqlConnection connection,
        string firmId,
        ChargeKind kind,
        DateOnly month,
        int months,
        int slotCount,
        IReadOnlyList<ChargeLine> lines,
        DateTimeOffset? nextAttemptAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var charge = new Charge(
            Guid.CreateVersion7(now),
            firmId,
            await BillingStore.NextChargeNumberAsync(connection, cancellationToken),
            kind,
            ChargeStatus.Pending,
            month,
            months,
            slotCount,
            lines,
            lines.Sum(l => l.Amount),
            Terms.Currency,
            gateway.Provider,
            null,
            null,
            0,
            nextAttemptAt,
            now,
            null,
            null);
        await BillingStore.InsertChargeAsync(connection, charge, cancellationToken);
        await BillingStore.AddEventAsync(connection, firmId, charge.Id, "created", BillingSources.Platform, null, now, cancellationToken);
        return charge;
    }

    private async Task<bool> IsMonthPaidAsync(Firm firm, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(cancellationToken);
        return await BillingStore.GetPeriodAsync(connection, firm.Id, BillingRules.MonthOf(time.GetUtcNow()), cancellationToken) is not null;
    }

    // A charge someone else tried meanwhile, as it ended.
    private async Task<AttemptOutcome> SettledOutcomeAsync(Guid chargeId, CancellationToken cancellationToken)
    {
        await using var connection = await store.OpenAsync(cancellationToken);
        return await BillingStore.GetChargeAsync(connection, chargeId, forUpdate: false, cancellationToken) switch
        {
            { Status: ChargeStatus.Paid } => new AttemptOutcome.Paid(),
            { Status: ChargeStatus.Void or ChargeStatus.Failed } settled => new AttemptOutcome.Declined(settled.Failure ?? "The card was declined."),
            _ => new AttemptOutcome.Unavailable(),
        };
    }

    private static async Task<FirmBilling?> PaidBillingAsync(NpgsqlConnection connection, Firm firm, CancellationToken cancellationToken) =>
        firm.Status == FirmStatus.Live
        && await BillingStore.GetBillingAsync(connection, firm.Id, forUpdate: false, cancellationToken) is { Plan: BillingPlan.Paid, ActivatedAt: not null } billing
            ? billing
            : null;

    private static async Task<bool> HasOpenSlotsChargeAsync(NpgsqlConnection connection, string firmId, CancellationToken cancellationToken) =>
        (await BillingStore.OpenChargesAsync(connection, firmId, cancellationToken)).Any(c => c.Kind == ChargeKind.Slots);

    // Paid only on a checkout page the firm opened, never by charging the saved card, and void when it is not paid in time.
    private static bool IsStartedByFirm(ChargeKind kind) => kind is ChargeKind.Activation or ChargeKind.Deposit;

    // Best effort: the admin panel shows the same, and a failed email is only logged.
    private async Task EmailAdminsAsync(Firm firm, Func<string, EmailMessage> message, CancellationToken cancellationToken)
    {
        foreach (var admin in await admins.ListAsync(firm.Id, cancellationToken))
        {
            try
            {
                await email.SendAsync(message(admin.Email), cancellationToken);
            }
            catch (EmailNotSentException exception)
            {
                LogEmailNotSent(logger, exception, firm.Id);
            }
        }
    }

    private static Uri BillingUrl(Firm firm) => new(firm.Portal.Url, "admin/billing");

    private static string Money(decimal amount, string currency) => string.Create(CultureInfo.InvariantCulture, $"{amount:N2} {currency}");

    private static string Detail(params (string Key, object? Value)[] values) =>
        System.Text.Json.JsonSerializer.Serialize(values.ToDictionary(v => v.Key, v => v.Value));

    private static BillingResult.Refused NotPaying() =>
        new(StatusCodes.Status409Conflict, "The firm does not pay for its slots by card. Go live first.");

    private static BillingResult.Refused UnknownCharge() => new(StatusCodes.Status404NotFound, "The firm has no such charge.");

    /// <summary>
    /// What paying a charge changed beyond the charge: whether the firm went live, checkout pages no longer wanted,
    /// and whether the firm's application was sent.
    /// </summary>
    private sealed record PaidEffects(bool WentLive, IReadOnlyList<string> Abandoned, bool Submitted = false);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The billing provider did not start a checkout for firm {FirmId}")]
    private static partial void LogCheckoutNotStarted(ILogger logger, Exception exception, string firmId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The billing provider could not be reached for charge {ChargeNumber} of firm {FirmId}; it is tried again shortly")]
    private static partial void LogUnavailable(ILogger logger, Exception exception, long chargeNumber, string firmId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Charge {ChargeNumber} of firm {FirmId} was declined: {Reason}")]
    private static partial void LogDeclined(ILogger logger, long chargeNumber, string firmId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "A payment for charge {ChargeNumber} of firm {FirmId} does not match the charge; nothing was applied")]
    private static partial void LogPaymentMismatch(ILogger logger, long chargeNumber, string firmId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Charge {ChargeNumber} of firm {FirmId} was paid twice ({Reference}); refund the extra payment")]
    private static partial void LogPaidTwice(ILogger logger, long chargeNumber, string firmId, string? reference);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Firm {FirmId} is paused: this month is not paid")]
    private static partial void LogPaused(ILogger logger, string firmId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Automatic expansion for firm {FirmId} did not buy slots: {Problem}")]
    private static partial void LogExpansionFailed(ILogger logger, string firmId, string problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A billing email to an administrator of firm {FirmId} could not be sent")]
    private static partial void LogEmailNotSent(ILogger logger, Exception exception, string firmId);
}
