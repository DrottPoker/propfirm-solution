using Common.Postgres;

using Npgsql;

using Prop.Rules;

namespace Prop.Api.Challenges;

/// <summary>A payout with its challenge account and trader.</summary>
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
    string? Reference);

/// <summary>Reads payouts for the API. Every query is limited to one firm.</summary>
internal sealed class PayoutQueries(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    private const string SelectView =
        """
        select p.id, p.challenge_account_id, a.number, a.trader_id, t.email, p.trading_account_id, p.status, p.profit, p.profit_split_percent,
               p.amount, p.currency, p.requested_at, p.withdrawn_at, p.approved_at, p.paid_at, p.rejected_at, p.failed_at, p.reason, p.reference
        from payouts p
        join challenge_accounts a on a.id = p.challenge_account_id
        join traders t on t.id = a.trader_id
        """;

    public async Task<PayoutView?> GetAsync(string firmId, Guid id, CancellationToken cancellationToken) =>
        (await ReadAsync($"{SelectView} where p.firm_id = $1 and p.id = $2", [firmId, id], cancellationToken)).SingleOrDefault();

    /// <summary>The firm's newest payouts, optionally only those with one of the statuses.</summary>
    public Task<List<PayoutView>> ListAsync(string firmId, IReadOnlyList<PayoutStatus> statuses, int limit, CancellationToken cancellationToken) =>
        ReadAsync(
            $"{SelectView} where p.firm_id = $1 and (cardinality($2::text[]) = 0 or p.status = any($2)) order by p.requested_at desc limit $3",
            [firmId, statuses.Select(s => s.ToString()).ToArray(), limit],
            cancellationToken);

    /// <summary>The account's payouts, newest first.</summary>
    public Task<List<PayoutView>> ListByAccountAsync(string firmId, Guid challengeAccountId, CancellationToken cancellationToken) =>
        ReadAsync($"{SelectView} where p.firm_id = $1 and p.challenge_account_id = $2 order by p.requested_at desc", [firmId, challengeAccountId], cancellationToken);

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
            payouts.Add(new PayoutView(
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
                reader.IsDBNull(18) ? null : reader.GetString(18)));
        }

        return payouts;
    }

    private static DateTimeOffset? Time(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
}
