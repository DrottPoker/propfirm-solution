using Microsoft.Extensions.Logging;

using Npgsql;

namespace Common.Postgres;

/// <summary>
/// Brings the database schema up to date once, before any store first uses it. Stores start in no
/// particular order, so each one waits for this before its first query.
/// </summary>
public sealed class DatabaseSchema(NpgsqlDataSource dataSource, SqlMigrations migrations, ILogger<DatabaseSchema> logger)
{
    private readonly Lock _lock = new();
    private Task? _migration;

    /// <summary>Completes when the schema is up to date. A failed attempt is tried again on the next call.</summary>
    public Task EnsureAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_migration is null || _migration.IsFaulted || _migration.IsCanceled)
            {
                // Shared by all callers, so one caller giving up does not cancel it for the others.
                _migration = migrations.ApplyAsync(dataSource, logger, CancellationToken.None);
            }

            return _migration.WaitAsync(cancellationToken);
        }
    }

    /// <summary>For callers that cannot be asynchronous, like the store for the cookie keys.</summary>
    public void Ensure() => EnsureAsync(CancellationToken.None).GetAwaiter().GetResult();
}
