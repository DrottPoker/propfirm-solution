using Common.Postgres;

using Npgsql;

using Prop.Api.Firms;
using Prop.Rules;

namespace Prop.Api.Challenges;

/// <summary>
/// Starts the trading day for every open challenge account when its day begins, which resets the daily loss
/// floor on the trading platform. After a restart, accounts whose day has moved on are caught up at once.
/// </summary>
internal sealed partial class TradingDayScheduler(
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    FirmCatalog firms,
    ChallengeService challenges,
    TimeProvider time,
    ILogger<TradingDayScheduler> logger) : BackgroundService
{
    /// <summary>The longest wait between looks, so new accounts and changed time zones are picked up.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await schema.EnsureAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = MaxWait;
            try
            {
                wait = await StartDueDaysAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }

            await Task.Delay(wait, time, stoppingToken);
        }
    }

    /// <summary>Starts the day where it has begun, and returns how long until the next day begins anywhere.</summary>
    internal async Task<TimeSpan> StartDueDaysAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var next = now + MaxWait;
        foreach (var (firmId, accountId, definition, currentDay) in await OpenAccountsAsync(cancellationToken))
        {
            var start = TradingDays.NextStart(now, definition);
            next = start < next ? start : next;

            var day = TradingDays.DayOf(now, definition);
            if ((currentDay is null || day > currentDay) && firms.ById(firmId) is { } firm)
            {
                await challenges.ApplyAsync(firm, accountId, new TradingDayStarted(now, day), cancellationToken);
            }
        }

        // A moment past the start, so the new day is certainly the current one.
        return next - now + TimeSpan.FromMilliseconds(10);
    }

    private async Task<List<(string FirmId, Guid AccountId, TradingDayDefinition Definition, DateOnly? CurrentDay)>> OpenAccountsAsync(
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "select firm_id, id, day_time_zone, day_start, current_day from challenge_accounts where status not in ('Failed', 'Cancelled')");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var accounts = new List<(string, Guid, TradingDayDefinition, DateOnly?)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add((
                reader.GetString(0),
                reader.GetGuid(1),
                new TradingDayDefinition(reader.GetString(2), reader.GetFieldValue<TimeOnly>(3)),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4)));
        }

        return accounts;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Starting trading days failed; trying again shortly")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
