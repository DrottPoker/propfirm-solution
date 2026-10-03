using System.Reflection;

using Microsoft.Extensions.Logging;

using Npgsql;

namespace Common.Postgres;

/// <summary>
/// A product's SQL scripts, embedded in its assembly as <c>Migrations/{name}</c>, applied in order and once
/// each. An advisory lock serializes instances that start at the same time. Never change a script that has
/// been applied anywhere; add a new one.
/// </summary>
public sealed partial class SqlMigrations(Assembly assembly, IReadOnlyList<string> scripts, long lockKey)
{
    public async Task ApplyAsync(NpgsqlDataSource dataSource, ILogger logger, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, $"select pg_advisory_xact_lock({lockKey})", cancellationToken);
        await ExecuteAsync(
            connection,
            "create table if not exists schema_migrations (version text primary key, applied_at timestamptz not null default now())",
            cancellationToken);

        var applied = new HashSet<string>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand("select version from schema_migrations", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(reader.GetString(0));
            }
        }

        foreach (var script in scripts.Where(s => !applied.Contains(s)))
        {
            await ExecuteAsync(connection, ReadScript(script), cancellationToken);
            await using var record = new NpgsqlCommand("insert into schema_migrations (version) values ($1)", connection);
            record.Parameters.AddWithValue(script);
            await record.ExecuteNonQueryAsync(cancellationToken);
            LogApplied(logger, script);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string ReadScript(string name)
    {
        using var stream = assembly.GetManifestResourceStream($"Migrations/{name}")
            ?? throw new InvalidOperationException($"Migration {name} is not embedded in {assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied database migration {Script}")]
    private static partial void LogApplied(ILogger logger, string script);
}
