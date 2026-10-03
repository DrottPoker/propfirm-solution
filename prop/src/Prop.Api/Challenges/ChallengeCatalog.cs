using System.Text.Json;

using Common.Postgres;

using Npgsql;

using NpgsqlTypes;

using Prop.Api.Json;
using Prop.Rules;

namespace Prop.Api.Challenges;

/// <summary>The challenges each firm sells. Changing one never affects accounts already started, which keep their copy.</summary>
internal sealed class ChallengeCatalog(NpgsqlDataSource dataSource, DatabaseSchema schema, TimeProvider time)
{
    /// <summary>What is wrong with the definition, including a time zone the server does not know. Empty when it is valid.</summary>
    public static IReadOnlyList<string> Validate(ChallengeDefinition definition)
    {
        // Parts can be missing when the definition comes from JSON.
        if (definition.TradingDay is null || definition.Evaluation is null || definition.Funded is null || definition.Evaluation.Any(s => s is null))
        {
            return ["The challenge needs a trading day, evaluation stages and a funded stage."];
        }

        var errors = definition.Validate().ToList();
        if (definition.TradingDay.TimeZone.Trim().Length > 0 && !TradingDays.IsKnownTimeZone(definition.TradingDay.TimeZone))
        {
            errors.Add($"Unknown time zone {definition.TradingDay.TimeZone}. Use an IANA name such as Europe/Stockholm.");
        }

        return errors;
    }

    /// <summary>Creates or replaces the challenge. Validate it first.</summary>
    public Task SaveAsync(string firmId, ChallengeDefinition definition, CancellationToken cancellationToken) =>
        WriteAsync(
            """
            insert into challenge_definitions (firm_id, id, definition, updated_at) values ($1, $2, $3, $4)
            on conflict (firm_id, id) do update set definition = excluded.definition, updated_at = excluded.updated_at
            """,
            firmId,
            definition,
            cancellationToken);

    public async Task<IReadOnlyList<ChallengeDefinition>> ListAsync(string firmId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select definition from challenge_definitions where firm_id = $1 order by id");
        command.Parameters.AddWithValue(firmId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var definitions = new List<ChallengeDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            definitions.Add(JsonSerializer.Deserialize<ChallengeDefinition>(reader.GetString(0), PropJson.Options)!);
        }

        return definitions;
    }

    private async Task WriteAsync(string sql, string firmId, ChallengeDefinition definition, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(definition.Id);
        command.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.Serialize(definition, PropJson.Options), NpgsqlDbType = NpgsqlDbType.Jsonb });
        command.Parameters.AddWithValue(time.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
