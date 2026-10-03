using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Trading.Service.Identity;

/// <summary>
/// One-time links that log a trader in to the terminal, so the firm's portal can open the terminal without
/// a second login. Only a hash of each token is stored.
/// </summary>
public interface ILoginLinkStore
{
    Task CreateAsync(byte[] tokenHash, Guid userId, string? accountId, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>Uses up the link. Null if it is unknown, already used or expired.</summary>
    Task<LoginLink?> UseAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Who the link logs in, and the account to open, if any.</summary>
public sealed record LoginLink(Guid UserId, string? AccountId);

internal static class LoginLinkTokens
{
    /// <summary>Long enough to open from the portal, short enough that a leaked link is soon worthless.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    /// <summary>256 random bits, safe in a URL.</summary>
    public static string Create() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
