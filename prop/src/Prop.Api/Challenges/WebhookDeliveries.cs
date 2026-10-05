using System.Text.Json.Nodes;

using Common.Postgres;

using Npgsql;

using Prop.Api.Firms;

namespace Prop.Api.Challenges;

/// <summary>Every event webhooks tell about, with what it means, for the firm's admin panel.</summary>
internal static class WebhookEvents
{
    public const string Test = "webhook.test";

    public static readonly IReadOnlyList<(string Type, string Description)> All =
    [
        ("account.stage_started", "A phase has its trading account."),
        ("account.passed", "A phase is passed."),
        ("account.funding_awaited", "Every phase is passed, and the funded account waits for your approval."),
        ("account.breached", "A loss limit was broken, with the level and the equity."),
        ("account.expired", "The challenge ran out of time, from a time limit or inactivity."),
        ("account.cancelled", "The account was cancelled."),
        ("account.paused", "The challenge is paused, since your month is not paid."),
        ("account.resumed", "The challenge goes on."),
        ("payout.requested", "A funded trader asked for a payout, and the profit left the trading account."),
        ("payout.approved", "You approved a payout."),
        ("payout.paid", "You marked a payout as paid."),
        ("payout.rejected", "You rejected a payout, with the reason."),
        ("order.paid", "A challenge bought in your portal is paid."),
        ("order.refunded", "The money for an order went back to the buyer."),
        ("order.disputed", "The buyer disputed the payment for an order."),
        ("trader.identity_verified", "Our built-in KYC approved a trader, with the name, date of birth and country from the document."),
        ("trader.identity_declined", "Our built-in KYC declined a trader, with the reason."),
        (Test, "A test you sent from the admin panel."),
    ];
}

/// <summary>A webhook to the firm and how its delivery went.</summary>
internal sealed record WebhookDelivery(
    Guid Id,
    string EventType,
    DateTimeOffset CreatedAt,
    int Attempts,
    int? LastStatus,
    string? LastError,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? FailedAt,
    DateTimeOffset NextAttemptAt);

/// <summary>The firm's webhooks as its admin panel shows them, and the test event it can send.</summary>
internal sealed class WebhookDeliveries(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    /// <summary>How many of the latest deliveries the admin panel shows.</summary>
    public const int Shown = 20;

    public async Task<IReadOnlyList<WebhookDelivery>> LatestAsync(string firmId, int limit, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            select id, event_type, created_at, attempts, last_status, last_error, delivered_at, failed_at, next_attempt_at
            from webhook_deliveries where firm_id = $1 order by created_at desc, id desc limit $2
            """);
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var deliveries = new List<WebhookDelivery>();
        while (await reader.ReadAsync(cancellationToken))
        {
            deliveries.Add(new WebhookDelivery(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetFieldValue<DateTimeOffset>(8)));
        }

        return deliveries;
    }

    /// <summary>Queues <see cref="WebhookEvents.Test"/>, signed like every other webhook.</summary>
    public async Task AddTestAsync(Firm firm, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await WebhookOutbox.AddAsync(
            connection,
            firm,
            WebhookEvents.Test,
            null,
            new JsonObject { ["message"] = $"A test webhook from {firm.Name}'s admin panel." },
            now,
            cancellationToken);
    }
}
