using Npgsql;

namespace Trading.Service.Persistence;

/// <summary>
/// Applies the SQL scripts in Persistence/Migrations in order, once each.
/// Never change a script that has been applied anywhere; add a new one.
/// </summary>
internal static partial class Migrations
{
    private static readonly string[] Scripts = ["0001_journal.sql", "0002_identity.sql"];

    // Serializes migrations if several instances start at once.
    private const long LockKey = 7_301_947_265;

    public static async Task ApplyAsync(NpgsqlDataSource dataSource, ILogger logger, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, $"select pg_advisory_xact_lock({LockKey})", cancellationToken);
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

        foreach (var script in Scripts.Where(s => !applied.Contains(s)))
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

    private static string ReadScript(string name)
    {
        using var stream = typeof(Migrations).Assembly.GetManifestResourceStream($"Migrations/{name}")
            ?? throw new InvalidOperationException($"Migration {name} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied database migration {Script}")]
    private static partial void LogApplied(ILogger logger, string script);
}
