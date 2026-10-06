using System.Runtime.CompilerServices;

using Common.Postgres;

using Npgsql;

using NpgsqlTypes;

using Trading.Service.Candles;

namespace Trading.Service.Persistence;

/// <summary>The charts' bars in Postgres. A feed's history is written in one transaction with COPY.</summary>
internal sealed class PostgresChartStore(NpgsqlDataSource dataSource, DatabaseSchema schema) : IChartStore
{
    public async Task<DateTimeOffset?> GetHistoryReachAsync(string feed, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select reach from chart_histories where feed = $1");
        command.Parameters.AddWithValue(feed);
        return await command.ExecuteScalarAsync(cancellationToken) is DateTime reach ? new DateTimeOffset(reach, TimeSpan.Zero) : null;
    }

    public async Task ForgetHistoryAsync(string feed, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("delete from chart_histories where feed = $1");
        command.Parameters.AddWithValue(feed);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReplaceHistoryAsync(string feed, DateTimeOffset reach, DateTimeOffset until, IReadOnlyList<ChartBar> bars, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var delete = new NpgsqlCommand("delete from chart_bars where feed = $1 and time < $2", connection))
        {
            delete.Parameters.AddWithValue(feed);
            delete.Parameters.AddWithValue(until);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var importer = await connection.BeginBinaryImportAsync(
            "copy chart_bars (feed, time, symbol, resolution, open, high, low, close, ticks) from stdin (format binary)",
            cancellationToken))
        {
            foreach (var (symbol, resolution, candle) in bars)
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(feed, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(candle.Time, NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(symbol, NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(resolution.ToString(), NpgsqlDbType.Text, cancellationToken);
                await importer.WriteAsync(candle.Open, NpgsqlDbType.Numeric, cancellationToken);
                await importer.WriteAsync(candle.High, NpgsqlDbType.Numeric, cancellationToken);
                await importer.WriteAsync(candle.Low, NpgsqlDbType.Numeric, cancellationToken);
                await importer.WriteAsync(candle.Close, NpgsqlDbType.Numeric, cancellationToken);
                await importer.WriteAsync(candle.TickCount, NpgsqlDbType.Integer, cancellationToken);
            }

            await importer.CompleteAsync(cancellationToken);
        }

        await using (var mark = new NpgsqlCommand(
            "insert into chart_histories (feed, loaded_at, reach) values ($1, $2, $3) on conflict (feed) do update set loaded_at = excluded.loaded_at, reach = excluded.reach",
            connection))
        {
            mark.Parameters.AddWithValue(feed);
            mark.Parameters.AddWithValue(until);
            mark.Parameters.AddWithValue(reach);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveAsync(string feed, IReadOnlyList<ChartBar> bars, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            insert into chart_bars (feed, time, symbol, resolution, open, high, low, close, ticks)
            select $1, * from unnest($2::timestamptz[], $3::text[], $4::text[], $5::numeric[], $6::numeric[], $7::numeric[], $8::numeric[], $9::integer[])
            on conflict (feed, time, symbol, resolution) do update
            set open = excluded.open, high = excluded.high, low = excluded.low, close = excluded.close, ticks = excluded.ticks
            """);
        command.Parameters.AddWithValue(feed);
        command.Parameters.AddWithValue(bars.Select(b => b.Candle.Time).ToArray());
        command.Parameters.AddWithValue(bars.Select(b => b.Symbol).ToArray());
        command.Parameters.AddWithValue(bars.Select(b => b.Resolution.ToString()).ToArray());
        command.Parameters.AddWithValue(bars.Select(b => b.Candle.Open).ToArray());
        command.Parameters.AddWithValue(bars.Select(b => b.Candle.High).ToArray());
        command.Parameters.AddWithValue(bars.Select(b => b.Candle.Low).ToArray());
        command.Parameters.AddWithValue(bars.Select(b => b.Candle.Close).ToArray());
        command.Parameters.AddWithValue(bars.Select(b => b.Candle.TickCount).ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async IAsyncEnumerable<ChartBar> ReadAsync(string feed, DateTimeOffset since, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            "select time, symbol, resolution, open, high, low, close, ticks from chart_bars where feed = $1 and time >= $2 order by time, symbol, resolution");
        command.Parameters.AddWithValue(feed);
        command.Parameters.AddWithValue(since);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return new ChartBar(
                reader.GetString(1),
                Enum.Parse<Timeframe>(reader.GetString(2)),
                new Candle(reader.GetFieldValue<DateTimeOffset>(0), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetInt32(7)));
        }
    }

    public async Task PruneAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("delete from chart_bars where time < $1");
        command.Parameters.AddWithValue(before);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
