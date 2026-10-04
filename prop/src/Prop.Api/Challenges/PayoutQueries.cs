using Common.Postgres;

using Npgsql;

using Prop.Rules;

namespace Prop.Api.Challenges;

/// <summary>A payout with its challenge account and trader, and where the trader asked to be paid.</summary>
internal sealed record PayoutView(
    Guid Id,
    Guid ChallengeAccountId,
    long AccountNumber,
    Guid TraderId,
    string Email,
    string TradingAccountId,
    PayoutStatus Status,
    decimal Profit,
    decimal ProfitSplitPercent,
    decimal Amount,
    string Currency,
    DateTimeOffset RequestedAt,
    DateTimeOffset? WithdrawnAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? RejectedAt,
    DateTimeOffset? FailedAt,
    string? Reason,
    string? Reference,
    PayoutMethod? PayTo = null,
    bool ProfitReturned = false,
    string TimeZone = "UTC");

/// <summary>
/// A payout as the admin panel lists it: with the challenge's name, and how many payouts the account had paid before it
/// was asked for, and how much they were.
/// </summary>
internal sealed record AdminPayoutView(PayoutView Payout, string ChallengeName, int PaidBefore, decimal PaidBeforeAmount);

/// <summary>An amount in one currency.</summary>
internal sealed record MoneyAmount(string Currency, decimal Amount);

/// <summary>Payouts with a status, how much they are per currency and when the oldest got there.</summary>
internal sealed record PayoutGroup(int Count, IReadOnlyList<MoneyAmount> Totals, DateTimeOffset? Oldest);

/// <summary>
/// What the firm has to do about payouts and what it has paid: the payouts to approve and since when, those approved and
/// to pay, those paid since a time, and how long those took on average from the request to the payment.
/// </summary>
internal sealed record PayoutSummary(PayoutGroup ToApprove, PayoutGroup ToPay, PayoutGroup Paid, TimeSpan? AverageTimeToPay);

/// <summary>Reads payouts for the API. Every query is limited to one firm.</summary>
internal sealed class PayoutQueries(NpgsqlDataSource dataSource, DatabaseSchema schema, PayoutMethods methods)
{
    private const string ViewColumns =
        """
        p.id, p.challenge_account_id, a.number, a.trader_id, t.email, p.trading_account_id, p.status, p.profit, p.profit_split_percent,
        p.amount, p.currency, p.requested_at, p.withdrawn_at, p.approved_at, p.paid_at, p.rejected_at, p.failed_at, p.reason, p.reference,
        p.payout_details, p.profit_returned, a.day_time_zone
        """;

    private const string ViewTables =
        """
        from payouts p
        join challenge_accounts a on a.id = p.challenge_account_id
        join traders t on t.id = a.trader_id
        """;

    private const string SelectView = $"select {ViewColumns} {ViewTables}";

    public async Task<PayoutView?> GetAsync(string firmId, Guid id, CancellationToken cancellationToken) =>
        (await ReadAsync($"{SelectView} where p.firm_id = $1 and p.id = $2", [firmId, id], cancellationToken)).SingleOrDefault();

    /// <summary>The firm's newest payouts, optionally only those with one of the statuses.</summary>
    public Task<List<PayoutView>> ListAsync(string firmId, IReadOnlyList<PayoutStatus> statuses, int limit, CancellationToken cancellationToken) =>
        ReadAsync(
            $"{SelectView} where p.firm_id = $1 and (cardinality($2::text[]) = 0 or p.status = any($2)) order by p.requested_at desc limit $3",
            [firmId, statuses.Select(s => s.ToString()).ToArray(), limit],
            cancellationToken);

    /// <summary>
    /// The firm's payouts for the admin panel, optionally only those with one of the statuses: the newest first, or the
    /// oldest first for a queue the firm works through.
    /// </summary>
    public async Task<List<AdminPayoutView>> ListForAdminAsync(
        string firmId,
        IReadOnlyList<PayoutStatus> statuses,
        bool oldestFirst,
        int limit,
        CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            $"""
            select {ViewColumns}, a.state -> 'definition' ->> 'name', earlier.paid, earlier.amount
            {ViewTables}
            left join lateral (
                select count(*) as paid, coalesce(sum(b.amount), 0) as amount
                from payouts b
                where b.challenge_account_id = p.challenge_account_id and b.status = 'Paid' and b.requested_at < p.requested_at
            ) earlier on true
            where p.firm_id = $1 and (cardinality($2::text[]) = 0 or p.status = any($2))
            order by p.requested_at {(oldestFirst ? "asc" : "desc")}
            limit $3
            """);
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(statuses.Select(s => s.ToString()).ToArray());
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var payouts = new List<AdminPayoutView>();
        while (await reader.ReadAsync(cancellationToken))
        {
            payouts.Add(new AdminPayoutView(ReadView(reader), reader.GetString(22), (int)reader.GetInt64(23), reader.GetDecimal(24)));
        }

        return payouts;
    }

    /// <summary>The payouts the firm has to approve or pay, and those it has paid since <paramref name="paidSince"/>.</summary>
    public async Task<PayoutSummary> SummaryAsync(string firmId, DateTimeOffset paidSince, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        var rows = new List<(PayoutStatus Status, MoneyAmount Total, int Count, DateTimeOffset Oldest)>();
        await using (var command = dataSource.CreateCommand(
            $"""
            select p.status, p.currency, count(*), sum(p.amount), min(case when p.status = 'Approved' then p.approved_at else p.requested_at end)
            from payouts p join challenge_accounts a on a.id = p.challenge_account_id
            where p.firm_id = $1 and (p.status in ('Pending', 'Approved') or (p.status = 'Paid' and p.paid_at >= $2 and {Portal.AdminFigures.CountedSql("a")}))
            group by p.status, p.currency
            order by p.currency
            """))
        {
            command.Parameters.AddWithValue(firmId);
            command.Parameters.AddWithValue(paidSince);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    Enum.Parse<PayoutStatus>(reader.GetString(0)),
                    new MoneyAmount(reader.GetString(1), reader.GetDecimal(3)),
                    (int)reader.GetInt64(2),
                    reader.GetFieldValue<DateTimeOffset>(4)));
            }
        }

        await using var averageCommand = dataSource.CreateCommand(
            $"""
            select extract(epoch from avg(p.paid_at - p.requested_at))::float8
            from payouts p join challenge_accounts a on a.id = p.challenge_account_id
            where p.firm_id = $1 and p.status = 'Paid' and p.paid_at >= $2 and {Portal.AdminFigures.CountedSql("a")}
            """);
        averageCommand.Parameters.AddWithValue(firmId);
        averageCommand.Parameters.AddWithValue(paidSince);
        var average = await averageCommand.ExecuteScalarAsync(cancellationToken) is double seconds ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;
        return new PayoutSummary(Group(PayoutStatus.Pending, oldest: true), Group(PayoutStatus.Approved, oldest: true), Group(PayoutStatus.Paid, oldest: false), average);

        PayoutGroup Group(PayoutStatus status, bool oldest)
        {
            var group = rows.Where(r => r.Status == status).ToList();
            return new PayoutGroup(
                group.Sum(r => r.Count),
                [.. group.Select(r => r.Total)],
                oldest && group.Count > 0 ? group.Min(r => r.Oldest) : null);
        }
    }

    /// <summary>The account's payouts, newest first.</summary>
    public Task<List<PayoutView>> ListByAccountAsync(string firmId, Guid challengeAccountId, CancellationToken cancellationToken) =>
        ReadAsync($"{SelectView} where p.firm_id = $1 and p.challenge_account_id = $2 order by p.requested_at desc", [firmId, challengeAccountId], cancellationToken);

    /// <summary>The accounts' payouts, newest first.</summary>
    public Task<List<PayoutView>> ListByAccountsAsync(string firmId, Guid[] challengeAccountIds, CancellationToken cancellationToken) =>
        ReadAsync($"{SelectView} where p.firm_id = $1 and p.challenge_account_id = any($2) order by p.requested_at desc", [firmId, challengeAccountIds], cancellationToken);

    /// <summary>The trader's payouts from every account, newest first.</summary>
    public Task<List<PayoutView>> ListByTraderAsync(string firmId, Guid traderId, CancellationToken cancellationToken) =>
        ReadAsync($"{SelectView} where p.firm_id = $1 and a.trader_id = $2 order by p.requested_at desc", [firmId, traderId], cancellationToken);

    private async Task<List<PayoutView>> ReadAsync(string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(sql);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var payouts = new List<PayoutView>();
        while (await reader.ReadAsync(cancellationToken))
        {
            payouts.Add(ReadView(reader));
        }

        return payouts;
    }

    // The view's own columns, the first 22 of a query that starts with SelectView.
    private PayoutView ReadView(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetInt64(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            Enum.Parse<PayoutStatus>(reader.GetString(6)),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            reader.GetDecimal(9),
            reader.GetString(10),
            reader.GetFieldValue<DateTimeOffset>(11),
            Time(reader, 12),
            Time(reader, 13),
            Time(reader, 14),
            Time(reader, 15),
            Time(reader, 16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.IsDBNull(18) ? null : reader.GetString(18),
            reader.IsDBNull(19) ? null : methods.Open(reader.GetGuid(3), reader.GetString(19)),
            reader.GetBoolean(20),
            reader.GetString(21));

    private static DateTimeOffset? Time(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
}
