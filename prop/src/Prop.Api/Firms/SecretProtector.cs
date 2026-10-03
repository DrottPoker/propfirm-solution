using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

using Prop.Api.Configuration;

namespace Prop.Api.Firms;

/// <summary>
/// Encrypts the firms' secrets, such as their key on the trading platform, before they are stored. AES-GCM with a
/// key from the configuration, never from the database. The purpose ties a secret to its place, so a secret
/// copied to another firm or column cannot be decrypted there.
/// </summary>
internal sealed class SecretProtector
{
    private const string Prefix = "v1.";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public SecretProtector(IOptions<SecretsOptions> options)
    {
        var key = TryDecode(options.Value.Key);
        if (key is not { Length: 32 })
        {
            throw new InvalidOperationException($"{SecretsOptions.SectionName}:{nameof(SecretsOptions.Key)} must be 32 random bytes in base64.");
        }

        _key = key;
    }

    public string Protect(string secret, string purpose)
    {
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var sealedBox = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = sealedBox.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, sealedBox.AsSpan(NonceSize, plaintext.Length), sealedBox.AsSpan(NonceSize + plaintext.Length), Encoding.UTF8.GetBytes(purpose));
        return Prefix + Base64Url.EncodeToString(sealedBox);
    }

    /// <summary>The secret. Throws if it was protected with another key or purpose, or changed.</summary>
    public string Unprotect(string protectedSecret, string purpose)
    {
        if (!protectedSecret.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("The secret is not in a known format.");
        }

        var sealedBox = Base64Url.DecodeFromChars(protectedSecret.AsSpan(Prefix.Length));
        if (sealedBox.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("The secret is too short.");
        }

        var plaintext = new byte[sealedBox.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            sealedBox.AsSpan(0, NonceSize),
            sealedBox.AsSpan(NonceSize, plaintext.Length),
            sealedBox.AsSpan(NonceSize + plaintext.Length),
            plaintext,
            Encoding.UTF8.GetBytes(purpose));
        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[]? TryDecode(string key)
    {
        try
        {
            return Convert.FromBase64String(key);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
