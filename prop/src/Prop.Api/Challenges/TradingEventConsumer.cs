using Common.Postgres;

using Microsoft.Extensions.Options;

using Npgsql;

using Prop.Api.Configuration;
using Prop.Api.Firms;
using Prop.Api.Trading;
using Prop.Rules;

namespace Prop.Api.Challenges;

/// <summary>
/// Reads each firm's event stream from the trading platform and turns the events into facts for the rule
/// engine. A page of events, the decisions they lead to and the new cursor are stored in one transaction,
/// so every event is acted on exactly once, in order, also across restarts.
/// </summary>
internal sealed partial class TradingEventConsumer(
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    FirmCatalog firms,
    ITradingPlatform trading,
    ChallengeService challenges,
    IOptions<TradingPlatformOptions> options,
    TimeProvider time,
    ILogger<TradingEventConsumer> logger) : BackgroundService
{
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await schema.EnsureAsync(stoppingToken);
        await Task.WhenAll(firms.All.Select(firm => RunAsync(firm, stoppingToken)));
    }

    private async Task RunAsync(Firm firm, CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var cursor = await CursorAsync(firm, cancellationToken);
                var page = await trading.ReadEventsAsync(firm.Trading, cursor, options.Value.EventsPerRequest, options.Value.EventWaitSeconds, cancellationToken);
                if (page.Events.Count > 0)
                {
                    await HandleAsync(firm, page, cancellationToken);
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
                LogRetry(logger, firm.Id, retryDelay, exception);
                await Task.Delay(retryDelay, time, cancellationToken);
                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }
    }

    internal async Task HandleAsync(Firm firm, TradingEventPage page, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var tradingEvent in page.Events)
        {
            await HandleAsync(connection, firm, tradingEvent, cancellationToken);
        }

        await using (var command = new NpgsqlCommand(
            """
            insert into trading_cursors (firm_id, after_sequence) values ($1, $2)
            on conflict (firm_id) do update set after_sequence = excluded.after_sequence
            """,
            connection))
        {
            command.Parameters.AddWithValue(firm.Id);
            command.Parameters.AddWithValue(page.Cursor);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        challenges.Notify(firm);
    }

    private async Task HandleAsync(NpgsqlConnection connection, Firm firm, TradingEvent tradingEvent, CancellationToken cancellationToken)
    {
        // Accounts the prop platform did not open, for example the firm's own, are none of its business.
        if (await ChallengeAccountOfAsync(connection, tradingEvent.AccountId, cancellationToken) is not { } accountId)
        {
            return;
        }

        var (time, account, sequence) = (tradingEvent.Time, tradingEvent.AccountId, tradingEvent.Sequence);
        switch (tradingEvent)
        {
            case TradingAccountCreated created:
                await UpdateAsync(connection, "created = true, balance = $2", account, created.Balance, cancellationToken);
                await challenges.ApplyAsync(
                    connection, firm, accountId, s => new AccountOpened(time, account, sequence, TradingDays.DayOf(time, s.Definition.TradingDay)), null, cancellationToken);
                break;
            case TradingPositionOpened opened:
                await UpdateAsync(connection, "balance = $2, open_positions = open_positions + 1", account, opened.BalanceAfter, cancellationToken);
                await challenges.ApplyAsync(
                    connection, firm, accountId, s => new PositionOpened(time, account, sequence, TradingDays.DayOf(time, s.Definition.TradingDay)), null, cancellationToken);
                break;
            case TradingPositionClosed closed:
                var openPositions = await UpdateAsync(
                    connection, "balance = $2, open_positions = greatest(open_positions - 1, 0)", account, closed.BalanceAfter, cancellationToken);
                await challenges.ApplyAsync(
                    connection, firm, accountId, _ => new AccountUpdated(time, account, sequence, closed.BalanceAfter, openPositions), null, cancellationToken);
                break;
            case TradingFloorSet floorSet when floorSet.FloorId is FloorIds.Daily or FloorIds.MaxLoss:
                var column = floorSet.FloorId == FloorIds.Daily ? "daily_floor" : "max_loss_floor";
                await UpdateAsync(connection, $"{column} = $2", account, floorSet.Level, cancellationToken);
                break;
            case TradingFloorBreached breached:
                await challenges.ApplyAsync(
                    connection,
                    firm,
                    accountId,
                    _ => new FloorBreached(time, account, sequence, breached.FloorId, breached.Level, breached.Equity),
                    breached.Raw,
                    cancellationToken);
                break;
            case TradingAccountDisabled:
                await UpdateAsync(connection, "disabled = true", account, null, cancellationToken);
                await challenges.ApplyAsync(connection, firm, accountId, _ => new AccountDisabled(time, account, sequence), null, cancellationToken);
                break;
            default:
                break;
        }
    }

    private static async Task<Guid?> ChallengeAccountOfAsync(NpgsqlConnection connection, string tradingAccountId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select challenge_account_id from trading_accounts where account_id = $1", connection);
        command.Parameters.AddWithValue(tradingAccountId);
        return await command.ExecuteScalarAsync(cancellationToken) as Guid?;
    }

    /// <summary>Updates what the trading platform last reported for the account, and returns its open positions.</summary>
    private static async Task<int> UpdateAsync(NpgsqlConnection connection, string set, string tradingAccountId, decimal? value, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"update trading_accounts set {set} where account_id = $1 returning open_positions", connection);
        command.Parameters.AddWithValue(tradingAccountId);
        if (value is { } reported)
        {
            command.Parameters.AddWithValue(reported);
        }

        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<long> CursorAsync(Firm firm, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("select after_sequence from trading_cursors where firm_id = $1");
        command.Parameters.AddWithValue(firm.Id);
        return await command.ExecuteScalarAsync(cancellationToken) as long? ?? 0;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the trading platform's events for firm {FirmId} failed; trying again in {Delay}")]
    private static partial void LogRetry(ILogger logger, string firmId, TimeSpan delay, Exception exception);
}
