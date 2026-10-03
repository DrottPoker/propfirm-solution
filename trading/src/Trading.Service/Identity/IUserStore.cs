namespace Trading.Service.Identity;

/// <summary>Traders who log in to the terminal, and the trading accounts they own.</summary>
public interface IUserStore
{
    /// <summary>Creates a user. Returns null if the email address is already used within the firm.</summary>
    Task<User?> CreateAsync(string tenantId, string email, string passwordHash, CancellationToken cancellationToken);

    Task<User?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken);

    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Records the owner of a new account. Returns false if the account already has an owner.</summary>
    Task<bool> AddAccountAsync(Guid userId, string accountId, CancellationToken cancellationToken);

    /// <summary>Undoes AddAccountAsync when the account could not be created.</summary>
    Task RemoveAccountAsync(string accountId, CancellationToken cancellationToken);

    Task<bool> OwnsAsync(Guid userId, string accountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> AccountsOfAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record User(Guid Id, string TenantId, string Email, string PasswordHash);

internal static class Emails
{
    /// <summary>Email addresses are compared without case and surrounding spaces.</summary>
    public static string Normalize(string email) => email.Trim().ToUpperInvariant();
}
