using Common.Postgres;

using Npgsql;

using Prop.Api.Challenges;
using Prop.Api.Payments;

namespace Prop.Api.Portal;

/// <summary>How many accounts of a challenge are open, how many started since a time, and how its evaluations ended.</summary>
internal sealed record ChallengeFigures(string ChallengeId, int Trading, int Started, int Passed, int Ended);

/// <summary>
/// Paid orders since a time, not refunded, and how much they came to per currency. Test purchases from the sandbox count
/// only while the firm is still there.
/// </summary>
internal sealed record SalesFigures(int Orders, IReadOnlyList<MoneyAmount> Totals);

/// <summary>Sales and paid payouts in one week, from Monday in UTC, per currency.</summary>
internal sealed record WeekFigures(DateOnly Start, IReadOnlyList<MoneyAmount> Sales, IReadOnlyList<MoneyAmount> Payouts);

/// <summary>Something that happened to one of the firm's accounts, for the admin panel's overview (ADR 0023).</summary>
internal sealed record ActivityRecord(
    string Kind,
    DateTimeOffset Time,
    Guid AccountId,
    long AccountNumber,
    string Email,
    string ChallengeName,
    string? StageName,
    decimal? Amount,
    string? Currency,
    long? OrderNumber,
    int? TradingDays,
    string? Reason,
    string? Reference);

/// <summary>A trader as the admin panel shows them: since when, whether they have a portal password, and what they bought.</summary>
internal sealed record TraderRecord(
    Guid Id, string Email, DateTimeOffset CreatedAt, bool HasPassword, int Orders, IReadOnlyList<MoneyAmount> Bought, string? Name = null, string? Country = null);

/// <summary>The paid order that started an account.</summary>
internal sealed record AccountOrder(Guid Id, long Number, decimal Amount, string Currency, PaymentProvider Provider, DateTimeOffset PaidAt);

/// <summary>
/// The figures behind the admin panel's overview and challenges (ADR 0023), worked out from the firm's accounts, the
/// stages they passed, their orders and their payouts. Every query is limited to one firm.
/// </summary>
internal sealed class AdminFigures(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    private const string FundedStage = ChallengeQueries.FundedStageSql;

    // The columns of one kind of activity, in the order ActivityRecord has them.
    private const string ActivityTail = "null::bigint, null::int, null::text, null::text";

    /// <summary>
    /// Whether an order or account under <paramref name="alias"/> counts in the figures: one from the sandbox only while the
    /// firm is still there, so its tests show while it tries the platform, but not once it is live.
    /// </summary>
    public static string CountedSql(string alias) =>
        $"(not {alias}.sandbox or (select cf.status from firms cf where cf.id = {alias}.firm_id) <> 'Live')";

    /// <summary>
    /// Each challenge's figures: its open accounts, those started since <paramref name="startedSince"/>, and its evaluations
    /// that ended since <paramref name="endedSince"/>, by passing every evaluation stage or by failing one. A challenge the
    /// firm cancelled is left out, since it says nothing about the challenge.
    /// </summary>
    public async Task<List<ChallengeFigures>> ChallengesAsync(string firmId, DateTimeOffset startedSince, DateTimeOffset endedSince, CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            $"""
            select a.definition_id,
                   count(*) filter (where a.status in ('OpeningAccount', 'Active')),
                   count(*) filter (where a.created_at >= $2),
                   count(*) filter (where passed.passed_at >= $3),
                   count(*) filter (where passed.passed_at >= $3
                                    or (a.status = 'Failed' and a.stage < {FundedStage} and (a.ending ->> 'time')::timestamptz >= $3))
            from challenge_accounts a
            left join lateral (
                select ta.passed_at from trading_accounts ta where ta.challenge_account_id = a.id and ta.stage = {FundedStage} - 1
            ) passed on true
            where a.firm_id = $1 and {CountedSql("a")} and (a.status in ('OpeningAccount', 'Active') or a.created_at >= $2 or a.updated_at >= $3)
            group by a.definition_id
            order by a.definition_id
            """,
            [firmId, startedSince, endedSince],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var figures = new List<ChallengeFigures>();
        while (await reader.ReadAsync(cancellationToken))
        {
            figures.Add(new ChallengeFigures(reader.GetString(0), Count(reader, 1), Count(reader, 2), Count(reader, 3), Count(reader, 4)));
        }

        return figures;
    }

    public async Task<SalesFigures> SalesAsync(string firmId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            $"""
            select o.currency, count(*), sum(o.amount)
            from orders o
            where o.firm_id = $1 and o.paid_at >= $2 and o.refunded_at is null and {CountedSql("o")}
            group by o.currency
            order by o.currency
            """,
            [firmId, since],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var orders = 0;
        var totals = new List<MoneyAmount>();
        while (await reader.ReadAsync(cancellationToken))
        {
            orders += Count(reader, 1);
            totals.Add(new MoneyAmount(reader.GetString(0), reader.GetDecimal(2)));
        }

        return new SalesFigures(orders, totals);
    }

    /// <summary>
    /// Sales and paid payouts per week for at most <paramref name="weeks"/> weeks, the one with <paramref name="now"/> last.
    /// The first is the week the firm started: when it went live, or signed up while in the sandbox, so the weeks before
    /// the firm counted anything are not shown as empty ones.
    /// </summary>
    public async Task<List<WeekFigures>> WeeksAsync(string firmId, DateTimeOffset now, int weeks, CancellationToken cancellationToken)
    {
        var thisWeek = WeekOf(now);
        var first = thisWeek.AddDays(-7 * (weeks - 1));
        await using (var started = await CommandAsync(
            """
            select case when f.status = 'Live' then coalesce(b.activated_at, f.created_at) else f.created_at end
            from firms f left join firm_billing b on b.firm_id = f.id
            where f.id = $1
            """,
            [firmId],
            cancellationToken))
        {
            if (await started.ExecuteScalarAsync(cancellationToken) is DateTime since)
            {
                var start = WeekOf(new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc)));
                first = start > first ? (start > thisWeek ? thisWeek : start) : first;
            }
        }

        var count = (thisWeek.DayNumber - first.DayNumber) / 7 + 1;
        await using var command = await CommandAsync(
            $"""
            select 'sales', date_trunc('week', o.paid_at at time zone 'UTC')::date, o.currency, sum(o.amount)
            from orders o
            where o.firm_id = $1 and o.paid_at >= $2 and o.refunded_at is null and {CountedSql("o")}
            group by 2, 3
            union all
            select 'payouts', date_trunc('week', p.paid_at at time zone 'UTC')::date, p.currency, sum(p.amount)
            from payouts p join challenge_accounts a on a.id = p.challenge_account_id
            where p.firm_id = $1 and p.status = 'Paid' and p.paid_at >= $2 and {CountedSql("a")}
            group by 2, 3
            order by 3
            """,
            [firmId, new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(bool Sale, DateOnly Week, MoneyAmount Amount)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetString(0) == "sales", reader.GetFieldValue<DateOnly>(1), new MoneyAmount(reader.GetString(2), reader.GetDecimal(3))));
        }

        return
        [
            .. Enumerable.Range(0, count).Select(i => first.AddDays(7 * i)).Select(week => new WeekFigures(
                week,
                [.. rows.Where(r => r.Sale && r.Week == week).Select(r => r.Amount)],
                [.. rows.Where(r => !r.Sale && r.Week == week).Select(r => r.Amount)])),
        ];
    }

    // The Monday, in UTC, of the week the time is in.
    private static DateOnly WeekOf(DateTimeOffset time)
    {
        var day = DateOnly.FromDateTime(time.UtcDateTime);
        return day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    }

    /// <summary>
    /// What happened to the firm's accounts since <paramref name="since"/>, the newest first: challenges started or bought,
    /// stages passed, funded accounts started, challenges that ended, and payouts asked for, paid or rejected.
    /// </summary>
    public async Task<List<ActivityRecord>> ActivityAsync(string firmId, DateTimeOffset since, int limit, CancellationToken cancellationToken)
    {
        const string Account = "a.id, a.number, t.email, a.state -> 'definition' ->> 'name'";
        const string AccountTables = "challenge_accounts a join traders t on t.id = a.trader_id";
        const string PayoutTables = "payouts p join challenge_accounts a on a.id = p.challenge_account_id join traders t on t.id = a.trader_id";
        await using var command = await CommandAsync(
            $"""
            select * from (
                (select case when o.id is null then 'ChallengeStarted' else 'ChallengeBought' end, a.created_at, {Account},
                        null::text, o.amount, o.currency, o.number, null::int, null::text, null::text
                 from {AccountTables}
                 left join orders o on o.challenge_account_id = a.id and o.paid_at is not null
                 where a.firm_id = $1 and a.created_at >= $2
                 order by a.created_at desc limit $3)
                union all
                (select case when ta.stage = {FundedStage} - 1 then 'EvaluationPassed' else 'StagePassed' end, ta.passed_at, {Account},
                        a.state -> 'definition' -> 'evaluation' -> ta.stage ->> 'name',
                        ta.passed_balance - (a.state -> 'definition' ->> 'initialBalance')::numeric, a.state -> 'definition' ->> 'currency',
                        null::bigint, ta.passed_trading_days, null::text, null::text
                 from trading_accounts ta join {AccountTables} on a.id = ta.challenge_account_id
                 where a.firm_id = $1 and a.updated_at >= $2 and ta.passed_at >= $2
                 order by ta.passed_at desc limit $3)
                union all
                (select 'FundedStarted', ta.started_at, {Account}, a.state -> 'definition' -> 'funded' ->> 'name', null::numeric, null::text, {ActivityTail}
                 from trading_accounts ta join {AccountTables} on a.id = ta.challenge_account_id
                 where a.firm_id = $1 and a.updated_at >= $2 and ta.stage = {FundedStage} and ta.started_at >= $2
                 order by ta.started_at desc limit $3)
                union all
                (select a.ending ->> 'kind', (a.ending ->> 'time')::timestamptz, {Account},
                        case when a.stage < {FundedStage} then a.state -> 'definition' -> 'evaluation' -> a.stage ->> 'name'
                             else a.state -> 'definition' -> 'funded' ->> 'name' end,
                        null::numeric, null::text, null::bigint, null::int, a.ending ->> 'reason', null::text
                 from {AccountTables}
                 where a.firm_id = $1 and a.updated_at >= $2 and a.ending is not null and (a.ending ->> 'time')::timestamptz >= $2
                 order by 2 desc limit $3)
                union all
                (select 'PayoutRequested', p.requested_at, {Account}, null::text, p.amount, p.currency, {ActivityTail}
                 from {PayoutTables}
                 where p.firm_id = $1 and p.requested_at >= $2
                 order by p.requested_at desc limit $3)
                union all
                (select 'PayoutPaid', p.paid_at, {Account}, null::text, p.amount, p.currency, null::bigint, null::int, null::text, p.reference
                 from {PayoutTables}
                 where p.firm_id = $1 and p.status = 'Paid' and p.paid_at >= $2
                 order by p.paid_at desc limit $3)
                union all
                (select 'PayoutRejected', p.rejected_at, {Account}, null::text, p.amount, p.currency, null::bigint, null::int, p.reason, null::text
                 from {PayoutTables}
                 where p.firm_id = $1 and p.status = 'Rejected' and p.rejected_at >= $2
                 order by p.rejected_at desc limit $3)
            ) activity
            order by 2 desc
            limit $3
            """,
            [firmId, since, limit],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var activity = new List<ActivityRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            activity.Add(new ActivityRecord(
                reader.GetString(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetGuid(2),
                reader.GetInt64(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        return activity;
    }

    /// <summary>The firm's trader with what they bought and did not get refunded, or null for another firm's.</summary>
    public async Task<TraderRecord?> TraderAsync(string firmId, Guid traderId, CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            """
            select t.id, t.email, t.created_at, t.password_hash is not null, o.currency, count(o.id), coalesce(sum(o.amount), 0), t.name, t.country
            from traders t
            left join challenge_accounts a on a.trader_id = t.id
            left join orders o on o.challenge_account_id = a.id and o.paid_at is not null and o.refunded_at is null
            where t.firm_id = $1 and t.id = $2
            group by t.id, o.currency
            order by o.currency
            """,
            [firmId, traderId],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        TraderRecord? trader = null;
        var orders = 0;
        var bought = new List<MoneyAmount>();
        while (await reader.ReadAsync(cancellationToken))
        {
            trader ??= new TraderRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetBoolean(3),
                0,
                [],
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8));
            if (!reader.IsDBNull(4))
            {
                orders += Count(reader, 5);
                bought.Add(new MoneyAmount(reader.GetString(4), reader.GetDecimal(6)));
            }
        }

        return trader is null ? null : trader with { Orders = orders, Bought = bought };
    }

    /// <summary>The paid order that started the account, or null when it was started some other way.</summary>
    public async Task<AccountOrder?> OrderOfAsync(string firmId, Guid accountId, CancellationToken cancellationToken)
    {
        await using var command = await CommandAsync(
            "select o.id, o.number, o.amount, o.currency, o.provider, o.paid_at from orders o where o.firm_id = $1 and o.challenge_account_id = $2 and o.paid_at is not null",
            [firmId, accountId],
            cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new AccountOrder(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetDecimal(2),
                reader.GetString(3),
                Enum.Parse<PaymentProvider>(reader.GetString(4)),
                reader.GetFieldValue<DateTimeOffset>(5))
            : null;
    }

    private static int Count(NpgsqlDataReader reader, int ordinal) => (int)reader.GetInt64(ordinal);

    private async Task<NpgsqlCommand> CommandAsync(string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        var command = dataSource.CreateCommand(sql);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        return command;
    }
}
