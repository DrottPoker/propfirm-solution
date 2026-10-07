namespace Trading.Service.Identity;

/// <summary>Traders who log in to the terminal, and the trading accounts they own.</summary>
public interface IUserStore
{
    /// <summary>Creates a user. Returns null if the email address is already used within the firm.</summary>
    Task<User?> CreateAsync(string tenantId, string email, string passwordHash, CancellationToken cancellationToken);

    Task<User?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken);

    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Replaces the password. Returns false if the user does not exist.</summary>
    Task<bool> SetPasswordHashAsync(Guid userId, string passwordHash, CancellationToken cancellationToken);

    /// <summary>Records the owner of a new account. Returns false if the account already has an owner.</summary>
    Task<bool> AddAccountAsync(Guid userId, string accountId, CancellationToken cancellationToken);

    /// <summary>Undoes AddAccountAsync when the account could not be created.</summary>
    Task RemoveAccountAsync(string accountId, CancellationToken cancellationToken);

    Task<bool> OwnsAsync(Guid userId, string accountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> AccountsOfAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Replaces what the terminal shows about the account.</summary>
    Task SetAccountDetailsAsync(AccountDetails details, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>What the terminal shows about each of the user's accounts, in the order of <see cref="AccountsOfAsync"/>.</summary>
    Task<IReadOnlyList<AccountDetails>> AccountDetailsOfAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Replaces the account's rules as the firm's system sees them.</summary>
    Task SetAccountRulesAsync(string accountId, AccountRules rules, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The account's rules, or null when the firm's system has not told them.</summary>
    Task<AccountRules?> AccountRulesOfAsync(string accountId, CancellationToken cancellationToken);

    /// <summary>Shows the notice at the top of the firm's terminals, or removes it with null (ADR 0053).</summary>
    Task SetTenantNoticeAsync(string tenantId, TerminalNotice? notice, CancellationToken cancellationToken);

    Task<TerminalNotice?> TenantNoticeOfAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>The user's settings in the terminal, by key, each the JSON the terminal stored.</summary>
    Task<IReadOnlyDictionary<string, string>> SettingsOfAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the setting, or removes it when <paramref name="json"/> is null. A new key is refused, and false
    /// returned, when the user already has <paramref name="maxSettings"/>.
    /// </summary>
    Task<bool> SetSettingAsync(Guid userId, string key, string? json, int maxSettings, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>
/// The account's rules as the firm's system sees them now, which the terminal shows and warns about (ADR 0052). Each
/// is null when the account has no such rule. <paramref name="Funded"/> tells a funded account, whose trading days
/// count toward a payout rather than passing the stage. <paramref name="PassBy"/> is when the stage fails unless it was
/// passed, and <paramref name="OpenPositionBy"/> when the account ends unless a position is opened before.
/// <paramref name="ConsistencyPercent"/> is the most of the profit the best trading day may have made for a payout,
/// and <paramref name="BestDayPercent"/> what it has made now.
/// </summary>
public sealed record AccountRules(
    bool Funded,
    int? TradingDaysRequired,
    int? TradingDaysCounted,
    DateTimeOffset? PassBy,
    DateTimeOffset? OpenPositionBy,
    decimal? ConsistencyPercent,
    decimal? BestDayPercent)
{
    /// <summary>An account the firm's system has told nothing about.</summary>
    public static readonly AccountRules None = new(false, null, null, null, null, null, null);
}

/// <summary>
/// What the terminal shows about an account, set by the firm's systems. <paramref name="Label"/> names it for the trader,
/// for example "#1001 Two-step 100K · Phase 1". <paramref name="ProfitTarget"/> is the balance that passes it.
/// <paramref name="TimeZone"/> is its trading day's, which the terminal shows times in. <paramref name="DetailsUrl"/> is
/// where the trader sees more about it, for example the account in the firm's portal. Each is null when not set.
/// </summary>
public sealed record AccountDetails(string AccountId, string? Label, decimal? ProfitTarget, string? TimeZone, Uri? DetailsUrl);

public sealed record User(Guid Id, string TenantId, string Email, string PasswordHash);

internal static class Emails
{
    /// <summary>Email addresses are compared without case and surrounding spaces.</summary>
    public static string Normalize(string email) => email.Trim().ToUpperInvariant();
}

/// <summary>
/// What the firm's terminals show at the top, for example that the price feed has stopped and what the firm does about
/// it (ADR 0053). <paramref name="Url"/> is where the trader reads more, such as the firm's status page.
/// </summary>
public sealed record TerminalNotice(string Title, string Text, NoticeLevel Level, Uri? Url, DateTimeOffset UpdatedAt);

public enum NoticeLevel
{
    Info,
    Warning,
}
