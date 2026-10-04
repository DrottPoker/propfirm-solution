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
