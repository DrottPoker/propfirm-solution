using System.Text.Json;
using System.Text.Json.Nodes;

using Common.Postgres;

using Microsoft.Extensions.Options;

using Npgsql;

using NpgsqlTypes;

using Prop.Api.Configuration;
using Prop.Api.Firms;
using Prop.Api.Json;
using Prop.Rules;

namespace Prop.Api.Challenges;

/// <summary>A trader's challenge account as stored, with where its challenge stands.</summary>
internal sealed record ChallengeAccount(
    Guid Id,
    string FirmId,
    long Number,
    Guid TraderId,
    string Email,
    string DefinitionId,
    string? Reference,
    ChallengeState State,
    int Steps,
    DateTimeOffset CreatedAt);

/// <summary>
/// A started account, or the one the firm already started with the same reference. Null account: the challenge
/// does not exist, or <paramref name="SandboxFull"/> when the firm's sandbox has no room for another open account.
/// </summary>
internal sealed record StartResult(ChallengeAccount? Account, bool Created, bool SandboxFull = false);

/// <summary>
/// Runs the rule engine for challenge accounts. Every change happens in one transaction with the account
/// locked: the input and the decisions are recorded as a step, the state is saved, and the work the
/// decisions need, commands for the trading platform and webhooks, is queued. Nothing is done twice and
/// nothing is lost if the service stops halfway.
/// </summary>
internal sealed class ChallengeService(
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    FirmCatalog firms,
    WorkSignals signals,
    IOptions<SandboxOptions> sandbox,
    TimeProvider time)
{
    private const string SelectAccount =
        """
        select a.id, a.firm_id, a.number, a.trader_id, t.email, a.definition_id, a.reference, a.state, a.steps, a.created_at
        from challenge_accounts a join traders t on t.id = a.trader_id
        """;

    /// <summary>The trading account id for a stage: unique across firms on the trading platform, and readable.</summary>
    public static string TradingAccountId(string firmId, long number, int stage) => $"{firmId}-{number}-{stage + 1}";

    public async Task<StartResult> StartAsync(Firm firm, string email, string definitionId, string? reference, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (reference is not null)
        {
            // Requests that repeat a reference wait for each other, so only the first starts an account.
            await ExecuteAsync(connection, "select pg_advisory_xact_lock(hashtextextended($1 || ':' || $2, 0))", [firm.Id, reference], cancellationToken);
            if (await FindAsync(connection, "a.firm_id = $1 and a.reference = $2", [firm.Id, reference], forUpdate: false, cancellationToken) is { } existing)
            {
                await transaction.CommitAsync(cancellationToken);
                return new StartResult(existing, Created: false);
            }
        }

        if (await LoadDefinitionAsync(connection, firm.Id, definitionId, cancellationToken) is not { } definition)
        {
            return new StartResult(null, Created: false);
        }

        // Starts in a firm's sandbox wait for each other, so the limit holds.
        if (firm.Status != FirmStatus.Live)
        {
            await ExecuteAsync(connection, "select pg_advisory_xact_lock(hashtextextended('sandbox:' || $1, 0))", [firm.Id], cancellationToken);
            if (await CountOpenAsync(connection, firm.Id, cancellationToken) >= sandbox.Value.MaxOpenAccounts)
            {
                return new StartResult(null, Created: false, SandboxFull: true);
            }
        }

        var now = time.GetUtcNow();
        var traderId = await EnsureTraderAsync(connection, firm.Id, email, now, cancellationToken);
        var number = await NextNumberAsync(connection, firm.Id, cancellationToken);
        var id = Guid.CreateVersion7(now);
        var started = ChallengeRules.Start(id.ToString(), definition, now);

        await ExecuteAsync(
            connection,
            """
            insert into challenge_accounts
                (id, firm_id, number, trader_id, definition_id, reference, status, stage, day_time_zone, day_start, current_day, state, steps, created_at, updated_at)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, null, $11, 0, $12, $12)
            """,
            [
                id, firm.Id, number, traderId, definitionId, (object?)reference ?? DBNull.Value, started.State.Status.ToString(), started.State.Stage,
                definition.TradingDay.TimeZone, definition.TradingDay.Start, Jsonb(started.State), now,
            ],
            cancellationToken);

        var account = new ChallengeAccount(id, firm.Id, number, traderId, email.Trim(), definitionId, reference, started.State, 0, now);
        var input = new JsonObject { [PropJson.KindProperty] = "ChallengeStarted", ["definitionId"] = definitionId, ["reference"] = reference };
        await RecordStepAsync(connection, account, 0, now, input.ToJsonString(), started.Outputs, null, cancellationToken);
        await QueueAsync(connection, firm, account, started.Outputs, now, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        Notify(firm);
        return new StartResult(account, Created: true);
    }

    private static async Task<long> CountOpenAsync(NpgsqlConnection connection, string firmId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"select count(*) from challenge_accounts where firm_id = $1 and status not in ('{ChallengeStatus.Failed}', '{ChallengeStatus.Cancelled}')",
            connection);
        command.Parameters.AddWithValue(firmId);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Applies an input in its own transaction. Null when the firm has no such account.</summary>
    public async Task<ChallengeStep?> ApplyAsync(Firm firm, Guid accountId, ChallengeInput input, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var step = await ApplyAsync(connection, firm, accountId, _ => input, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Notify(firm);
        return step;
    }

    /// <summary>
    /// Applies an input inside the caller's transaction, with the account locked until it commits. The input
    /// is made from the current state, for example to know the challenge's trading day. Null when the firm
    /// has no such account. The caller notifies the workers after committing.
    /// </summary>
    public async Task<ChallengeStep?> ApplyAsync(
        NpgsqlConnection connection,
        Firm firm,
        Guid accountId,
        Func<ChallengeState, ChallengeInput> createInput,
        string? sourceEvent,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(connection, "a.firm_id = $1 and a.id = $2", [firm.Id, accountId], forUpdate: true, cancellationToken) is not { } account)
        {
            return null;
        }

        var input = createInput(account.State);
        var step = ChallengeRules.Apply(account.State, input);

        // Repeated and late facts leave the state as it was. There is nothing to record.
        if (ReferenceEquals(step.State, account.State) && step.Outputs.Count == 0)
        {
            return step;
        }

        var now = time.GetUtcNow();
        var number = account.Steps + 1;
        await RecordStepAsync(connection, account, number, now, JsonSerializer.Serialize(input, PropJson.Options), step.Outputs, sourceEvent, cancellationToken);
        await ExecuteAsync(
            connection,
            "update challenge_accounts set status = $2, stage = $3, current_day = $4, state = $5, steps = $6, updated_at = $7 where id = $1",
            [account.Id, step.State.Status.ToString(), step.State.Stage, (object?)step.State.CurrentDay ?? DBNull.Value, Jsonb(step.State), number, now],
            cancellationToken);
        await QueueAsync(connection, firm, account with { State = step.State }, step.Outputs, now, cancellationToken);
        return step;
    }

    public void Notify(Firm firm)
    {
        signals.CommandsOf(firm.Id).Set();
        signals.Webhooks.Set();
    }

    private static async Task RecordStepAsync(
        NpgsqlConnection connection,
        ChallengeAccount account,
        int step,
        DateTimeOffset now,
        string input,
        IReadOnlyList<ChallengeOutput> outputs,
        string? sourceEvent,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            connection,
            "insert into challenge_steps (challenge_account_id, step, recorded_at, input, outputs, source_event) values ($1, $2, $3, $4, $5, $6)",
            [
                account.Id, step, now, Jsonb(input),
                Jsonb(JsonSerializer.Serialize<IReadOnlyList<ChallengeOutput>>(outputs, PropJson.Options)),
                sourceEvent is null ? DBNull.Value : Jsonb(sourceEvent),
            ],
            cancellationToken);

    /// <summary>Queues what the decisions need: commands for the trading platform and webhooks for the firm.</summary>
    private async Task QueueAsync(
        NpgsqlConnection connection,
        Firm firm,
        ChallengeAccount account,
        IReadOnlyList<ChallengeOutput> outputs,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // The firm as it is now, so a webhook the firm set a moment ago gets the decision.
        firm = firms.ById(firm.Id) ?? firm;
        foreach (var output in outputs)
        {
            switch (output)
            {
                case OpenAccountRequested open:
                    var tradingAccountId = TradingAccountId(firm.Id, account.Number, open.Stage);
                    await ExecuteAsync(
                        connection,
                        "insert into trading_accounts (account_id, challenge_account_id, stage) values ($1, $2, $3) on conflict (account_id) do nothing",
                        [tradingAccountId, account.Id, open.Stage],
                        cancellationToken);
                    await QueueCommandAsync(connection, firm, account, new OpenTradingAccount(tradingAccountId, open.InitialBalance, account.TraderId), now, cancellationToken);
                    break;
                case FloorRequested floor:
                    await QueueCommandAsync(connection, firm, account, new SetTradingFloor(floor.AccountId, floor.FloorId, floor.Floor), now, cancellationToken);
                    break;
                case CloseAccountRequested close:
                    await QueueCommandAsync(connection, firm, account, new CloseTradingAccount(close.AccountId), now, cancellationToken);
                    break;
                case StageStarted:
                    await QueueWebhookAsync(connection, firm, account, "account.stage_started", output, now, cancellationToken);
                    break;
                case StagePassed:
                    await QueueWebhookAsync(connection, firm, account, "account.passed", output, now, cancellationToken);
                    break;
                case FundingAwaited:
                    await QueueWebhookAsync(connection, firm, account, "account.funding_awaited", output, now, cancellationToken);
                    break;
                case ChallengeFailed:
                    await QueueWebhookAsync(connection, firm, account, "account.breached", output, now, cancellationToken);
                    break;
                case ChallengeCancelled:
                    await QueueWebhookAsync(connection, firm, account, "account.cancelled", output, now, cancellationToken);
                    break;
                case PayoutRequested requested:
                    await InsertPayoutAsync(connection, firm, account, requested.Payout, cancellationToken);
                    break;
                case WithdrawalRequested withdrawal:
                    await QueueCommandAsync(
                        connection,
                        firm,
                        account,
                        new WithdrawFromTradingAccount(withdrawal.AccountId, withdrawal.OperationId, withdrawal.Amount, withdrawal.MinBalance),
                        now,
                        cancellationToken);
                    break;
                case PayoutWithdrawn withdrawn:
                    await UpdatePayoutAsync(connection, withdrawn.Payout, "withdrawn_at", withdrawn.Time, cancellationToken);
                    await QueueWebhookAsync(connection, firm, account, "payout.requested", output, now, cancellationToken);
                    break;
                case PayoutApproved approved:
                    await UpdatePayoutAsync(connection, approved.Payout, "approved_at", approved.Time, cancellationToken);
                    await QueueWebhookAsync(connection, firm, account, "payout.approved", output, now, cancellationToken);
                    break;
                case PayoutPaid paid:
                    await UpdatePayoutAsync(connection, paid.Payout, "paid_at", paid.Time, cancellationToken, reference: paid.Reference);
                    await QueueWebhookAsync(connection, firm, account, "payout.paid", output, now, cancellationToken);
                    break;
                case PayoutRejected rejected:
                    await UpdatePayoutAsync(connection, rejected.Payout, "rejected_at", rejected.Time, cancellationToken, reason: rejected.Reason);
                    await QueueWebhookAsync(connection, firm, account, "payout.rejected", output, now, cancellationToken);
                    break;
                case PayoutFailed failed:
                    // Nothing happened on the account, so the firm has nothing to act on.
                    await UpdatePayoutAsync(connection, failed.Payout, "failed_at", failed.Time, cancellationToken, reason: failed.Reason);
                    break;
                default:
                    // Progress and ignored inputs are kept in the steps only.
                    break;
            }
        }
    }

    private static Task InsertPayoutAsync(NpgsqlConnection connection, Firm firm, ChallengeAccount account, Payout payout, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            """
            insert into payouts
                (id, firm_id, challenge_account_id, trading_account_id, status, profit, profit_split_percent, amount, currency, requested_at)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            """,
            [
                Guid.Parse(payout.Id), firm.Id, account.Id, payout.AccountId, payout.Status.ToString(), payout.Profit, payout.ProfitSplitPercent, payout.Amount,
                account.State.Definition.Currency, payout.RequestedAt,
            ],
            cancellationToken);

    /// <summary>The payout's new status, when it changed, and the firm's reference or the reason when there is one.</summary>
    private static Task UpdatePayoutAsync(
        NpgsqlConnection connection,
        Payout payout,
        string timeColumn,
        DateTimeOffset time,
        CancellationToken cancellationToken,
        string? reference = null,
        string? reason = null) =>
        ExecuteAsync(
            connection,
            $"update payouts set status = $2, {timeColumn} = $3, reference = coalesce($4, reference), reason = coalesce($5, reason) where id = $1",
            [Guid.Parse(payout.Id), payout.Status.ToString(), time, Text(reference), Text(reason)],
            cancellationToken);

    private static NpgsqlParameter Text(string? value) => new() { Value = (object?)value ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text };

    private static Task QueueCommandAsync(
        NpgsqlConnection connection,
        Firm firm,
        ChallengeAccount account,
        TradingCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            "insert into trading_commands (firm_id, challenge_account_id, command, created_at) values ($1, $2, $3, $4)",
            [firm.Id, account.Id, Jsonb(JsonSerializer.Serialize(command, PropJson.Options)), now],
            cancellationToken);

    private static async Task QueueWebhookAsync(
        NpgsqlConnection connection,
        Firm firm,
        ChallengeAccount account,
        string eventType,
        ChallengeOutput output,
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
            ["account"] = new JsonObject
            {
                ["id"] = account.Id,
                ["number"] = account.Number,
                ["email"] = account.Email,
                ["challengeId"] = account.DefinitionId,
                ["reference"] = account.Reference,
            },
            ["data"] = JsonSerializer.SerializeToNode(output, PropJson.Options),
        };
        await ExecuteAsync(
            connection,
            "insert into webhook_deliveries (id, firm_id, event_type, payload, created_at, next_attempt_at) values ($1, $2, $3, $4, $5, $5)",
            [id, firm.Id, eventType, Jsonb(payload.ToJsonString()), now],
            cancellationToken);
    }

    private static async Task<ChallengeAccount?> FindAsync(
        NpgsqlConnection connection,
        string where,
        object[] parameters,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"{SelectAccount} where {where}{(forUpdate ? " for update of a" : "")}", connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAccount(reader) : null;
    }

    internal static ChallengeAccount ReadAccount(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            JsonSerializer.Deserialize<ChallengeState>(reader.GetString(7), PropJson.Options)!,
            reader.GetInt32(8),
            reader.GetFieldValue<DateTimeOffset>(9));

    private static async Task<ChallengeDefinition?> LoadDefinitionAsync(NpgsqlConnection connection, string firmId, string definitionId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select definition from challenge_definitions where firm_id = $1 and id = $2", connection);
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(definitionId);
        return await command.ExecuteScalarAsync(cancellationToken) is string json
            ? JsonSerializer.Deserialize<ChallengeDefinition>(json, PropJson.Options)
            : null;
    }

    private static async Task<Guid> EnsureTraderAsync(NpgsqlConnection connection, string firmId, string email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            with inserted as (
                insert into traders (id, firm_id, email, normalized_email, created_at) values ($1, $2, $3, $4, $5)
                on conflict (firm_id, normalized_email) do nothing
                returning id)
            select id from inserted
            union all
            select id from traders where firm_id = $2 and normalized_email = $4
            limit 1
            """,
            connection);
        command.Parameters.AddWithValue(Guid.CreateVersion7(now));
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(email.Trim());
        command.Parameters.AddWithValue(Emails.Normalize(email));
        command.Parameters.AddWithValue(now);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    // Account numbers start at 1001 for every firm.
    private static async Task<long> NextNumberAsync(NpgsqlConnection connection, string firmId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into firm_counters (firm_id, next_number) values ($1, 1002)
            on conflict (firm_id) do update set next_number = firm_counters.next_number + 1
            returning next_number - 1
            """,
            connection);
        command.Parameters.AddWithValue(firmId);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter as NpgsqlParameter ?? new NpgsqlParameter { Value = parameter });
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlParameter Jsonb(ChallengeState state) => Jsonb(JsonSerializer.Serialize(state, PropJson.Options));

    private static NpgsqlParameter Jsonb(string json) => new() { Value = json, NpgsqlDbType = NpgsqlDbType.Jsonb };
}

internal static class Emails
{
    /// <summary>Email addresses are compared without case and surrounding spaces.</summary>
    public static string Normalize(string email) => email.Trim().ToUpperInvariant();
}
