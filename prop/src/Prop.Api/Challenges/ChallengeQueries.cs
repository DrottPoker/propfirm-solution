using System.Text.Json;

using Common.Postgres;

using Npgsql;

using Prop.Api.Api;
using Prop.Api.Json;
using Prop.Rules;

namespace Prop.Api.Challenges;

/// <summary>
/// A challenge account with what the trading platform last reported for its current trading account, and the rule
/// engine's decision that ended it, if it has ended.
/// </summary>
internal sealed record AccountView(ChallengeAccount Account, TradingFigures? Trading, ChallengeOutput? Ending);

internal sealed record TradingFigures(decimal? Balance, int OpenPositions, decimal? DailyFloor, decimal? MaxLossFloor);

/// <summary>Which accounts the admin panel lists. Evaluation and funded accounts are those trading or opening their trading account.</summary>
public enum AccountGroup
{
    All,
    Evaluation,

    /// <summary>Passed every evaluation stage, and waits for the firm to approve the funded account.</summary>
    AwaitingFunding,

    Funded,

    /// <summary>Failed or cancelled.</summary>
    Ended,
}

/// <summary>
/// What the admin panel looks for among the firm's accounts: <paramref name="Text"/> is part of the trader's email, part
/// of the firm's reference or the account number, with or without #.
/// </summary>
internal sealed record AccountSearch(string? Text, string? ChallengeId, AccountGroup Group);

/// <summary>How many accounts are in each group.</summary>
internal sealed record AccountCounts(int All, int Evaluation, int AwaitingFunding, int Funded, int Ended);

/// <summary>One recorded step: the input and what the rule engine decided, with the trading platform's event behind it.</summary>
internal sealed record StepView(int Step, DateTimeOffset RecordedAt, JsonElement Input, JsonElement Outputs, JsonElement? SourceEvent);

/// <summary>Reads challenge accounts for the API. Every query is limited to one firm.</summary>
internal sealed class ChallengeQueries(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    private const string SelectView =
        """
        select a.id, a.firm_id, a.number, a.trader_id, t.email, a.definition_id, a.reference, a.state, a.steps, a.created_at,
               ta.balance, ta.open_positions, ta.daily_floor, ta.max_loss_floor, ta.account_id is not null, a.ending
        from challenge_accounts a
        join traders t on t.id = a.trader_id
        left join trading_accounts ta on ta.account_id = a.state ->> 'accountId'
        """;

    /// <summary>The index of the account's funded stage, from the definition it was bought with, in a query on challenge_accounts a.</summary>
    internal const string FundedStageSql = "jsonb_array_length(a.state -> 'definition' -> 'evaluation')";

    // Part of an email address, part of the firm's reference or the account number, and the challenge, in $2 to $4.
    private const string SearchCondition =
        """
        ($2::text is null or strpos(t.normalized_email, $2) > 0 or strpos(upper(coalesce(a.reference, '')), $2) > 0 or a.number = $3::bigint)
        and ($4::text is null or a.definition_id = $4)
        """;

    /// <summary>Which accounts are in the group, in a query on challenge_accounts a.</summary>
    internal static string GroupCondition(AccountGroup group) => group switch
    {
        AccountGroup.Evaluation => $"(a.status in ('OpeningAccount', 'Active') and a.stage < {FundedStageSql})",
        AccountGroup.AwaitingFunding => "a.status = 'AwaitingFunding'",
        AccountGroup.Funded => $"(a.status in ('OpeningAccount', 'Active') and a.stage >= {FundedStageSql})",
        AccountGroup.Ended => "a.status in ('Failed', 'Cancelled')",
        _ => "true",
    };

    public async Task<AccountView?> GetAsync(string firmId, Guid id, CancellationToken cancellationToken) =>
        (await ReadAsync($"{SelectView} where a.firm_id = $1 and a.id = $2", [firmId, id], cancellationToken)).SingleOrDefault();

    /// <summary>
    /// A page of the firm's accounts found by the search, the newest first. <paramref name="before"/> is the account
    /// number the page starts below, from the previous page.
    /// </summary>
    public Task<List<AccountView>> SearchAsync(string firmId, AccountSearch search, long? before, int limit, CancellationToken cancellationToken) =>
        ReadAsync(
            $"{SelectView} where a.firm_id = $1 and {SearchCondition} and {GroupCondition(search.Group)} and ($5::bigint is null or a.number < $5) order by a.number desc limit $6",
            [.. SearchValues(firmId, search), before is { } number ? number : DBNull.Value, limit],
            cancellationToken);

    /// <summary>How many of the firm's accounts the search finds in each group, whichever group it asks for.</summary>
    public async Task<AccountCounts> CountAsync(string firmId, AccountSearch search, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            $"""
            select count(*),
                   count(*) filter (where {GroupCondition(AccountGroup.Evaluation)}),
                   count(*) filter (where {GroupCondition(AccountGroup.AwaitingFunding)}),
                   count(*) filter (where {GroupCondition(AccountGroup.Funded)}),
                   count(*) filter (where {GroupCondition(AccountGroup.Ended)})
            from challenge_accounts a join traders t on t.id = a.trader_id
            where a.firm_id = $1 and {SearchCondition}
            """);
        foreach (var value in SearchValues(firmId, search))
        {
            command.Parameters.AddWithValue(value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new AccountCounts(
            (int)reader.GetInt64(0),
            (int)reader.GetInt64(1),
            (int)reader.GetInt64(2),
            (int)reader.GetInt64(3),
            (int)reader.GetInt64(4));
    }

    public Task<List<AccountView>> ListByTraderAsync(string firmId, Guid traderId, CancellationToken cancellationToken) =>
        ReadAsync($"{SelectView} where a.firm_id = $1 and a.trader_id = $2 order by a.number", [firmId, traderId], cancellationToken);

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
            var ending = reader.IsDBNull(15) ? null : JsonSerializer.Deserialize<ChallengeOutput>(reader.GetString(15), PropJson.Options);
            views.Add(new AccountView(ChallengeService.ReadAccount(reader), trading, ending));
        }

        return views;
    }

    private static object[] SearchValues(string firmId, AccountSearch search)
    {
        var text = string.IsNullOrWhiteSpace(search.Text) ? null : Emails.Normalize(search.Text);
        var number = text is not null && long.TryParse(text.TrimStart('#'), out var parsed) ? parsed : (long?)null;
        return [firmId, (object?)text ?? DBNull.Value, number is { } n ? n : DBNull.Value, string.IsNullOrWhiteSpace(search.ChallengeId) ? DBNull.Value : search.ChallengeId];
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
