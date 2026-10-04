using System.Text.Json;

using Common.Postgres;

using Npgsql;

using NpgsqlTypes;

using Prop.Api.Json;
using Prop.Api.Review;

namespace Prop.Api.Billing;

/// <summary>How a firm has its slots: paid by card in advance, or given without charges.</summary>
public enum BillingPlan
{
    Paid,

    /// <summary>Slots without charges, for configured firms.</summary>
    Complimentary,
}

public enum ChargeKind
{
    /// <summary>The first payment, with the startup fee, which takes the firm live.</summary>
    Activation,

    /// <summary>A month's slots, charged before the month starts.</summary>
    Renewal,

    /// <summary>More slots during a month.</summary>
    Slots,

    /// <summary>The deposit for our review, paid when the firm sends its application and taken off the startup fee (ADR 0021).</summary>
    Deposit,
}

public enum ChargeStatus
{
    /// <summary>Not paid yet. The saved card is tried at the next attempt, or the firm pays on a checkout page.</summary>
    Pending,

    Paid,

    /// <summary>The card was declined and is not tried again by itself. The firm can still pay it.</summary>
    Failed,

    /// <summary>No longer to be paid, for example a first payment the firm started again.</summary>
    Void,
}

public enum CheckoutStatus
{
    Open,
    Completed,
    Expired,
}

/// <summary>
/// How a firm pays us. <paramref name="Slots"/> is a complimentary firm's limit, or the slots a paid firm is
/// charged for from the next unpaid month. <paramref name="UnpaidSince"/> is set while the current month is unpaid.
/// </summary>
internal sealed record FirmBilling(
    string FirmId,
    BillingPlan Plan,
    int? Slots,
    int? AutoExpandStep,
    BillingProvider? Provider,
    SavedCard? Card,
    DateTimeOffset? SlotsWarnedAt,
    DateTimeOffset? UnpaidSince,
    DateTimeOffset? ActivatedAt);

/// <summary>A month a paid firm has paid for, with the slots it has in it.</summary>
internal sealed record BillingPeriod(DateOnly Month, int Slots, DateTimeOffset PaidAt);

/// <summary>
/// What a firm is charged. Once paid, the firm has <paramref name="Slots"/> in <paramref name="Months"/> months from
/// <paramref name="Month"/>. <paramref name="NextAttemptAt"/> is when the saved card is charged next, or null when
/// the charge waits for the firm.
/// </summary>
internal sealed record Charge(
    Guid Id,
    string FirmId,
    long Number,
    ChargeKind Kind,
    ChargeStatus Status,
    DateOnly Month,
    int Months,
    int Slots,
    IReadOnlyList<ChargeLine> Lines,
    decimal Amount,
    string Currency,
    BillingProvider Provider,
    string? PaymentReference,
    string? Failure,
    int Attempts,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? FailedAt)
{
    public bool IsOpen => Status is ChargeStatus.Pending or ChargeStatus.Failed;

    public string Description => Kind switch
    {
        ChargeKind.Activation => $"Going live, charge {Number}",
        ChargeKind.Renewal => $"Slots for {BillingRules.NameOf(Month)}, charge {Number}",
        ChargeKind.Deposit => $"Review deposit, charge {Number}",
        _ => $"More slots, charge {Number}",
    };

    public ChargeToPay ToPay() => new(Id, Number, Description, Lines, Amount, Currency);
}

/// <summary>What decides whether a firm may go live: its status, whether we suspended it, and our review of it.</summary>
internal sealed record GoLiveState(Firms.FirmStatus Status, bool Suspended, ReviewStatus? Review);

/// <summary>A page where the firm pays a charge or saves a card at the provider.</summary>
internal sealed record BillingCheckout(
    string Id,
    string FirmId,
    BillingProvider Provider,
    CheckoutPurpose Purpose,
    Guid? ChargeId,
    Uri Url,
    CheckoutStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt);

/// <summary>Who changed a firm's billing.</summary>
internal static class BillingSources
{
    public const string Admin = "admin";
    public const string Platform = "platform";
    public const string Stripe = "stripe";
    public const string Test = "test";
}

/// <summary>Billing in the database. Changes that must happen together run in the caller's transaction.</summary>
internal sealed class BillingStore(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    private const string SelectBilling =
        """
        select firm_id, plan, slots, auto_expand_step, provider, customer_id, payment_method_id, card_brand, card_last4, card_exp_month, card_exp_year,
               slots_warned_at, unpaid_since, activated_at
        from firm_billing
        """;

    private const string SelectCharge =
        """
        select id, firm_id, number, kind, status, month, months, slots, lines, amount, currency, provider, payment_reference, failure, attempts,
               next_attempt_at, created_at, paid_at, failed_at
        from billing_charges
        """;

    private const string SelectCheckout =
        "select id, firm_id, provider, purpose, charge_id, url, status, created_at, expires_at, completed_at from billing_checkouts";

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        return await dataSource.OpenConnectionAsync(cancellationToken);
    }

    public static async Task<FirmBilling?> GetBillingAsync(NpgsqlConnection connection, string firmId, bool forUpdate, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, $"{SelectBilling} where firm_id = $1{(forUpdate ? " for update" : "")}", [firmId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var card = reader.IsDBNull(6)
            ? null
            : new SavedCard(
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetInt32(9),
                reader.GetInt32(10));
        return new FirmBilling(
            reader.GetString(0),
            Enum.Parse<BillingPlan>(reader.GetString(1)),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.IsDBNull(4) ? null : Enum.Parse<BillingProvider>(reader.GetString(4)),
            card,
            NullableTime(reader, 11),
            NullableTime(reader, 12),
            NullableTime(reader, 13));
    }

    /// <summary>A configured firm's slots, without charges. Its billing is replaced at every start.</summary>
    public async Task SaveComplimentaryAsync(string firmId, int? slots, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteAsync(
            connection,
            """
            insert into firm_billing (firm_id, plan, slots, created_at, updated_at) values ($1, $2, $3, $4, $4)
            on conflict (firm_id) do update set plan = excluded.plan, slots = excluded.slots, auto_expand_step = null, updated_at = excluded.updated_at
            """,
            [firmId, BillingPlan.Complimentary.ToString(), Int(slots), now],
            cancellationToken);
    }

    /// <summary>
    /// A billing row for a firm in the sandbox that starts paying us, so the card it pays with is saved. Keeps the
    /// row the firm already has.
    /// </summary>
    public static Task<int> EnsurePaidPlanAsync(NpgsqlConnection connection, string firmId, BillingProvider provider, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            "insert into firm_billing (firm_id, plan, provider, created_at, updated_at) values ($1, $2, $3, $4, $4) on conflict (firm_id) do nothing",
            [firmId, BillingPlan.Paid.ToString(), provider.ToString(), now],
            cancellationToken);

    /// <summary>What the firm has paid as deposit for our review, in the currency. 0 when nothing.</summary>
    public static async Task<decimal> DepositPaidAsync(NpgsqlConnection connection, string firmId, string currency, CancellationToken cancellationToken)
    {
        await using var command = Command(
            connection,
            "select coalesce(sum(amount), 0) from billing_charges where firm_id = $1 and kind = 'Deposit' and status = 'Paid' and currency = $2",
            [firmId, currency]);
        return (decimal)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>The slots and automatic expansion a firm in the sandbox chose when it started paying to go live.</summary>
    public static Task<int> SavePaidPlanAsync(
        NpgsqlConnection connection,
        string firmId,
        BillingProvider provider,
        int slots,
        int? autoExpandStep,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            """
            insert into firm_billing (firm_id, plan, slots, auto_expand_step, provider, created_at, updated_at) values ($1, $2, $3, $4, $5, $6, $6)
            on conflict (firm_id) do update set plan = excluded.plan, slots = excluded.slots, auto_expand_step = excluded.auto_expand_step,
                provider = excluded.provider, updated_at = excluded.updated_at
            """,
            [firmId, BillingPlan.Paid.ToString(), slots, Int(autoExpandStep), provider.ToString(), now],
            cancellationToken);

    /// <summary>Changes the firm's billing row with <paramref name="set"/>, whose values start at $2.</summary>
    public static Task<int> UpdateBillingAsync(NpgsqlConnection connection, string firmId, string set, object[] values, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync(connection, $"update firm_billing set {set}, updated_at = ${values.Length + 2} where firm_id = $1", [firmId, .. values, now], cancellationToken);

    public static Task<int> SaveCardAsync(NpgsqlConnection connection, string firmId, BillingProvider provider, SavedCard card, DateTimeOffset now, CancellationToken cancellationToken) =>
        UpdateBillingAsync(
            connection,
            firmId,
            """
            provider = $2, customer_id = coalesce($3, customer_id), payment_method_id = $4, card_brand = $5, card_last4 = $6,
            card_exp_month = $7, card_exp_year = $8
            """,
            [provider.ToString(), Text(card.CustomerId), card.PaymentMethodId, card.Brand, card.Last4, card.ExpMonth, card.ExpYear],
            now,
            cancellationToken);

    public static async Task<BillingPeriod?> GetPeriodAsync(NpgsqlConnection connection, string firmId, DateOnly month, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, "select month, slots, paid_at from billing_periods where firm_id = $1 and month = $2", [firmId, month]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPeriod(reader) : null;
    }

    /// <summary>The latest paid month up to <paramref name="month"/>, for the slots an unpaid firm had.</summary>
    public static async Task<BillingPeriod?> LatestPeriodAsync(NpgsqlConnection connection, string firmId, DateOnly month, CancellationToken cancellationToken)
    {
        await using var command = Command(
            connection,
            "select month, slots, paid_at from billing_periods where firm_id = $1 and month <= $2 order by month desc limit 1",
            [firmId, month]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPeriod(reader) : null;
    }

    /// <summary>The months are paid with at least <paramref name="slots"/> slots. A month already paid keeps more slots if it had them.</summary>
    public static Task<int> PayMonthsAsync(NpgsqlConnection connection, string firmId, DateOnly month, int months, int slots, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            """
            insert into billing_periods (firm_id, month, slots, paid_at)
            select $1, (($2::date + make_interval(months => m)))::date, $4, $5 from generate_series(0, $3 - 1) m
            on conflict (firm_id, month) do update set slots = greatest(billing_periods.slots, excluded.slots)
            """,
            [firmId, month, months, slots, now],
            cancellationToken);

    public static async Task<long> NextChargeNumberAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, "select nextval('billing_charge_numbers')", []);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public static Task<int> InsertChargeAsync(NpgsqlConnection connection, Charge charge, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            """
            insert into billing_charges (id, firm_id, number, kind, status, month, months, slots, lines, amount, currency, provider, attempts, next_attempt_at, created_at)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, 0, $13, $14)
            """,
            [
                charge.Id, charge.FirmId, charge.Number, charge.Kind.ToString(), charge.Status.ToString(), charge.Month, charge.Months, charge.Slots,
                Jsonb(JsonSerializer.Serialize(charge.Lines, PropJson.Options)), charge.Amount, charge.Currency, charge.Provider.ToString(),
                NullableTimestamp(charge.NextAttemptAt), charge.CreatedAt,
            ],
            cancellationToken);

    /// <summary>Changes the charge with <paramref name="set"/>, whose values start at $2.</summary>
    public static Task<int> UpdateChargeAsync(NpgsqlConnection connection, Guid chargeId, string set, object[] values, CancellationToken cancellationToken) =>
        ExecuteAsync(connection, $"update billing_charges set {set} where id = $1", [chargeId, .. values], cancellationToken);

    public static async Task<Charge?> GetChargeAsync(NpgsqlConnection connection, Guid chargeId, bool forUpdate, CancellationToken cancellationToken) =>
        (await ReadChargesAsync(connection, $"{SelectCharge} where id = $1{(forUpdate ? " for update" : "")}", [chargeId], cancellationToken)).SingleOrDefault();

    /// <summary>The firm's charges that are not paid or void, oldest first.</summary>
    public static Task<List<Charge>> OpenChargesAsync(NpgsqlConnection connection, string firmId, CancellationToken cancellationToken) =>
        ReadChargesAsync(connection, $"{SelectCharge} where firm_id = $1 and status in ('Pending', 'Failed') order by created_at", [firmId], cancellationToken);

    /// <summary>When slots the firm tried to buy were last declined, or null.</summary>
    public static async Task<DateTimeOffset?> LastDeclinedSlotsAsync(NpgsqlConnection connection, string firmId, CancellationToken cancellationToken)
    {
        await using var command = Command(
            connection, "select max(failed_at) from billing_charges where firm_id = $1 and kind = 'Slots' and status = 'Void' and failed_at is not null", [firmId]);
        return await command.ExecuteScalarAsync(cancellationToken) is DateTime failedAt ? new DateTimeOffset(failedAt, TimeSpan.Zero) : null;
    }

    /// <summary>The firm's newest charges.</summary>
    public async Task<List<Charge>> ListChargesAsync(string firmId, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await ReadChargesAsync(connection, $"{SelectCharge} where firm_id = $1 order by created_at desc limit $2", [firmId, limit], cancellationToken);
    }

    /// <summary>Charges whose saved card is due to be tried.</summary>
    public async Task<List<(Guid Id, string FirmId)>> DueChargesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            "select id, firm_id from billing_charges where status = 'Pending' and next_attempt_at <= $1 order by next_attempt_at",
            [now]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var due = new List<(Guid, string)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            due.Add((reader.GetGuid(0), reader.GetString(1)));
        }

        return due;
    }

    public static Task<int> InsertCheckoutAsync(NpgsqlConnection connection, BillingCheckout checkout, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            """
            insert into billing_checkouts (id, firm_id, provider, purpose, charge_id, url, status, created_at, expires_at)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            """,
            [
                checkout.Id, checkout.FirmId, checkout.Provider.ToString(), checkout.Purpose.ToString(),
                new NpgsqlParameter { Value = (object?)checkout.ChargeId ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid },
                checkout.Url.AbsoluteUri, checkout.Status.ToString(), checkout.CreatedAt, checkout.ExpiresAt,
            ],
            cancellationToken);

    public static async Task<BillingCheckout?> GetCheckoutAsync(NpgsqlConnection connection, string checkoutId, bool forUpdate, CancellationToken cancellationToken) =>
        (await ReadCheckoutsAsync(connection, $"{SelectCheckout} where id = $1{(forUpdate ? " for update" : "")}", [checkoutId], cancellationToken)).SingleOrDefault();

    public async Task<BillingCheckout?> GetCheckoutAsync(string checkoutId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await GetCheckoutAsync(connection, checkoutId, forUpdate: false, cancellationToken);
    }

    /// <summary>The open checkout pages of a charge, or of the firm's card when <paramref name="chargeId"/> is null.</summary>
    public static Task<List<BillingCheckout>> OpenCheckoutsAsync(NpgsqlConnection connection, string firmId, Guid? chargeId, CancellationToken cancellationToken) =>
        ReadCheckoutsAsync(
            connection,
            $"{SelectCheckout} where firm_id = $1 and status = 'Open' and charge_id is not distinct from $2",
            [firmId, new NpgsqlParameter { Value = (object?)chargeId ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid }],
            cancellationToken);

    /// <summary>Open checkout pages whose time is up.</summary>
    public async Task<List<BillingCheckout>> ExpiredCheckoutsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await ReadCheckoutsAsync(connection, $"{SelectCheckout} where status = 'Open' and expires_at <= $1", [now], cancellationToken);
    }

    public static Task<int> SetCheckoutStatusAsync(NpgsqlConnection connection, string checkoutId, CheckoutStatus status, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            "update billing_checkouts set status = $2, completed_at = case when $2 = 'Completed' then $3 else completed_at end where id = $1",
            [checkoutId, status.ToString(), now],
            cancellationToken);

    public static Task<int> AddEventAsync(
        NpgsqlConnection connection,
        string firmId,
        Guid? chargeId,
        string type,
        string source,
        string? detail,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            "insert into billing_events (firm_id, charge_id, type, recorded_at, source, detail) values ($1, $2, $3, $4, $5, $6)",
            [
                firmId, new NpgsqlParameter { Value = (object?)chargeId ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid }, type, now, source,
                detail is null ? new NpgsqlParameter { Value = DBNull.Value, NpgsqlDbType = NpgsqlDbType.Jsonb } : Jsonb(detail),
            ],
            cancellationToken);

    /// <summary>The ids of the live firms that pay by card.</summary>
    public async Task<List<string>> PaidFirmsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            "select b.firm_id from firm_billing b join firms f on f.id = b.firm_id where b.plan = 'Paid' and b.activated_at is not null and f.status = 'Live' order by b.firm_id",
            []);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var firms = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            firms.Add(reader.GetString(0));
        }

        return firms;
    }

    /// <summary>The firm's challenge accounts that have not ended, with whether each is paused.</summary>
    public static async Task<List<(Guid Id, bool Paused)>> OpenAccountsAsync(NpgsqlConnection connection, string firmId, CancellationToken cancellationToken)
    {
        await using var command = Command(
            connection,
            "select id, paused from challenge_accounts where firm_id = $1 and status not in ('Failed', 'Cancelled') order by number",
            [firmId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var accounts = new List<(Guid, bool)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add((reader.GetGuid(0), reader.GetBoolean(1)));
        }

        return accounts;
    }

    public static NpgsqlParameter Text(string? value) => new() { Value = (object?)value ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text };

    public static NpgsqlParameter Int(int? value) => new() { Value = (object?)value ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Integer };

    public static NpgsqlParameter NullableTimestamp(DateTimeOffset? value) => new() { Value = (object?)value ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.TimestampTz };

    /// <summary>Runs the statement and returns how many rows it changed.</summary>
    public static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>The firm's status as saved, with the firm locked until the caller's transaction ends.</summary>
    public static async Task<Firms.FirmStatus> LockFirmStatusAsync(NpgsqlConnection connection, string firmId, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, "select status from firms where id = $1 for update", [firmId]);
        return Enum.Parse<Firms.FirmStatus>((string)(await command.ExecuteScalarAsync(cancellationToken))!);
    }

    /// <summary>The firm's status, suspension and review as saved, with the firm locked until the caller's transaction ends when <paramref name="forUpdate"/> is set.</summary>
    public static async Task<GoLiveState> GoLiveStateAsync(NpgsqlConnection connection, string firmId, bool forUpdate, CancellationToken cancellationToken)
    {
        await using var command = Command(
            connection,
            $"select f.status, f.suspended_at is not null, r.status from firms f left join firm_reviews r on r.firm_id = f.id where f.id = $1{(forUpdate ? " for update of f" : "")}",
            [firmId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new GoLiveState(
            Enum.Parse<Firms.FirmStatus>(reader.GetString(0)),
            reader.GetBoolean(1),
            reader.IsDBNull(2) ? null : Enum.Parse<ReviewStatus>(reader.GetString(2)));
    }

    /// <summary>
    /// The firms whose challenges may need pausing or resuming beyond the firms that pay by card: those we have
    /// suspended, and those with paused challenges.
    /// </summary>
    public async Task<List<string>> FirmsToKeepStandingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            """
            select id from firms where suspended_at is not null
            union
            select firm_id from challenge_accounts where paused and status not in ('Failed', 'Cancelled')
            order by 1
            """,
            []);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var firms = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            firms.Add(reader.GetString(0));
        }

        return firms;
    }

    /// <summary>The first month from <paramref name="month"/> that is neither paid nor has a monthly charge waiting to be paid.</summary>
    public static async Task<DateOnly> FirstUnchargedMonthAsync(NpgsqlConnection connection, string firmId, DateOnly month, CancellationToken cancellationToken)
    {
        await using var command = Command(
            connection,
            """
            select max(month) from (
                select month from billing_periods where firm_id = $1 and month >= $2
                union all
                select month from billing_charges where firm_id = $1 and kind = 'Renewal' and status in ('Pending', 'Failed') and month >= $2) charged
            """,
            [firmId, month]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return reader.IsDBNull(0) ? month : reader.GetFieldValue<DateOnly>(0).AddMonths(1);
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, string sql, object[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter as NpgsqlParameter ?? new NpgsqlParameter { Value = parameter });
        }

        return command;
    }

    private static async Task<List<Charge>> ReadChargesAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var charges = new List<Charge>();
        while (await reader.ReadAsync(cancellationToken))
        {
            charges.Add(new Charge(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetInt64(2),
                Enum.Parse<ChargeKind>(reader.GetString(3)),
                Enum.Parse<ChargeStatus>(reader.GetString(4)),
                reader.GetFieldValue<DateOnly>(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                JsonSerializer.Deserialize<List<ChargeLine>>(reader.GetString(8), PropJson.Options)!,
                reader.GetDecimal(9),
                reader.GetString(10),
                Enum.Parse<BillingProvider>(reader.GetString(11)),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetInt32(14),
                NullableTime(reader, 15),
                reader.GetFieldValue<DateTimeOffset>(16),
                NullableTime(reader, 17),
                NullableTime(reader, 18)));
        }

        return charges;
    }

    private static async Task<List<BillingCheckout>> ReadCheckoutsAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var checkouts = new List<BillingCheckout>();
        while (await reader.ReadAsync(cancellationToken))
        {
            checkouts.Add(new BillingCheckout(
                reader.GetString(0),
                reader.GetString(1),
                Enum.Parse<BillingProvider>(reader.GetString(2)),
                Enum.Parse<CheckoutPurpose>(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                new Uri(reader.GetString(5)),
                Enum.Parse<CheckoutStatus>(reader.GetString(6)),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                NullableTime(reader, 9)));
        }

        return checkouts;
    }

    private static BillingPeriod ReadPeriod(NpgsqlDataReader reader) =>
        new(reader.GetFieldValue<DateOnly>(0), reader.GetInt32(1), reader.GetFieldValue<DateTimeOffset>(2));

    private static DateTimeOffset? NullableTime(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private static NpgsqlParameter Jsonb(string json) => new() { Value = json, NpgsqlDbType = NpgsqlDbType.Jsonb };
}
