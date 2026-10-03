using System.Runtime.CompilerServices;
using System.Text.Json;

using Common.Postgres;

using Microsoft.Extensions.Options;

using Npgsql;

using NpgsqlTypes;

using Trading.Engine;
using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Service.Engine;
using Trading.Service.Json;

namespace Trading.Service.Persistence;

/// <summary>The journal in Postgres. Each batch is written in one transaction with COPY.</summary>
internal sealed class PostgresEngineJournal(NpgsqlDataSource dataSource, DatabaseSchema schema, IOptions<JournalOptions> options)
    : IEngineJournal
{
    private const string QuoteKind = nameof(Quote);

    private static readonly JsonSerializerOptions Json = EngineJson.CreateOptions();

    public Task InitializeAsync(CancellationToken cancellationToken) => schema.EnsureAsync(cancellationToken);

    public async Task<JournalSnapshot?> LoadLatestSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "select input_sequence, event_sequence, configuration_fingerprint, state from engine_snapshots order by input_sequence desc limit 1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new JournalSnapshot(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), Deserialize<EngineState>(reader.GetString(3)));
    }

    public async IAsyncEnumerable<JournaledInput> ReadInputsAsync(long afterSequence, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "select sequence, recorded_at, kind, symbol, bid, ask, payload from engine_inputs where sequence > $1 order by sequence");
        command.Parameters.AddWithValue(afterSequence);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var input = reader.GetString(2) == QuoteKind
                ? new Quote(reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(3), reader.GetDecimal(4), reader.GetDecimal(5))
                : Deserialize<EngineInput>(reader.GetString(6));
            yield return new JournaledInput(reader.GetInt64(0), input);
        }
    }

    public Task<long> GetLastInputSequenceAsync(CancellationToken cancellationToken) =>
        ScalarAsync("select coalesce(max(sequence), 0) from engine_inputs", cancellationToken);

    public Task<long> GetLastEventSequenceAsync(CancellationToken cancellationToken) =>
        ScalarAsync("select coalesce(max(sequence), 0) from engine_events", cancellationToken);

    public async Task AppendAsync(JournalBatch batch, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (batch.Inputs.Count > 0)
        {
            await using var importer = await connection.BeginBinaryImportAsync(
                "copy engine_inputs (sequence, recorded_at, kind, symbol, bid, ask, payload) from stdin (format binary)",
                cancellationToken);
            foreach (var (sequence, input) in batch.Inputs)
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(sequence, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(input.Timestamp, NpgsqlDbType.TimestampTz, cancellationToken);
                if (input is Quote quote)
                {
                    await importer.WriteAsync(QuoteKind, NpgsqlDbType.Text, cancellationToken);
                    await importer.WriteAsync(quote.Symbol, NpgsqlDbType.Text, cancellationToken);
                    await importer.WriteAsync(quote.Bid, NpgsqlDbType.Numeric, cancellationToken);
                    await importer.WriteAsync(quote.Ask, NpgsqlDbType.Numeric, cancellationToken);
                    await importer.WriteNullAsync(cancellationToken);
                }
                else
                {
                    await importer.WriteAsync(input.GetType().Name, NpgsqlDbType.Text, cancellationToken);
                    await importer.WriteNullAsync(cancellationToken);
                    await importer.WriteNullAsync(cancellationToken);
                    await importer.WriteNullAsync(cancellationToken);
                    await importer.WriteAsync(JsonSerializer.Serialize(input, Json), NpgsqlDbType.Jsonb, cancellationToken);
                }
            }

            await importer.CompleteAsync(cancellationToken);
        }

        if (batch.Events.Count > 0)
        {
            await using var importer = await connection.BeginBinaryImportAsync(
                "copy engine_events (sequence, account_id, group_id, kind, occurred_at, payload) from stdin (format binary)",
                cancellationToken);
            foreach (var ((sequence, engineEvent), groupId) in batch.Events)
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(sequence, NpgsqlDbType.Bigint, cancellationToken);
                await WriteNullableTextAsync(importer, EventLog.AccountIdOf(engineEvent), cancellationToken);
                await WriteNullableTextAsync(importer, groupId, cancellationToken);

                await importer.WriteAsync(engineEvent.GetType().Name, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(engineEvent.Timestamp, NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(JsonSerializer.Serialize(engineEvent, Json), NpgsqlDbType.Jsonb, cancellationToken);
            }

            await importer.CompleteAsync(cancellationToken);
        }

        if (batch.Snapshot is { } snapshot)
        {
            await using var insert = new NpgsqlCommand(
                """
                insert into engine_snapshots (input_sequence, event_sequence, configuration_fingerprint, state)
                values ($1, $2, $3, $4)
                on conflict (input_sequence) do update
                set event_sequence = excluded.event_sequence,
                    configuration_fingerprint = excluded.configuration_fingerprint,
                    state = excluded.state,
                    created_at = now()
                """,
                connection);
            insert.Parameters.AddWithValue(snapshot.InputSequence);
            insert.Parameters.AddWithValue(snapshot.EventSequence);
            insert.Parameters.AddWithValue(snapshot.ConfigurationFingerprint);
            insert.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.Serialize(snapshot.State, Json), NpgsqlDbType = NpgsqlDbType.Jsonb });
            await insert.ExecuteNonQueryAsync(cancellationToken);

            await using var prune = new NpgsqlCommand(
                "delete from engine_snapshots where input_sequence not in (select input_sequence from engine_snapshots order by input_sequence desc limit $1)",
                connection);
            prune.Parameters.AddWithValue(Math.Max(1, options.Value.SnapshotsToKeep));
            await prune.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EventEnvelope>> ReadEventsAsync(string accountId, long afterSequence, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "select sequence, payload from engine_events where account_id = $1 and sequence > $2 order by sequence limit $3");
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(afterSequence);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var events = new List<EventEnvelope>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new EventEnvelope(reader.GetInt64(0), Deserialize<EngineEvent>(reader.GetString(1))));
        }

        return events;
    }

    public async Task<IReadOnlyList<EventEnvelope>> ReadGroupEventsAsync(IReadOnlyCollection<string> groupIds, long afterSequence, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "select sequence, payload from engine_events where group_id = any($1) and sequence > $2 order by sequence limit $3");
        command.Parameters.AddWithValue(groupIds.ToArray());
        command.Parameters.AddWithValue(afterSequence);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var events = new List<EventEnvelope>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new EventEnvelope(reader.GetInt64(0), Deserialize<EngineEvent>(reader.GetString(1))));
        }

        return events;
    }

    public async IAsyncEnumerable<Quote> ReadQuotesAsync(DateTimeOffset since, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "select recorded_at, symbol, bid, ask from engine_inputs where kind = 'Quote' and recorded_at >= $1 order by sequence");
        command.Parameters.AddWithValue(since);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return new Quote(reader.GetFieldValue<DateTimeOffset>(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3));
        }
    }

    private async Task<long> ScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task WriteNullableTextAsync(NpgsqlBinaryImporter importer, string? value, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            await importer.WriteNullAsync(cancellationToken);
        }
        else
        {
            await importer.WriteAsync(value, NpgsqlDbType.Text, cancellationToken);
        }
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidOperationException($"The journal has an empty {typeof(T).Name}.");
}
