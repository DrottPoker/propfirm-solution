using Common.Postgres;

namespace Trading.Service.Persistence;

/// <summary>The trading service's database scripts, in the order they are applied.</summary>
internal static class TradingMigrations
{
    // Serializes migrations if several instances start at once.
    private const long LockKey = 7_301_947_265;

    public static readonly SqlMigrations All = new(
        typeof(TradingMigrations).Assembly,
        ["0001_journal.sql", "0002_identity.sql", "0003_integration.sql"],
        LockKey);
}
