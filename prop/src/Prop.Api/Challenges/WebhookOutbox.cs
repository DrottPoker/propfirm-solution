using System.Text.Json.Nodes;

using Npgsql;

using NpgsqlTypes;

using Prop.Api.Firms;

namespace Prop.Api.Challenges;

/// <summary>
/// Queues webhooks for the firm in the caller's transaction, so a webhook is sent if and only if the change
/// behind it is saved. <see cref="WebhookWorker"/> delivers them.
/// </summary>
internal static class WebhookOutbox
{
    /// <summary>
    /// Queues <c>{ id, type, createdAt, account, data }</c> when the firm has a webhook. <paramref name="account"/>
    /// is the challenge account the event is about, if any.
    /// </summary>
    public static async Task AddAsync(
        NpgsqlConnection connection,
        Firm firm,
        string eventType,
        JsonObject? account,
        JsonNode? data,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (firm.Webhook is null)
        {
            return;
        }

        var id = Guid.CreateVersion7(now);
        var payload = new JsonObject
        {
            ["id"] = id,
            ["type"] = eventType,
            ["createdAt"] = now,
            ["account"] = account,
            ["data"] = data,
        };
        await using var command = new NpgsqlCommand(
            "insert into webhook_deliveries (id, firm_id, event_type, payload, created_at, next_attempt_at) values ($1, $2, $3, $4, $5, $5)",
            connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(firm.Id);
        command.Parameters.AddWithValue(eventType);
        command.Parameters.Add(new NpgsqlParameter { Value = payload.ToJsonString(), NpgsqlDbType = NpgsqlDbType.Jsonb });
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>The account as webhooks describe it.</summary>
    public static JsonObject Account(Guid id, long number, string email, string challengeId, string? reference) =>
        new()
        {
            ["id"] = id,
            ["number"] = number,
            ["email"] = email,
            ["challengeId"] = challengeId,
            ["reference"] = reference,
        };
}
