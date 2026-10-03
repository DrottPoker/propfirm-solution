using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Common.Postgres;

using Npgsql;

using Prop.Api.Firms;

namespace Prop.Api.Challenges;

/// <summary>
/// Delivers webhooks to the firms' systems. Each one is signed with the firm's secret and tried again with
/// growing pauses until the firm answers with a success, or given up after <see cref="MaxAttempts"/> tries.
/// </summary>
internal sealed partial class WebhookWorker(
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    FirmCatalog firms,
    IHttpClientFactory httpClients,
    WorkSignals signals,
    TimeProvider time,
    ILogger<WebhookWorker> logger) : BackgroundService
{
    public const string HttpClientName = "Webhooks";

    public const string SignatureHeader = "Prop-Signature";

    /// <summary>About a day and a half of tries with the growing pauses.</summary>
    public const int MaxAttempts = 16;

    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    /// <summary>A delivery in progress is not picked up again before this, in case the worker stops halfway.</summary>
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await schema.EnsureAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deliveries = await ClaimDueAsync(stoppingToken);
                foreach (var delivery in deliveries)
                {
                    await DeliverAsync(delivery, stoppingToken);
                }

                if (deliveries.Count > 0)
                {
                    continue;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }

            await signals.Webhooks.WaitAsync(PollInterval, time, stoppingToken);
        }
    }

    /// <summary>The signature of a body: <c>t={unix seconds},v1={hex HMAC-SHA256 of "{t}.{body}"}</c>.</summary>
    public static string Sign(string secret, DateTimeOffset at, string body)
    {
        var timestamp = at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(mac)}";
    }

    /// <summary>The pause before the next try: 30 seconds, doubled each time, at most 6 hours.</summary>
    public static TimeSpan RetryDelay(int attempts) => TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, attempts - 1), TimeSpan.FromHours(6).TotalSeconds));

    private async Task DeliverAsync(Delivery delivery, CancellationToken cancellationToken)
    {
        if (firms.ById(delivery.FirmId)?.Webhook is not { } webhook)
        {
            await FinishAsync(delivery, "failed_at", null, "The firm no longer has a webhook.", cancellationToken);
            return;
        }

        int? status = null;
        string? error = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url);
            request.Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json");
            request.Headers.Add("Prop-Webhook-Id", delivery.Id.ToString());
            request.Headers.Add("Prop-Webhook-Event", delivery.EventType);
            request.Headers.Add(SignatureHeader, Sign(webhook.Secret, time.GetUtcNow(), delivery.Payload));
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                await FinishAsync(delivery, "delivered_at", status, null, cancellationToken);
                return;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            error = exception.Message;
        }

        var attempts = delivery.Attempts + 1;
        if (attempts >= MaxAttempts)
        {
            LogGivenUp(logger, delivery.Id, delivery.FirmId);
            await FinishAsync(delivery, "failed_at", status, error, cancellationToken);
            return;
        }

        await using var command = dataSource.CreateCommand(
            "update webhook_deliveries set attempts = $2, next_attempt_at = $3, last_status = $4, last_error = $5 where id = $1");
        command.Parameters.AddWithValue(delivery.Id);
        command.Parameters.AddWithValue(attempts);
        command.Parameters.AddWithValue(time.GetUtcNow() + RetryDelay(attempts));
        command.Parameters.AddWithValue((object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task FinishAsync(Delivery delivery, string column, int? status, string? error, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"update webhook_deliveries set {column} = $2, attempts = attempts + 1, last_status = $3, last_error = $4 where id = $1");
        command.Parameters.AddWithValue(delivery.Id);
        command.Parameters.AddWithValue(time.GetUtcNow());
        command.Parameters.AddWithValue((object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Claimed deliveries are pushed into the future, so another worker does not pick them up meanwhile.
    private async Task<List<Delivery>> ClaimDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var command = dataSource.CreateCommand(
            """
            update webhook_deliveries set next_attempt_at = $2
            where id in (
                select id from webhook_deliveries
                where delivered_at is null and failed_at is null and next_attempt_at <= $1
                order by created_at limit 20
                for update skip locked)
            returning id, firm_id, event_type, payload, attempts, created_at
            """);
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(now + Lease);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var deliveries = new List<Delivery>();
        while (await reader.ReadAsync(cancellationToken))
        {
            deliveries.Add(new Delivery(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return [.. deliveries.OrderBy(d => d.CreatedAt)];
    }

    private sealed record Delivery(Guid Id, string FirmId, string EventType, string Payload, int Attempts, DateTimeOffset CreatedAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Delivering webhooks failed; trying again shortly")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Gave up webhook {DeliveryId} to firm {FirmId}")]
    private static partial void LogGivenUp(ILogger logger, Guid deliveryId, string firmId);
}
