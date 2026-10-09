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
            "select sequence, recorded_at, kind, symbol, bid, ask, payload, feed from engine_inputs where sequence > $1 order by sequence");
        command.Parameters.AddWithValue(afterSequence);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var input = reader.GetString(2) == QuoteKind
                ? new Quote(reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(3), reader.GetDecimal(4), reader.GetDecimal(5))
                : Deserialize<EngineInput>(reader.GetString(6));
            yield return new JournaledInput(reader.GetInt64(0), input) { Feed = reader.IsDBNull(7) ? null : reader.GetString(7) };
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
                "copy engine_inputs (sequence, recorded_at, kind, symbol, bid, ask, payload, feed) from stdin (format binary)",
                cancellationToken);
            foreach (var journaled in batch.Inputs)
            {
                var input = journaled.Input;
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(journaled.Sequence, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(input.Timestamp, NpgsqlDbType.TimestampTz, cancellationToken);
                if (input is Quote quote)
                {
                    await importer.WriteAsync(QuoteKind, NpgsqlDbType.Text, cancellationToken);
                    await importer.WriteAsync(quote.Symbol, NpgsqlDbType.Text, cancellationToken);
                    await importer.WriteAsync(quote.Bid, NpgsqlDbType.Numeric, cancellationToken);
                    await importer.WriteAsync(quote.Ask, NpgsqlDbType.Numeric, cancellationToken);
                    await importer.WriteNullAsync(cancellationToken);
                    await WriteNullableTextAsync(importer, journaled.Feed, cancellationToken);
                }
                else
                {
                    await importer.WriteAsync(input.GetType().Name, NpgsqlDbType.Text, cancellationToken);
                    await importer.WriteNullAsync(cancellationToken);
                    await importer.WriteNullAsync(cancellationToken);
                    await importer.WriteNullAsync(cancellationToken);
                    await importer.WriteAsync(JsonSerializer.Serialize(input, Json), NpgsqlDbType.Jsonb, cancellationToken);
                    await importer.WriteNullAsync(cancellationToken);
                }
            }

            await importer.CompleteAsync(cancellationToken);
        }

        if (batch.Events.Count > 0)
        {
            await using var importer = await connection.BeginBinaryImportAsync(
                "copy engine_events (sequence, account_id, group_id, kind, occurred_at, payload, input_sequence) from stdin (format binary)",
                cancellationToken);
            foreach (var journaled in batch.Events)
            {
                var ((sequence, engineEvent), groupId) = journaled;
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(sequence, NpgsqlDbType.Bigint, cancellationToken);
                await WriteNullableTextAsync(importer, EventLog.AccountIdOf(engineEvent), cancellationToken);
                await WriteNullableTextAsync(importer, groupId, cancellationToken);

                await importer.WriteAsync(engineEvent.GetType().Name, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(engineEvent.Timestamp, NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(JsonSerializer.Serialize(engineEvent, Json), NpgsqlDbType.Jsonb, cancellationToken);
                if (journaled.InputSequence is { } inputSequence)
                {
                    await importer.WriteAsync(inputSequence, NpgsqlDbType.Bigint, cancellationToken);
                }
                else
                {
                    await importer.WriteNullAsync(cancellationToken);
                }
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

    public Task<IReadOnlyList<EventEnvelope>> ReadEventsAsync(string accountId, long afterSequence, int limit, CancellationToken cancellationToken) =>
        ReadEnvelopesAsync(
            "select sequence, payload from engine_events where account_id = $1 and sequence > $2 order by sequence limit $3",
            accountId,
            afterSequence,
            limit,
            cancellationToken);

    // Walks the account index backwards from the sequence number, then returns the page oldest first.
    public Task<IReadOnlyList<EventEnvelope>> ReadEventsBeforeAsync(string accountId, long beforeSequence, int limit, CancellationToken cancellationToken) =>
        ReadEnvelopesAsync(
            """
            select sequence, payload from (
                select sequence, payload from engine_events where account_id = $1 and sequence < $2 order by sequence desc limit $3
            ) as latest
            order by sequence
            """,
            accountId,
            beforeSequence,
            limit,
            cancellationToken);

    public async Task<IReadOnlyList<EventEnvelope>> ReadAllEventsAsync(long afterSequence, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("select sequence, payload from engine_events where sequence > $1 order by sequence limit $2");
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

    public Task<IReadOnlyList<EventEnvelope>> ReadGroupEventsAsync(IReadOnlyCollection<string> groupIds, long afterSequence, int limit, CancellationToken cancellationToken) =>
        ReadEnvelopesAsync(
            "select sequence, payload from engine_events where group_id = any($1) and sequence > $2 order by sequence limit $3",
            groupIds.ToArray(),
            afterSequence,
            limit,
            cancellationToken);

    public async Task<IReadOnlyList<EventEnvelope>> ReadLatestGroupEventsAsync(
        IReadOnlyCollection<string> groupIds,
        string? accountId,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "select sequence, payload from engine_events where group_id = any($1) and ($2::text is null or account_id = $2) order by sequence desc limit $3");
        command.Parameters.AddWithValue(groupIds.ToArray());
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)accountId ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text });
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<EventEnvelope>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new EventEnvelope(reader.GetInt64(0), Deserialize<EngineEvent>(reader.GetString(1))));
        }

        return events;
    }

    public async Task<IReadOnlyDictionary<string, GroupActivity>> CountGroupActivityAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            select group_id,
                   (count(*) filter (where kind = 'PositionOpened'))::int,
                   (count(*) filter (where kind = 'InputRejected'))::int,
                   (count(*) filter (where kind = 'InputRejected' and payload ->> 'reason' = 'StalePrice'))::int
            from engine_events
            where group_id is not null and occurred_at >= $1 and kind in ('PositionOpened', 'InputRejected')
            group by group_id
            """);
        command.Parameters.AddWithValue(since);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var activity = new Dictionary<string, GroupActivity>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            activity[reader.GetString(0)] = new GroupActivity(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
        }

        return activity;
    }

    public async Task<JournalStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        await using var size = dataSource.CreateCommand(
            "select pg_total_relation_size('engine_inputs') + pg_total_relation_size('engine_events') + pg_total_relation_size('engine_snapshots')");
        var bytes = (long)(await size.ExecuteScalarAsync(cancellationToken))!;

        await using var command = dataSource.CreateCommand("select input_sequence, created_at from engine_snapshots order by input_sequence desc");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var snapshots = new List<SnapshotInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            snapshots.Add(new SnapshotInfo(reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1)));
        }

        return new JournalStats(bytes, snapshots);
    }

    public async Task<string?> GetLastQuoteFeedAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("select feed from engine_inputs where kind = 'Quote' order by sequence desc limit 1");
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async IAsyncEnumerable<Quote> ReadQuotesAsync(DateTimeOffset since, string feed, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "select recorded_at, symbol, bid, ask from engine_inputs where kind = 'Quote' and recorded_at >= $1 and feed = $2 order by sequence");
        command.Parameters.AddWithValue(since);
        command.Parameters.AddWithValue(feed);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return new Quote(reader.GetFieldValue<DateTimeOffset>(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3));
        }
    }

    public async Task<IReadOnlyList<RecordedEvent>> ReadPositionEventsAsync(string accountId, string positionId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            select sequence, payload, input_sequence from engine_events
            where account_id = $1 and coalesce(payload ->> 'positionId', payload ->> 'orderId') = $2
            order by sequence
            """);
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(positionId);
        return await ReadRecordedEventsAsync(command, cancellationToken);
    }

    public async Task<RecordedQuote?> FindQuoteAsync(string symbol, DateTimeOffset atOrBefore, long? notAfterInput, CancellationToken cancellationToken)
    {
        // Inputs with the same time are told apart by their sequence number.
        await using var command = dataSource.CreateCommand(
            """
            select sequence, recorded_at, symbol, bid, ask, feed from engine_inputs
            where kind = 'Quote' and symbol = $1 and recorded_at <= $2 and ($3::bigint is null or sequence <= $3)
            order by recorded_at desc, sequence desc
            limit 1
            """);
        command.Parameters.AddWithValue(symbol);
        command.Parameters.AddWithValue(atOrBefore);
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)notAfterInput ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Bigint });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadQuote(reader) : null;
    }

    public async IAsyncEnumerable<RecordedQuote> ReadQuotesBetweenAsync(
        IReadOnlyCollection<string> symbols,
        DateTimeOffset first,
        DateTimeOffset last,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            select sequence, recorded_at, symbol, bid, ask, feed from engine_inputs
            where kind = 'Quote' and symbol = any($1) and recorded_at between $2 and $3
            order by sequence
            """);
        command.Parameters.AddWithValue(symbols.ToArray());
        command.Parameters.AddWithValue(first);
        command.Parameters.AddWithValue(last);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return ReadQuote(reader);
        }
    }

    public async Task<IReadOnlyList<RecordedEvent>> ReadGroupEventsSinceAsync(
        IReadOnlyCollection<string> groupIds,
        DateTimeOffset after,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            select sequence, payload, input_sequence from engine_events
            where group_id = any($1) and occurred_at > $2 and sequence > $3
            order by sequence
            limit $4
            """);
        command.Parameters.AddWithValue(groupIds.ToArray());
        command.Parameters.AddWithValue(after);
        command.Parameters.AddWithValue(afterSequence);
        command.Parameters.AddWithValue(limit);
        return await ReadRecordedEventsAsync(command, cancellationToken);
    }

    private static RecordedQuote ReadQuote(NpgsqlDataReader reader) =>
        new(
            reader.GetInt64(0),
            new Quote(reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2), reader.GetDecimal(3), reader.GetDecimal(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5));

    // The SQL selects the sequence number, the payload and the input sequence number, in that order.
    private static async Task<IReadOnlyList<RecordedEvent>> ReadRecordedEventsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<RecordedEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new RecordedEvent(new EventEnvelope(reader.GetInt64(0), Deserialize<EngineEvent>(reader.GetString(1))), reader.IsDBNull(2) ? null : reader.GetInt64(2)));
        }

        return events;
    }

    // The SQL takes the account or groups, a sequence number and a limit, in that order.
    private async Task<IReadOnlyList<EventEnvelope>> ReadEnvelopesAsync(string sql, object filter, long sequence, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(filter);
        command.Parameters.AddWithValue(sequence);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var events = new List<EventEnvelope>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new EventEnvelope(reader.GetInt64(0), Deserialize<EngineEvent>(reader.GetString(1))));
        }

        return events;
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
