using Common.Postgres;

using Npgsql;

using Prop.Api.Firms;
using Prop.Rules;

namespace Prop.Api.Challenges;

/// <summary>
/// Starts the trading day for every open challenge account when its day begins, which resets the daily loss
/// floor on the trading platform. After a restart, accounts whose day has moved on are caught up at once. A day
/// that ends a challenge for running out of time waits until the firm's events from before it are handled, so a
/// trade made just in time always counts.
/// </summary>
internal sealed partial class TradingDayScheduler(
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    FirmCatalog firms,
    ChallengeService challenges,
    TradingStreamProgress progress,
    TimeProvider time,
    ILogger<TradingDayScheduler> logger) : BackgroundService
{
    /// <summary>The longest wait between looks, so new accounts and changed time zones are picked up.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(1);

    /// <summary>How far past the start of a day the events must be read before it ends a challenge, for clocks that differ a little.</summary>
    public static readonly TimeSpan StreamMargin = TimeSpan.FromSeconds(30);

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

            await progress.CaughtUp.WaitAsync(wait, time, stoppingToken);
        }
    }

    /// <summary>Starts the day where it has begun, and returns how long until the next day begins anywhere.</summary>
    internal async Task<TimeSpan> StartDueDaysAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var next = now + MaxWait;
        foreach (var account in await OpenAccountsAsync(cancellationToken))
        {
            var start = TradingDays.NextStart(now, account.Definition);
            next = start < next ? start : next;

            var day = TradingDays.DayOf(now, account.Definition);
            if ((account.CurrentDay is not null && day <= account.CurrentDay) || firms.ById(account.FirmId) is not { } firm)
            {
                continue;
            }

            if (account.ExpiresOn(day) && !(progress.CaughtUpAt(firm.Id) >= TradingDays.StartOf(day, account.Definition) + StreamMargin))
            {
                progress.Await();
                continue;
            }

            await challenges.ApplyAsync(firm, account.Id, new TradingDayStarted(now, day), cancellationToken);
        }

        // A moment past the start, so the new day is certainly the current one.
        return next - now + TimeSpan.FromMilliseconds(10);
    }

    private async Task<List<OpenAccount>> OpenAccountsAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            select firm_id, id, day_time_zone, day_start, current_day, status = 'Active' and not paused,
                   (state ->> 'stageDeadline')::date, (state ->> 'inactivityDeadline')::date
            from challenge_accounts where status not in ('Failed', 'Cancelled')
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var accounts = new List<OpenAccount>();
        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add(new OpenAccount(
                reader.GetString(0),
                reader.GetGuid(1),
                new TradingDayDefinition(reader.GetString(2), reader.GetFieldValue<TimeOnly>(3)),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
                reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7)));
        }

        return accounts;
    }

    /// <summary>An account that has not ended, with the deadlines that can end it when it is traded and not paused.</summary>
    private sealed record OpenAccount(
        string FirmId,
        Guid Id,
        TradingDayDefinition Definition,
        DateOnly? CurrentDay,
        bool Running,
        DateOnly? StageDeadline,
        DateOnly? InactivityDeadline)
    {
        public bool ExpiresOn(DateOnly day) => Running && (day >= StageDeadline || day >= InactivityDeadline);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Starting trading days failed; trying again shortly")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
