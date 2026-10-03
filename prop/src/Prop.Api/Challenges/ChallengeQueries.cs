using System.Text.Json;

using Common.Postgres;

using Npgsql;

namespace Prop.Api.Challenges;

/// <summary>A challenge account with what the trading platform last reported for its current trading account.</summary>
internal sealed record AccountView(ChallengeAccount Account, TradingFigures? Trading);

internal sealed record TradingFigures(decimal? Balance, int OpenPositions, decimal? DailyFloor, decimal? MaxLossFloor);

/// <summary>One recorded step: the input and what the rule engine decided, with the trading platform's event behind it.</summary>
internal sealed record StepView(int Step, DateTimeOffset RecordedAt, JsonElement Input, JsonElement Outputs, JsonElement? SourceEvent);

/// <summary>Reads challenge accounts for the API. Every query is limited to one firm.</summary>
internal sealed class ChallengeQueries(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    private const string SelectView =
        """
        select a.id, a.firm_id, a.number, a.trader_id, t.email, a.definition_id, a.reference, a.state, a.steps, a.created_at,
               ta.balance, ta.open_positions, ta.daily_floor, ta.max_loss_floor, ta.account_id is not null
        from challenge_accounts a
        join traders t on t.id = a.trader_id
        left join trading_accounts ta on ta.account_id = a.state ->> 'accountId'
        """;

    public async Task<AccountView?> GetAsync(string firmId, Guid id, CancellationToken cancellationToken) =>
        (await ReadAsync($"{SelectView} where a.firm_id = $1 and a.id = $2", [firmId, id], cancellationToken)).SingleOrDefault();

    public Task<List<AccountView>> ListByEmailAsync(string firmId, string email, CancellationToken cancellationToken) =>
        ReadAsync($"{SelectView} where a.firm_id = $1 and t.normalized_email = $2 order by a.number", [firmId, Emails.Normalize(email)], cancellationToken);

    public async Task<List<StepView>> HistoryAsync(string firmId, Guid id, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            select s.step, s.recorded_at, s.input, s.outputs, s.source_event
            from challenge_steps s join challenge_accounts a on a.id = s.challenge_account_id
            where a.firm_id = $1 and a.id = $2
            order by s.step
            """);
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var steps = new List<StepView>();
        while (await reader.ReadAsync(cancellationToken))
        {
            steps.Add(new StepView(
                reader.GetInt32(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                Json(reader.GetString(2)),
                Json(reader.GetString(3)),
                reader.IsDBNull(4) ? null : Json(reader.GetString(4))));
        }

        return steps;
    }

    /// <summary>The trader's user on the trading platform, once the first trading account has been opened.</summary>
    public async Task<Guid?> TradingUserOfAsync(Guid traderId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select trading_user_id from traders where id = $1");
        command.Parameters.AddWithValue(traderId);
        return await command.ExecuteScalarAsync(cancellationToken) as Guid?;
    }

    private async Task<List<AccountView>> ReadAsync(string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(sql);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var views = new List<AccountView>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var trading = reader.GetBoolean(14)
                ? new TradingFigures(
                    reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                    reader.GetInt32(11),
                    reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                    reader.IsDBNull(13) ? null : reader.GetDecimal(13))
                : null;
            views.Add(new AccountView(ChallengeService.ReadAccount(reader), trading));
        }

        return views;
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
