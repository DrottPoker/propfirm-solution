using Common.Postgres;

using Microsoft.Extensions.Options;

using Npgsql;

using Prop.Api.Configuration;
using Prop.Api.Firms;
using Prop.Api.Trading;

namespace Prop.Api.History;

/// <summary>
/// Keeps the trading history of the accounts the service opened: positions, balance changes and floor levels,
/// for the trader's dashboard (ADR 0022). It reads each firm's event stream with a cursor of its own, apart from
/// the rule engine's, so the history is filled in for accounts that traded before it existed, and reading the
/// stream again from the start rebuilds it. A page of events and the new cursor are stored in one transaction,
/// and every row is written once per event, so reading an event again changes nothing.
/// </summary>
internal sealed partial class TradingHistoryRecorder(
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    FirmCatalog firms,
    ITradingPlatform trading,
    IOptions<TradingPlatformOptions> options,
    TimeProvider time,
    ILogger<TradingHistoryRecorder> logger) : BackgroundService
{
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await schema.EnsureAsync(stoppingToken);
        await FirmLoops.RunAsync(firms, f => f.Trading is not null, RunAsync, stoppingToken);
    }

    private async Task RunAsync(string firmId, CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var firm = firms.ById(firmId)!;
                var cursor = await CursorAsync(firm, cancellationToken);
                var page = await trading.ReadEventsAsync(firm.Trading!, cursor, options.Value.EventsPerRequest, options.Value.EventWaitSeconds, cancellationToken);
                if (page.Events.Count > 0)
                {
                    await RecordAsync(firm, page, cancellationToken);
                }

                retryDelay = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The page is read again from the stored cursor, so nothing is skipped.
                LogRetry(logger, firmId, retryDelay, exception);
                await Task.Delay(retryDelay, time, cancellationToken);
                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }
    }

    internal async Task RecordAsync(Firm firm, TradingEventPage page, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Accounts the service did not open for this firm, for example the firm's own, are not kept.
        var ours = await OwnAccountsAsync(connection, firm, [.. page.Events.Select(e => e.AccountId).Distinct(StringComparer.Ordinal)], cancellationToken);
        foreach (var tradingEvent in page.Events.Where(e => ours.Contains(e.AccountId)))
        {
            await RecordAsync(connection, tradingEvent, cancellationToken);
        }

        await ExecuteAsync(
            connection,
            """
            insert into trading_history_cursors (firm_id, after_sequence) values ($1, $2)
            on conflict (firm_id) do update set after_sequence = excluded.after_sequence
            """,
            [firm.Id, page.Cursor],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task RecordAsync(NpgsqlConnection connection, TradingEvent tradingEvent, CancellationToken cancellationToken)
    {
        var (account, sequence, time) = (tradingEvent.AccountId, tradingEvent.Sequence, tradingEvent.Time.ToUniversalTime());
        switch (tradingEvent)
        {
            case TradingAccountCreated created:
                await BalanceChangedAsync(connection, account, sequence, time, BalanceChangeKind.Created, created.Balance, created.Balance, cancellationToken);
                break;
            case TradingPositionOpened opened:
                await ExecuteAsync(
                    connection,
                    """
                    insert into trading_positions (account_id, position_id, symbol, side, volume, open_price, opened_at, open_commission)
                    values ($1, $2, $3, $4, $5, $6, $7, $8)
                    on conflict (account_id, position_id) do update set opened_at = excluded.opened_at, open_commission = excluded.open_commission
                    """,
                    [account, opened.PositionId, opened.Symbol, opened.Side.ToString(), opened.Volume, opened.OpenPrice, time, opened.Commission],
                    cancellationToken);
                await BalanceChangedAsync(connection, account, sequence, time, BalanceChangeKind.Opened, -opened.Commission, opened.BalanceAfter, cancellationToken);
                break;
            case TradingPositionClosed closed:
                // The close carries the position's own fields, so it is complete also if its opening was never seen.
                await ExecuteAsync(
                    connection,
                    """
                    insert into trading_positions
                        (account_id, position_id, symbol, side, volume, open_price, close_price, closed_at, close_commission, profit, close_reason, close_sequence,
                         close_volume)
                    values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $5)
                    on conflict (account_id, position_id) do update set
                        close_price = excluded.close_price, closed_at = excluded.closed_at, close_commission = excluded.close_commission,
                        profit = excluded.profit, close_reason = excluded.close_reason, close_sequence = excluded.close_sequence,
                        close_volume = excluded.close_volume
                    """,
                    [
                        account, closed.PositionId, closed.Symbol, closed.Side.ToString(), closed.Volume, closed.OpenPrice, closed.ClosePrice, time,
                        closed.Commission, closed.Profit, closed.Reason, sequence,
                    ],
                    cancellationToken);
                await BalanceChangedAsync(
                    connection, account, sequence, time, BalanceChangeKind.Closed, closed.Profit - closed.Commission, closed.BalanceAfter, cancellationToken);
                break;
            case TradingPositionPartiallyClosed part:
                await ExecuteAsync(
                    connection,
                    """
                    insert into trading_partial_closes (account_id, sequence, position_id, time, volume, close_price, profit, commission)
                    values ($1, $2, $3, $4, $5, $6, $7, $8)
                    on conflict (account_id, sequence) do nothing
                    """,
                    [account, sequence, part.PositionId, time, part.Volume, part.ClosePrice, part.Profit, part.Commission],
                    cancellationToken);
                await BalanceChangedAsync(
                    connection, account, sequence, time, BalanceChangeKind.Closed, part.Profit - part.Commission, part.BalanceAfter, cancellationToken);
                break;
            case TradingBalanceAdjusted adjusted:
                await BalanceChangedAsync(connection, account, sequence, time, BalanceChangeKind.Adjusted, adjusted.Amount, adjusted.BalanceAfter, cancellationToken);
                break;
            case TradingAccountReopened reopened:
                // The change is from the balance the account ended with.
                await ExecuteAsync(
                    connection,
                    """
                    insert into trading_balance_changes (account_id, sequence, time, kind, change, balance_after)
                    values ($1, $2, $3, $4, $5 - coalesce((select balance_after from trading_balance_changes where account_id = $1 and sequence < $2 order by sequence desc limit 1), 0), $5)
                    on conflict (account_id, sequence) do nothing
                    """,
                    [account, sequence, time, BalanceChangeKind.Reopened.ToString(), reopened.Balance],
                    cancellationToken);
                break;
            case TradingFloorSet floorSet:
                await ExecuteAsync(
                    connection,
                    """
                    insert into trading_floor_levels (account_id, sequence, time, floor_id, level) values ($1, $2, $3, $4, $5)
                    on conflict (account_id, sequence) do nothing
                    """,
                    [account, sequence, time, floorSet.FloorId, floorSet.Level],
                    cancellationToken);
                break;
            default:
                break;
        }
    }

    private static Task BalanceChangedAsync(
        NpgsqlConnection connection,
        string account,
        long sequence,
        DateTimeOffset time,
        BalanceChangeKind kind,
        decimal change,
        decimal balanceAfter,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            """
            insert into trading_balance_changes (account_id, sequence, time, kind, change, balance_after) values ($1, $2, $3, $4, $5, $6)
            on conflict (account_id, sequence) do nothing
            """,
            [account, sequence, time, kind.ToString(), change, balanceAfter],
            cancellationToken);

    private static async Task<HashSet<string>> OwnAccountsAsync(NpgsqlConnection connection, Firm firm, string[] accountIds, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            select ta.account_id
            from trading_accounts ta join challenge_accounts a on a.id = ta.challenge_account_id
            where ta.account_id = any($1) and a.firm_id = $2
            """,
            connection);
        command.Parameters.AddWithValue(accountIds);
        command.Parameters.AddWithValue(firm.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var ours = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            ours.Add(reader.GetString(0));
        }

        return ours;
    }

    private async Task<long> CursorAsync(Firm firm, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("select after_sequence from trading_history_cursors where firm_id = $1");
        command.Parameters.AddWithValue(firm.Id);
        return await command.ExecuteScalarAsync(cancellationToken) as long? ?? 0;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording the trading history of firm {FirmId} failed; trying again in {Delay}")]
    private static partial void LogRetry(ILogger logger, string firmId, TimeSpan delay, Exception exception);
}
