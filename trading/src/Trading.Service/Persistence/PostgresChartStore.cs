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

    public async Task<ChartHistoryInfo?> GetHistoryInfoAsync(string feed, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select loaded_at, reach from chart_histories where feed = $1");
        command.Parameters.AddWithValue(feed);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ChartHistoryInfo(reader.GetFieldValue<DateTimeOffset>(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1))
            : null;
    }

    public async Task SaveGapAsync(ChartGap gap, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            insert into chart_gaps (id, feed, from_time, until_time, found_at, state, tries, finished_at, bars, problem)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            on conflict (id) do update set state = excluded.state, tries = excluded.tries, finished_at = excluded.finished_at,
                bars = excluded.bars, problem = excluded.problem
            """);
        command.Parameters.AddWithValue(gap.Id);
        command.Parameters.AddWithValue(gap.Feed);
        command.Parameters.AddWithValue(gap.From);
        command.Parameters.AddWithValue(gap.Until);
        command.Parameters.AddWithValue(gap.FoundAt);
        command.Parameters.AddWithValue(gap.State.ToString());
        command.Parameters.AddWithValue(gap.Tries);
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)gap.FinishedAt ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.TimestampTz });
        command.Parameters.AddWithValue(gap.Bars);
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)gap.Problem ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChartGap>> ListGapsAsync(string feed, int limit, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            select id, feed, from_time, until_time, found_at, state, tries, finished_at, bars, problem
            from chart_gaps where feed = $1 order by found_at desc, from_time desc limit $2
            """);
        command.Parameters.AddWithValue(feed);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var gaps = new List<ChartGap>();
        while (await reader.ReadAsync(cancellationToken))
        {
            gaps.Add(new ChartGap(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                Enum.Parse<ChartGapState>(reader.GetString(5)),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return gaps;
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
