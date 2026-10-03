using Common.Postgres;

namespace Prop.Api.Persistence;

/// <summary>The prop platform's database scripts, in the order they are applied.</summary>
internal static class PropMigrations
{
    // Serializes migrations if several instances start at once.
    private const long LockKey = 8_402_115_377;

    public static readonly SqlMigrations All = new(typeof(PropMigrations).Assembly, ["0001_prop.sql"], LockKey);
}
