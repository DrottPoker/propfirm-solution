using System.Text.Json;

using Common.Postgres;

using Npgsql;

using Prop.Api.Email;
using Prop.Api.Firms;
using Prop.Api.Json;
using Prop.Rules;

namespace Prop.Api.Challenges;

/// <summary>
/// Reminds traders whose challenge ends soon because no trade was opened (ADR 0025): once per deadline, when the last day to
/// open a trade is at most <see cref="Notifications.InactivityReminderDays"/> days away. Looks every half hour.
/// </summary>
internal sealed partial class InactivityReminderWorker(
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    FirmCatalog firms,
    WorkSignals signals,
    TimeProvider time,
    ILogger<InactivityReminderWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await schema.EnsureAsync(stoppingToken);
        await firms.Ready.WaitAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RemindAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }

            await Task.Delay(Interval, time, stoppingToken);
        }
    }

    /// <summary>Queues the reminders that are due now. Repeating it queues nothing new.</summary>
    public async Task RemindAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var due = new List<(string FirmId, NotifiedAccount Account, DateOnly EndsOn)>();
        await using (var command = new NpgsqlCommand(
            """
            select a.firm_id, a.id, a.number, t.email, a.state
            from challenge_accounts a join traders t on t.id = a.trader_id
            where a.status = 'Active' and not a.paused and a.current_day is not null
              and (a.state->>'inactivityDeadline')::date - a.current_day <= $1
            """,
            connection))
        {
            command.Parameters.AddWithValue(Notifications.InactivityReminderDays + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var state = JsonSerializer.Deserialize<ChallengeState>(reader.GetString(4), PropJson.Options)!;
                if (state.InactivityDeadline is { } endsOn && state.CurrentDay is { } today && endsOn > today)
                {
                    due.Add((reader.GetString(0), new NotifiedAccount(reader.GetGuid(1), reader.GetInt64(2), reader.GetString(3), state.Definition), endsOn));
                }
            }
        }

        foreach (var (firmId, account, endsOn) in due)
        {
            if (firms.ById(firmId) is { } firm)
            {
                await Notifications.QueueInactivityReminderAsync(connection, firm, account, endsOn, time.GetUtcNow(), cancellationToken);
            }
        }

        if (due.Count > 0)
        {
            signals.Emails.Set();
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Queuing inactivity reminders failed; trying again later")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
