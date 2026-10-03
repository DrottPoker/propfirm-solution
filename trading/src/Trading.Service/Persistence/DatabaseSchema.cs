using Npgsql;

namespace Trading.Service.Persistence;

/// <summary>
/// Brings the database schema up to date once, before any store first uses it. The stores start in no
/// particular order: the login cookie keys, for example, are read before the engine starts.
/// </summary>
internal sealed class DatabaseSchema(NpgsqlDataSource dataSource, ILogger<DatabaseSchema> logger)
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
                _migration = Migrations.ApplyAsync(dataSource, logger, CancellationToken.None);
            }

            return _migration.WaitAsync(cancellationToken);
        }
    }

    /// <summary>For callers that cannot be asynchronous, like the store for the cookie keys.</summary>
    public void Ensure() => EnsureAsync(CancellationToken.None).GetAwaiter().GetResult();
}
