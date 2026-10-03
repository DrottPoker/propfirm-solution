using System.Security.Cryptography;
using System.Text.Json;

using Common.Postgres;

using Npgsql;

using Prop.Api.Firms;
using Prop.Api.Json;
using Prop.Api.Trading;

namespace Prop.Api.Challenges;

/// <summary>
/// Runs the queued commands on the trading platform, one firm at a time in the order they were queued, so an
/// account is always opened before its floors are set. A command that could not reach the platform is tried
/// again and holds back the firm's later commands. A command the platform refuses is set aside as failed.
/// </summary>
internal sealed partial class TradingCommandWorker(
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    FirmCatalog firms,
    ITradingPlatform trading,
    WorkSignals signals,
    TimeProvider time,
    ILogger<TradingCommandWorker> logger) : BackgroundService
{
    /// <summary>How often the queue is checked without a signal, for example after a restart.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

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
            long? current = null;
            try
            {
                if (await NextAsync(firm, cancellationToken) is not { } next)
                {
                    await signals.CommandsOf(firm.Id).WaitAsync(PollInterval, time, cancellationToken);
                    continue;
                }

                current = next.Id;
                await ExecuteCommandAsync(firm, next.Command, cancellationToken);
                await FinishAsync(next.Id, "done_at", null, cancellationToken);
                retryDelay = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (TradingPlatformRejectedException exception) when (current is { } id)
            {
                LogRejected(logger, firm.Id, exception);
                if (!await TryFinishAsync(id, "failed_at", exception.Message, cancellationToken))
                {
                    await Task.Delay(retryDelay, time, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The trading platform or the database is unavailable. The same command is tried again.
                LogRetry(logger, firm.Id, retryDelay, exception);
                if (current is { } id)
                {
                    await TryFinishAsync(id, null, exception.Message, cancellationToken);
                }

                await Task.Delay(retryDelay, time, cancellationToken);
                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }
    }

    private async Task ExecuteCommandAsync(Firm firm, TradingCommand command, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case OpenTradingAccount open:
                var userId = await TradingUserAsync(firm, open.TraderId, cancellationToken);
                await trading.OpenAccountAsync(firm.Trading, open.AccountId, open.InitialBalance, userId, cancellationToken);
                break;
            case SetTradingFloor floor:
                await trading.SetFloorAsync(firm.Trading, floor.AccountId, floor.FloorId, floor.Floor, cancellationToken);
                break;
            case CloseTradingAccount close:
                await trading.CloseAccountAsync(firm.Trading, close.AccountId, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Unknown command {command.GetType().Name}.");
        }
    }

    /// <summary>
    /// The trader's user on the trading platform, created on first use. Traders get in through login links,
    /// so the password is random and never shown. The portal lets them choose their own.
    /// </summary>
    private async Task<Guid> TradingUserAsync(Firm firm, Guid traderId, CancellationToken cancellationToken)
    {
        await using var read = dataSource.CreateCommand("select email, trading_user_id from traders where id = $1");
        read.Parameters.AddWithValue(traderId);
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Trader {traderId} does not exist.");
        }

        var email = reader.GetString(0);
        if (!reader.IsDBNull(1))
        {
            return reader.GetGuid(1);
        }

        var userId = await trading.EnsureUserAsync(firm.Trading, email, RandomNumberGenerator.GetHexString(32), cancellationToken);
        await using var write = dataSource.CreateCommand("update traders set trading_user_id = $2 where id = $1");
        write.Parameters.AddWithValue(traderId);
        write.Parameters.AddWithValue(userId);
        await write.ExecuteNonQueryAsync(cancellationToken);
        return userId;
    }

    private async Task<(long Id, TradingCommand Command)?> NextAsync(Firm firm, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "select id, command from trading_commands where firm_id = $1 and done_at is null and failed_at is null order by id limit 1");
        command.Parameters.AddWithValue(firm.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetInt64(0), JsonSerializer.Deserialize<TradingCommand>(reader.GetString(1), PropJson.Options)!)
            : null;
    }

    /// <summary>Counts an attempt, and marks the command done or failed when <paramref name="column"/> names it.</summary>
    private async Task FinishAsync(long id, string? column, string? error, CancellationToken cancellationToken)
    {
        var finished = column is null ? "" : $"{column} = $2, ";
        await using var command = dataSource.CreateCommand($"update trading_commands set {finished}attempts = attempts + 1, last_error = $3 where id = $1");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(time.GetUtcNow());
        command.Parameters.AddWithValue((object?)error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>False when the database could not be reached either. The command then stays queued and is tried again.</summary>
    private async Task<bool> TryFinishAsync(long id, string? column, string error, CancellationToken cancellationToken)
    {
        try
        {
            await FinishAsync(id, column, error, cancellationToken);
            return true;
        }
        catch (NpgsqlException exception)
        {
            LogNotRecorded(logger, id, exception);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The trading platform refused a command for firm {FirmId}; it is set aside")]
    private static partial void LogRejected(ILogger logger, string firmId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not record the outcome of trading command {CommandId}")]
    private static partial void LogNotRecorded(ILogger logger, long commandId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A command for firm {FirmId} failed and is tried again in {Delay}")]
    private static partial void LogRetry(ILogger logger, string firmId, TimeSpan delay, Exception exception);
}
