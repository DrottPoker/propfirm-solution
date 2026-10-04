using Common.Postgres;

namespace Prop.Api.Persistence;

/// <summary>The prop platform's database scripts, in the order they are applied.</summary>
internal static class PropMigrations
{
    // Serializes migrations if several instances start at once.
    private const long LockKey = 8_402_115_377;

    public static readonly SqlMigrations All = new(typeof(PropMigrations).Assembly, ["0001_prop.sql", "0002_portal.sql", "0003_payouts.sql", "0004_firms.sql", "0005_orders.sql", "0006_billing.sql", "0007_reviews.sql", "0008_history.sql", "0009_admin_panel.sql", "0010_ops_panel.sql"], LockKey);
}
