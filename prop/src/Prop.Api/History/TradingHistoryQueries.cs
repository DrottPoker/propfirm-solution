using System.Data;

using Common.Postgres;

using Npgsql;

using Prop.Api.Trading;

namespace Prop.Api.History;

/// <summary>What changed a trading account's balance.</summary>
public enum BalanceChangeKind
{
    /// <summary>The account was opened with its initial balance.</summary>
    Created,

    /// <summary>A position was opened, and its commission charged.</summary>
    Opened,

    /// <summary>A position was closed, with its profit and commission.</summary>
    Closed,

    /// <summary>Money was deposited or withdrawn, for example a payout's profit. Not a trading result.</summary>
    Adjusted,
}

internal sealed record BalanceChange(long Sequence, DateTimeOffset Time, BalanceChangeKind Kind, decimal Change, decimal BalanceAfter);

/// <summary>A closed position. <see cref="Result"/> is its profit after the commissions for opening and closing it.</summary>
internal sealed record ClosedTrade(
    string PositionId,
    string Symbol,
    TradeSide Side,
    decimal Volume,
    decimal OpenPrice,
    DateTimeOffset? OpenedAt,
    decimal ClosePrice,
    DateTimeOffset ClosedAt,
    decimal Profit,
    decimal Commission,
    string CloseReason,
    long CloseSequence)
{
    public decimal Result => Profit - Commission;
}

internal sealed record FloorLevel(DateTimeOffset Time, string FloorId, decimal Level);

/// <summary>A stage's trading account, when the stage started and how it was passed.</summary>
internal sealed record StageRecord(
    Guid ChallengeAccountId,
    string TradingAccountId,
    int Stage,
    DateTimeOffset? StartedAt,
    DateTimeOffset? PassedAt,
    decimal? PassedBalance,
    int? PassedTradingDays);

/// <summary>
/// What a stage's trading account has done, read at one moment so that its parts agree: every change to the balance
/// and every floor level, oldest first, the closed positions, newest first, and the days the rule engine counted.
/// </summary>
internal sealed record StageHistory(List<BalanceChange> Balance, List<FloorLevel> Floors, List<ClosedTrade> Trades, HashSet<DateOnly> CountedDays);

/// <summary>
/// A trading account's balance when the trading day started, null when it opened later that day, and the money
/// deposited or withdrawn since. <paramref name="HasHistory"/> is false until the history has the account.
/// </summary>
internal sealed record DayStart(decimal? Balance, decimal Adjustments, bool HasHistory);

/// <summary>Reads the trading history and the stages of challenge accounts the caller has already found for the firm.</summary>
internal sealed class TradingHistoryQueries(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    public async Task<ILookup<Guid, StageRecord>> StagesAsync(Guid[] challengeAccountIds, CancellationToken cancellationToken)
    {
        var stages = await ReadAsync(
            """
            select challenge_account_id, account_id, stage, started_at, passed_at, passed_balance, passed_trading_days
            from trading_accounts
            where challenge_account_id = any($1)
            order by stage
            """,
            [challengeAccountIds],
            r => new StageRecord(
                r.GetGuid(0),
                r.GetString(1),
                r.GetInt32(2),
                r.IsDBNull(3) ? null : r.GetFieldValue<DateTimeOffset>(3),
                r.IsDBNull(4) ? null : r.GetFieldValue<DateTimeOffset>(4),
                r.IsDBNull(5) ? null : r.GetDecimal(5),
                r.IsDBNull(6) ? null : r.GetInt32(6)),
            cancellationToken);
        return stages.ToLookup(s => s.ChallengeAccountId);
    }

    /// <summary>The balance of each trading account when its trading day started, and what was deposited or withdrawn since.</summary>
    public async Task<Dictionary<string, DayStart>> DayStartsAsync(
        IReadOnlyList<(string TradingAccountId, DateTimeOffset DayStartedAt)> accounts,
        CancellationToken cancellationToken)
    {
        if (accounts.Count == 0)
        {
            return [];
        }

        var starts = await ReadAsync(
            """
            select x.account_id,
                   (select c.balance_after from trading_balance_changes c
                    where c.account_id = x.account_id and c.time < x.day_started_at order by c.sequence desc limit 1),
                   (select coalesce(sum(c.change), 0) from trading_balance_changes c
                    where c.account_id = x.account_id and c.time >= x.day_started_at and c.kind = 'Adjusted'),
                   exists (select 1 from trading_balance_changes c where c.account_id = x.account_id)
            from unnest($1::text[], $2::timestamptz[]) as x (account_id, day_started_at)
            """,
            [accounts.Select(a => a.TradingAccountId).ToArray(), accounts.Select(a => a.DayStartedAt.ToUniversalTime()).ToArray()],
            r => (Account: r.GetString(0), Start: new DayStart(r.IsDBNull(1) ? null : r.GetDecimal(1), r.GetDecimal(2), r.GetBoolean(3))),
            cancellationToken);
        return starts.ToDictionary(s => s.Account, s => s.Start, StringComparer.Ordinal);
    }

    /// <summary>The newest event in each challenge account's history, or 0. It changes whenever the history does.</summary>
    public async Task<Dictionary<Guid, long>> LastSequencesAsync(Guid[] challengeAccountIds, CancellationToken cancellationToken)
    {
        var sequences = await ReadAsync(
            """
            select ta.challenge_account_id,
                   max(greatest(
                       coalesce((select max(c.sequence) from trading_balance_changes c where c.account_id = ta.account_id), 0),
                       coalesce((select max(f.sequence) from trading_floor_levels f where f.account_id = ta.account_id), 0)))
            from trading_accounts ta
            where ta.challenge_account_id = any($1)
            group by ta.challenge_account_id
            """,
            [challengeAccountIds],
            r => (Account: r.GetGuid(0), Sequence: r.GetInt64(1)),
            cancellationToken);
        return sequences.ToDictionary(s => s.Account, s => s.Sequence);
    }

    /// <summary>
    /// Everything the stage's trading account has done, in one snapshot, so that the balance, the floor levels, the
    /// positions and the counted days agree also while the history is being written.
    /// </summary>
    public async Task<StageHistory> StageHistoryAsync(Guid challengeAccountId, int stage, string tradingAccountId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var balance = await ReadAsync(
            connection,
            "select sequence, time, kind, change, balance_after from trading_balance_changes where account_id = $1 order by sequence",
            [tradingAccountId],
            r => new BalanceChange(r.GetInt64(0), r.GetFieldValue<DateTimeOffset>(1), Enum.Parse<BalanceChangeKind>(r.GetString(2)), r.GetDecimal(3), r.GetDecimal(4)),
            cancellationToken);
        var floors = await ReadAsync(
            connection,
            "select time, floor_id, level from trading_floor_levels where account_id = $1 order by sequence",
            [tradingAccountId],
            r => new FloorLevel(r.GetFieldValue<DateTimeOffset>(0), r.GetString(1), r.GetDecimal(2)),
            cancellationToken);
        var trades = await ClosedTradesAsync(connection, tradingAccountId, null, null, cancellationToken);
        var counted = await ReadAsync(
            connection,
            """
            select (o ->> 'day')::date
            from challenge_steps s, jsonb_array_elements(s.outputs) o
            where s.challenge_account_id = $1 and o ->> 'kind' = 'TradingDayCounted' and (o ->> 'stage')::int = $2
            """,
            [challengeAccountId, stage],
            r => r.GetFieldValue<DateOnly>(0),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StageHistory(balance, floors, trades, [.. counted]);
    }

    /// <summary>The account's closed positions, newest first: all of them, or a page of those closed before a sequence number.</summary>
    public async Task<List<ClosedTrade>> ClosedTradesAsync(string tradingAccountId, long? beforeSequence, int? limit, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ClosedTradesAsync(connection, tradingAccountId, beforeSequence, limit, cancellationToken);
    }

    private static Task<List<ClosedTrade>> ClosedTradesAsync(
        NpgsqlConnection connection,
        string tradingAccountId,
        long? beforeSequence,
        int? limit,
        CancellationToken cancellationToken) =>
        ReadAsync(
            connection,
            """
            select position_id, symbol, side, volume, open_price, opened_at, close_price, closed_at, profit,
                   open_commission + close_commission, close_reason, close_sequence
            from trading_positions
            where account_id = $1 and close_sequence is not null and ($2::bigint is null or close_sequence < $2)
            order by close_sequence desc
            limit $3::int
            """,
            [tradingAccountId, (object?)beforeSequence ?? DBNull.Value, (object?)limit ?? DBNull.Value],
            r => new ClosedTrade(
                r.GetString(0),
                r.GetString(1),
                Enum.Parse<TradeSide>(r.GetString(2)),
                r.GetDecimal(3),
                r.GetDecimal(4),
                r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5),
                r.GetDecimal(6),
                r.GetFieldValue<DateTimeOffset>(7),
                r.GetDecimal(8),
                r.GetDecimal(9),
                r.GetString(10),
                r.GetInt64(11)),
            cancellationToken);

    private async Task<List<T>> ReadAsync<T>(string sql, object[] parameters, Func<NpgsqlDataReader, T> read, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(connection, sql, parameters, read, cancellationToken);
    }

    private static async Task<List<T>> ReadAsync<T>(
        NpgsqlConnection connection,
        string sql,
        object[] parameters,
        Func<NpgsqlDataReader, T> read,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(read(reader));
        }

        return rows;
    }
}
