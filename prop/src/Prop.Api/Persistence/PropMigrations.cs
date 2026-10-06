using Common.Postgres;

namespace Prop.Api.Persistence;

/// <summary>The prop platform's database scripts, in the order they are applied.</summary>
internal static class PropMigrations
{
    // Serializes migrations if several instances start at once.
    private const long LockKey = 8_402_115_377;

    public static readonly SqlMigrations All = new(typeof(PropMigrations).Assembly, ["0001_prop.sql", "0002_portal.sql", "0003_payouts.sql", "0004_firms.sql", "0005_orders.sql", "0006_billing.sql", "0007_reviews.sql", "0008_history.sql", "0009_admin_panel.sql", "0010_ops_panel.sql", "0011_password_resets.sql", "0012_email_outbox.sql", "0013_payout_methods.sql", "0014_sandbox_flags.sql", "0015_trading_listing.sql", "0016_signup_currency.sql", "0017_invoices_and_buyers.sql", "0018_terminal_payouts_and_shop.sql", "0019_support.sql", "0020_support_opened_by.sql", "0021_identity.sql", "0022_identity_built_in_since.sql", "0023_identity_choice.sql", "0024_before_approval.sql", "0025_idle_sandboxes.sql", "0026_support_saved_replies.sql", "0027_admin_last_login.sql", "0028_shop_payouts.sql"], LockKey);
}
