using Common.Postgres;

using Npgsql;

using NpgsqlTypes;

using Prop.Api.Challenges;
using Prop.Api.Review;

namespace Prop.Api.Email;

/// <summary>
/// Queues emails, so they are sent by <see cref="EmailWorker"/> and tried again while the mail server is down. Queued in
/// the caller's transaction, an email goes out if and only if the change it tells about is saved.
/// </summary>
internal static class EmailOutbox
{
    /// <summary>
    /// Queues the email in the caller's transaction. With <paramref name="dedupeKey"/>, an email that was queued before with
    /// the same key is not queued again. The caller sets <see cref="WorkSignals.Emails"/> after committing. An email about
    /// a firm we have not approved is withheld, kept but never sent, unless it goes to one of the firm's administrators (ADR 0043).
    /// </summary>
    public static async Task AddAsync(
        NpgsqlConnection connection,
        EmailMessage message,
        string kind,
        string? firmId,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        string? dedupeKey = null)
    {
        await using var command = new NpgsqlCommand(
            $"""
            insert into email_outbox (id, firm_id, kind, to_address, from_name, subject, body, dedupe_key, created_at, next_attempt_at, html_body, reply_to, withheld_at)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $9, $10, $11, case when $2::text is null or {FirmApproval.MayReachSql("$2", "$12")} then null else $9 end)
            on conflict (dedupe_key) do nothing
            """,
            connection);
        command.Parameters.AddWithValue(Guid.CreateVersion7(now));
        command.Parameters.Add(Text(firmId));
        command.Parameters.AddWithValue(kind);
        command.Parameters.AddWithValue(message.To);
        command.Parameters.Add(Text(message.FromName));
        command.Parameters.AddWithValue(message.Subject);
        command.Parameters.AddWithValue(message.Body);
        command.Parameters.Add(Text(dedupeKey));
        command.Parameters.AddWithValue(now);
        command.Parameters.Add(Text(message.Html));
        command.Parameters.Add(Text(message.ReplyTo));
        command.Parameters.AddWithValue(Emails.Normalize(message.To));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Queues the email on its own and wakes the worker.</summary>
    public static async Task AddAsync(
        NpgsqlDataSource dataSource,
        DatabaseSchema schema,
        WorkSignals signals,
        EmailMessage message,
        string kind,
        string? firmId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await AddAsync(connection, message, kind, firmId, now, cancellationToken);
        signals.Emails.Set();
    }

    private static NpgsqlParameter Text(string? value) => new() { Value = (object?)value ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text };
}

/// <summary>
/// Sends the queued emails, oldest first. One the mail server does not take is tried again with growing pauses, and
/// given up after <see cref="MaxAttempts"/> tries.
/// </summary>
internal sealed partial class EmailWorker(
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    IEmailSender sender,
    WorkSignals signals,
    TimeProvider time,
    ILogger<EmailWorker> logger) : BackgroundService
{
    /// <summary>About a day of tries with the growing pauses.</summary>
    public const int MaxAttempts = 12;

    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    /// <summary>An email being sent is not picked up again before this, in case the worker stops halfway.</summary>
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    /// <summary>The pause before the next try: 30 seconds, doubled each time, at most 4 hours.</summary>
    public static TimeSpan RetryDelay(int attempts) => TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, attempts - 1), TimeSpan.FromHours(4).TotalSeconds));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await schema.EnsureAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var emails = await ClaimDueAsync(stoppingToken);
                foreach (var email in emails)
                {
                    await SendAsync(email, stoppingToken);
                }

                if (emails.Count > 0)
                {
                    continue;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }

            await signals.Emails.WaitAsync(PollInterval, time, stoppingToken);
        }
    }

    private async Task SendAsync(QueuedEmail email, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(new EmailMessage(email.To, email.Subject, email.Body, email.FromName, email.Html, email.ReplyTo), cancellationToken);
            await UpdateAsync("sent_at = $2", email.Id, time.GetUtcNow(), "", cancellationToken);
        }
        catch (EmailNotSentException exception)
        {
            var attempts = email.Attempts + 1;
            if (attempts >= MaxAttempts)
            {
                LogGivenUp(logger, email.Id, email.Kind);
                await UpdateAsync("failed_at = $2", email.Id, time.GetUtcNow(), exception.Message, cancellationToken);
            }
            else
            {
                await UpdateAsync("next_attempt_at = $2", email.Id, time.GetUtcNow() + RetryDelay(attempts), exception.Message, cancellationToken);
            }
        }
    }

    // Sets the column to the time and counts the try. An empty error clears the last one.
    private async Task UpdateAsync(string set, Guid id, DateTimeOffset at, string error, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"update email_outbox set {set}, attempts = attempts + 1, last_error = nullif($3, '') where id = $1");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(at);
        command.Parameters.AddWithValue(error);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Claimed emails are pushed into the future, so another worker does not pick them up meanwhile.
    private async Task<List<QueuedEmail>> ClaimDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var command = dataSource.CreateCommand(
            """
            update email_outbox set next_attempt_at = $2
            where id in (
                select id from email_outbox
                where sent_at is null and failed_at is null and withheld_at is null and next_attempt_at <= $1
                order by created_at limit 20
                for update skip locked)
            returning id, kind, to_address, from_name, subject, body, attempts, created_at, html_body, reply_to
            """);
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(now + Lease);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var emails = new List<QueuedEmail>();
        while (await reader.ReadAsync(cancellationToken))
        {
            emails.Add(new QueuedEmail(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return [.. emails.OrderBy(e => e.CreatedAt)];
    }

    private sealed record QueuedEmail(
        Guid Id, string Kind, string To, string? FromName, string Subject, string Body, int Attempts, DateTimeOffset CreatedAt, string? Html, string? ReplyTo);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending queued emails failed; trying again shortly")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Gave up email {EmailId} ({Kind})")]
    private static partial void LogGivenUp(ILogger logger, Guid emailId, string kind);
}
