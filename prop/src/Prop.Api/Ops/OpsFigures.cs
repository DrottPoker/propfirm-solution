using Common.Postgres;

using Npgsql;

using Prop.Api.Billing;
using Prop.Api.Challenges;
using Prop.Api.Firms;
using Prop.Api.Review;

namespace Prop.Api.Ops;

/// <summary>Which firms our staff list. Live firms that have not paid are among the live ones too.</summary>
public enum OpsFirmGroup
{
    All,

    /// <summary>The firms whose application waits for us, oldest first.</summary>
    ToReview,

    /// <summary>Not live yet, and neither suspended nor turned down.</summary>
    Sandbox,

    /// <summary>Live, and not suspended.</summary>
    Live,

    /// <summary>Live, with this month unpaid or a charge whose card was declined.</summary>
    Unpaid,

    Suspended,

    /// <summary>Not approved, and not suspended.</summary>
    Rejected,
}

/// <summary>
/// A firm as our staff list it: its standing with us, its review, how it pays us and with which card, and its challenges
/// that have not ended, those paused, the orders that hold a slot and the slots it has paid for this month.
/// </summary>
internal sealed record OpsFirmRecord(
    string Id,
    string Name,
    FirmStatus Status,
    bool Configured,
    DateTimeOffset CreatedAt,
    ReviewStatus? Review,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? SuspendedAt,
    BillingPlan? Plan,
    int? PlanSlots,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? UnpaidSince,
    bool Declined,
    int OpenAccounts,
    int PausedAccounts,
    int Reserved,
    int? PaidSlots,
    CardResponse? Card)
{
    /// <summary>This month is unpaid, or a charge's card was declined.</summary>
    public bool Unpaid => UnpaidSince is not null || Declined;

    /// <summary>Live, and paying us each month by card.</summary>
    public bool Paying => Status == FirmStatus.Live && Plan == BillingPlan.Paid && ActivatedAt is not null;
}

/// <summary>How many firms are in each group.</summary>
internal sealed record OpsFirmCounts(int All, int ToReview, int Sandbox, int Live, int Unpaid, int Suspended, int Rejected);

/// <summary>A firm and since when something has waited, for example its application.</summary>
internal sealed record WaitingFirm(string Id, string Name, DateTimeOffset Since);

/// <summary>
/// A firm's payouts that traders have waited for since before a time, how many of them the firm has approved, how much
/// they are per currency, and when the oldest was asked for.
/// </summary>
internal sealed record LatePayouts(string FirmId, string FirmName, int Count, int Approved, IReadOnlyList<MoneyAmount> Totals, DateTimeOffset OldestRequestedAt);

/// <summary>Charges paid since a time: how many of each kind, and how much per currency.</summary>
internal sealed record PaidCharges(int Months, int Slots, int GoingLive, int Deposits, IReadOnlyList<MoneyAmount> Totals);

/// <summary>What firms paid us in one week, from Monday in UTC: for months and slots, and for going live and deposits.</summary>
internal sealed record ChargeWeek(DateOnly Start, IReadOnlyList<MoneyAmount> Months, IReadOnlyList<MoneyAmount> GoingLive);

/// <summary>
/// Firms that signed up since a time and how far they got: sent an application, approved, live. Beside it the approved
/// firms of any age that are not live yet, and how long our decisions took on average.
/// </summary>
internal sealed record SignupFunnel(int SignedUp, int Sent, int Approved, int Live, int ApprovedNotLive, TimeSpan? AverageTimeToDecision);

/// <summary>Something that happened to a firm, for our overview.</summary>
internal sealed record OpsActivityRecord(
    string Kind,
    DateTimeOffset Time,
    string FirmId,
    string FirmName,
    string? Actor,
    decimal? Amount,
    string? Currency,
    string? Text,
    ChargeKind? ChargeKind,
    DateOnly? Month,
    int? Slots);

/// <summary>What a firm in the sandbox has tried: challenges, purchases, payouts and challenges of its own.</summary>
internal sealed record SandboxUse(int Challenges, int Purchases, int Payouts, int OwnChallenges);

/// <summary>How payouts were decided since a time: the average time from request to payment, those rejected, and those decided.</summary>
internal sealed record PayoutRecord(TimeSpan? AverageTimeToPay, int Rejected, int Decided);

/// <summary>
/// The figures behind our own admin view (ADR 0024), across the firms: the list of firms, what waits for us, what firms
/// pay us and how they get from signing up to live. Worked out from the tables that are already there.
/// </summary>
internal sealed class OpsFigures(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    // A charge whose card was declined and that waits to be paid.
    private const string DeclinedSql =
        "exists (select 1 from billing_charges c where c.firm_id = f.id and c.status in ('Pending', 'Failed') and c.failure is not null)";

    private const string FirmTables =
        """
        firms f
        left join firm_reviews r on r.firm_id = f.id
        left join firm_billing b on b.firm_id = f.id
        """;

    // The firm's name, short name or an administrator's email holds $1, already in capitals. Empty finds every firm.
    private const string SearchSql =
        """
        ($1 = '' or strpos(upper(f.name), $1) > 0 or strpos(upper(f.id), $1) > 0
         or exists (select 1 from firm_admins fa where fa.firm_id = f.id and strpos(fa.normalized_email, $1) > 0))
        """;

    private const string OpenAccount = "a.firm_id = f.id and a.status not in ('Failed', 'Cancelled')";

    /// <summary>The condition that puts a firm in the group, over firms f, their review r and their billing b.</summary>
    public static string GroupSql(OpsFirmGroup group) => group switch
    {
        OpsFirmGroup.ToReview => "r.status = 'Submitted'",
        OpsFirmGroup.Sandbox => "f.suspended_at is null and f.status <> 'Live' and r.status is distinct from 'Rejected'",
        OpsFirmGroup.Live => "f.suspended_at is null and f.status = 'Live'",
        OpsFirmGroup.Unpaid => $"f.suspended_at is null and f.status = 'Live' and (b.unpaid_since is not null or {DeclinedSql})",
        OpsFirmGroup.Suspended => "f.suspended_at is not null",
        OpsFirmGroup.Rejected => "f.suspended_at is null and r.status = 'Rejected'",
        _ => "true",
    };

    private static string OrderSql(OpsFirmGroup group) => group switch
    {
        OpsFirmGroup.ToReview => "r.submitted_at, f.id",
        OpsFirmGroup.Unpaid => "b.unpaid_since nulls last, f.id",
        OpsFirmGroup.Suspended => "f.suspended_at desc, f.id",
        OpsFirmGroup.Rejected => "r.decided_at desc nulls last, f.id",
        OpsFirmGroup.Live => "b.activated_at desc nulls last, f.created_at desc, f.id",
        _ => "f.created_at desc, f.id",
    };

    /// <summary>
    /// The firms in the group that the search finds, at most <paramref name="limit"/>, in the group's order: the oldest
    /// application first among those to review, otherwise mostly the newest first.
    /// </summary>
    public async Task<List<OpsFirmRecord>> ListFirmsAsync(OpsFirmGroup group, string? search, DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            $"""
            select f.id, f.name, f.status, f.configured, f.created_at, r.status, r.submitted_at, r.decided_at, f.suspended_at,
                   b.plan, b.slots, b.activated_at, b.unpaid_since, {DeclinedSql},
                   (select count(*) from challenge_accounts a where {OpenAccount}),
                   (select count(*) from challenge_accounts a where {OpenAccount} and a.paused),
                   (select count(*) from orders o
                    where o.firm_id = f.id and o.status = 'Pending' and o.expires_at > $2 and o.challenge_account_id is null),
                   (select p.slots from billing_periods p where p.firm_id = f.id and p.month <= $3 order by p.month desc limit 1),
                   b.card_brand, b.card_last4, b.card_exp_month, b.card_exp_year
            from {FirmTables}
            where {GroupSql(group)} and {SearchSql}
            order by {OrderSql(group)}
            limit $4
            """,
            [SearchText(search), now, BillingRules.MonthOf(now), limit],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var firms = new List<OpsFirmRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            firms.Add(new OpsFirmRecord(
                reader.GetString(0),
                reader.GetString(1),
                Enum.Parse<FirmStatus>(reader.GetString(2)),
                reader.GetBoolean(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : Enum.Parse<ReviewStatus>(reader.GetString(5)),
                Time(reader, 6),
                Time(reader, 7),
                Time(reader, 8),
                reader.IsDBNull(9) ? null : Enum.Parse<BillingPlan>(reader.GetString(9)),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                Time(reader, 11),
                Time(reader, 12),
                reader.GetBoolean(13),
                Count(reader, 14),
                Count(reader, 15),
                Count(reader, 16),
                reader.IsDBNull(17) ? null : reader.GetInt32(17),
                reader.IsDBNull(19) ? null : new CardResponse(reader.GetString(18), reader.GetString(19), reader.GetInt32(20), reader.GetInt32(21))));
        }

        return firms;
    }

    /// <summary>How many firms the search finds in each group.</summary>
    public async Task<OpsFirmCounts> CountFirmsAsync(string? search, CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            $"""
            select count(*),
                   count(*) filter (where {GroupSql(OpsFirmGroup.ToReview)}),
                   count(*) filter (where {GroupSql(OpsFirmGroup.Sandbox)}),
                   count(*) filter (where {GroupSql(OpsFirmGroup.Live)}),
                   count(*) filter (where {GroupSql(OpsFirmGroup.Unpaid)}),
                   count(*) filter (where {GroupSql(OpsFirmGroup.Suspended)}),
                   count(*) filter (where {GroupSql(OpsFirmGroup.Rejected)})
            from {FirmTables}
            where {SearchSql}
            """,
            [SearchText(search)],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new OpsFirmCounts(Count(reader, 0), Count(reader, 1), Count(reader, 2), Count(reader, 3), Count(reader, 4), Count(reader, 5), Count(reader, 6));
    }

    /// <summary>The applications that wait for us, the oldest first, with when each was sent.</summary>
    public async Task<List<WaitingFirm>> ToReviewAsync(CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            "select f.id, f.name, r.submitted_at from firm_reviews r join firms f on f.id = r.firm_id where r.status = 'Submitted' order by r.submitted_at, f.id",
            [],
            cancellationToken);
        return await ReadWaitingAsync(command, cancellationToken);
    }

    /// <summary>Firms whose trading server is still being created since before <paramref name="before"/>.</summary>
    public async Task<List<WaitingFirm>> SettingUpAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            "select f.id, f.name, f.created_at from firms f where f.status = 'Provisioning' and f.created_at < $1 order by f.created_at, f.id",
            [before],
            cancellationToken);
        return await ReadWaitingAsync(command, cancellationToken);
    }

    /// <summary>
    /// The firms with payouts that traders asked for before <paramref name="before"/> and are neither paid nor rejected, the
    /// longest waiting first.
    /// </summary>
    public async Task<List<LatePayouts>> LatePayoutsAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            """
            select p.firm_id, f.name, p.currency, count(*), count(*) filter (where p.status = 'Approved'), sum(p.amount), min(p.requested_at)
            from payouts p join firms f on f.id = p.firm_id
            where p.status in ('Pending', 'Approved') and p.requested_at < $1
            group by p.firm_id, f.name, p.currency
            order by p.firm_id, p.currency
            """,
            [before],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(string FirmId, string Name, MoneyAmount Total, int Count, int Approved, DateTimeOffset Oldest)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetString(0), reader.GetString(1), new MoneyAmount(reader.GetString(2), reader.GetDecimal(5)), Count(reader, 3), Count(reader, 4), reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return
        [
            .. rows.GroupBy(r => r.FirmId, StringComparer.Ordinal)
                .Select(g => new LatePayouts(g.Key, g.First().Name, g.Sum(r => r.Count), g.Sum(r => r.Approved), [.. g.Select(r => r.Total)], g.Min(r => r.Oldest)))
                .OrderBy(l => l.OldestRequestedAt),
        ];
    }

    /// <summary>The charges paid since <paramref name="since"/>, without VAT.</summary>
    public async Task<PaidCharges> PaidSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            "select kind, currency, count(*), sum(net_amount) from billing_charges where status = 'Paid' and paid_at >= $1 group by kind, currency order by currency",
            [since],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(ChargeKind Kind, MoneyAmount Total, int Count)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((Enum.Parse<ChargeKind>(reader.GetString(0)), new MoneyAmount(reader.GetString(1), reader.GetDecimal(3)), Count(reader, 2)));
        }

        return new PaidCharges(
            rows.Where(r => r.Kind == ChargeKind.Renewal).Sum(r => r.Count),
            rows.Where(r => r.Kind == ChargeKind.Slots).Sum(r => r.Count),
            rows.Where(r => r.Kind == ChargeKind.Activation).Sum(r => r.Count),
            rows.Where(r => r.Kind == ChargeKind.Deposit).Sum(r => r.Count),
            [.. rows.GroupBy(r => r.Total.Currency, StringComparer.Ordinal).Select(g => new MoneyAmount(g.Key, g.Sum(r => r.Total.Amount)))]);
    }

    /// <summary>What firms paid us per week without VAT for <paramref name="weeks"/> weeks, the one with <paramref name="now"/> last.</summary>
    public async Task<List<ChargeWeek>> WeeksAsync(DateTimeOffset now, int weeks, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var thisWeek = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var first = thisWeek.AddDays(-7 * (weeks - 1));
        await using var command = await CommandAsync(
            """
            select kind in ('Renewal', 'Slots'), date_trunc('week', paid_at at time zone 'UTC')::date, currency, sum(net_amount)
            from billing_charges
            where status = 'Paid' and paid_at >= $1
            group by 1, 2, 3
            order by 3
            """,
            [new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(bool Months, DateOnly Week, MoneyAmount Amount)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetBoolean(0), reader.GetFieldValue<DateOnly>(1), new MoneyAmount(reader.GetString(2), reader.GetDecimal(3))));
        }

        return
        [
            .. Enumerable.Range(0, weeks).Select(i => first.AddDays(7 * i)).Select(week => new ChargeWeek(
                week,
                [.. rows.Where(r => r.Months && r.Week == week).Select(r => r.Amount)],
                [.. rows.Where(r => !r.Months && r.Week == week).Select(r => r.Amount)])),
        ];
    }

    /// <summary>
    /// The firms that signed up since <paramref name="since"/> and how far they got, the approved firms that are not live
    /// yet, and how long our decisions since then took from the application they answered.
    /// </summary>
    public async Task<SignupFunnel> FunnelAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            """
            select count(*),
                   count(*) filter (where exists (select 1 from firm_events e where e.firm_id = f.id and e.type = 'submitted')),
                   count(*) filter (where exists (select 1 from firm_events e where e.firm_id = f.id and e.type = 'approved')),
                   count(*) filter (where b.activated_at is not null),
                   (select count(*) from firm_reviews ar join firms af on af.id = ar.firm_id where ar.status = 'Approved' and af.status <> 'Live'),
                   (select extract(epoch from avg(d.recorded_at - s.sent))::float8
                    from firm_events d
                    join lateral (
                        select max(e.recorded_at) as sent from firm_events e
                        where e.firm_id = d.firm_id and e.type = 'submitted' and e.recorded_at <= d.recorded_at
                    ) s on true
                    where d.type in ('approved', 'changes_requested', 'rejected') and d.recorded_at >= $1 and s.sent is not null)
            from firms f left join firm_billing b on b.firm_id = f.id
            where not f.configured and f.created_at >= $1
            """,
            [since],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new SignupFunnel(
            Count(reader, 0),
            Count(reader, 1),
            Count(reader, 2),
            Count(reader, 3),
            Count(reader, 4),
            reader.IsDBNull(5) ? null : TimeSpan.FromSeconds(reader.GetDouble(5)));
    }

    /// <summary>
    /// What happened to the firms since <paramref name="since"/>, the newest first: sign-ups, applications and our
    /// decisions, suspensions, firms going live, and charges paid or declined. Deposits come with their applications.
    /// </summary>
    public async Task<List<OpsActivityRecord>> ActivityAsync(DateTimeOffset since, int limit, CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            """
            select * from (
                (select 'signed_up', f.created_at, f.id, f.name, null::text, null::numeric, null::text, null::text, null::text, null::date, null::int
                 from firms f
                 where not f.configured and f.created_at >= $1
                 order by f.created_at desc limit $2)
                union all
                (select e.type, e.recorded_at, f.id, f.name, e.actor, null::numeric, null::text,
                        coalesce(e.detail ->> 'message', e.detail ->> 'reason'), null::text, null::date, null::int
                 from firm_events e join firms f on f.id = e.firm_id
                 where e.type in ('submitted', 'approved', 'changes_requested', 'rejected', 'suspended', 'suspension_lifted') and e.recorded_at >= $1
                 order by e.recorded_at desc limit $2)
                union all
                (select 'paid', c.paid_at, f.id, f.name, null::text, c.amount, c.currency, null::text, c.kind, c.month, c.slots
                 from billing_charges c join firms f on f.id = c.firm_id
                 where c.status = 'Paid' and c.kind <> 'Deposit' and c.paid_at >= $1
                 order by c.paid_at desc limit $2)
                union all
                (select 'declined', be.recorded_at, f.id, f.name, null::text, c.amount, c.currency, be.detail ->> 'reason', c.kind, c.month, c.slots
                 from billing_events be join billing_charges c on c.id = be.charge_id join firms f on f.id = be.firm_id
                 where be.type = 'declined' and be.recorded_at >= $1
                 order by be.recorded_at desc limit $2)
            ) activity
            order by 2 desc
            limit $2
            """,
            [since, limit],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var activity = new List<OpsActivityRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            activity.Add(new OpsActivityRecord(
                reader.GetString(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : Enum.Parse<ChargeKind>(reader.GetString(8)),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10)));
        }

        return activity;
    }

    /// <summary>The challenges at live firms that have not ended, and how many of them are paused.</summary>
    public async Task<(int Open, int Paused)> LiveChallengesAsync(CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            """
            select count(*), count(*) filter (where a.paused)
            from challenge_accounts a join firms f on f.id = a.firm_id
            where f.status = 'Live' and not a.sandbox and a.status not in ('Failed', 'Cancelled')
            """,
            [],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (Count(reader, 0), Count(reader, 1));
    }

    /// <summary>What the firm has tried in the sandbox: its test accounts and purchases, payouts from them and its challenges.</summary>
    public async Task<SandboxUse> SandboxUseAsync(string firmId, CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            """
            select (select count(*) from challenge_accounts where firm_id = $1 and sandbox),
                   (select count(*) from orders where firm_id = $1 and paid_at is not null and sandbox),
                   (select count(*) from payouts p join challenge_accounts a on a.id = p.challenge_account_id where p.firm_id = $1 and a.sandbox),
                   (select count(*) from challenge_definitions where firm_id = $1)
            """,
            [firmId],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new SandboxUse(Count(reader, 0), Count(reader, 1), Count(reader, 2), Count(reader, 3));
    }

    /// <summary>
    /// How the firm's payouts were decided since <paramref name="since"/>, or every firm's when <paramref name="firmId"/> is
    /// null: the average time from the request to the payment, and how many were rejected of those paid or rejected.
    /// </summary>
    public async Task<PayoutRecord> PayoutRecordAsync(string? firmId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            """
            select extract(epoch from avg(p.paid_at - p.requested_at) filter (where p.status = 'Paid'))::float8,
                   count(*) filter (where p.status = 'Rejected'),
                   count(*)
            from payouts p join challenge_accounts a on a.id = p.challenge_account_id
            where ($1::text is null or p.firm_id = $1) and not a.sandbox
              and ((p.status = 'Paid' and p.paid_at >= $2) or (p.status = 'Rejected' and p.rejected_at >= $2))
            """,
            [new NpgsqlParameter { Value = (object?)firmId ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text }, since],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new PayoutRecord(reader.IsDBNull(0) ? null : TimeSpan.FromSeconds(reader.GetDouble(0)), Count(reader, 1), Count(reader, 2));
    }

    // Capitals, as the firms' names are compared in capitals and the administrators' emails are kept in them.
    private static string SearchText(string? search) => (search ?? "").Trim().ToUpperInvariant();

    private static async Task<List<WaitingFirm>> ReadWaitingAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var firms = new List<WaitingFirm>();
        while (await reader.ReadAsync(cancellationToken))
        {
            firms.Add(new WaitingFirm(reader.GetString(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2)));
        }

        return firms;
    }

    private static int Count(NpgsqlDataReader reader, int ordinal) => (int)reader.GetInt64(ordinal);

    private static DateTimeOffset? Time(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private async Task<NpgsqlCommand> CommandAsync(string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        var command = dataSource.CreateCommand(sql);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter as NpgsqlParameter ?? new NpgsqlParameter { Value = parameter });
        }

        return command;
    }
}
