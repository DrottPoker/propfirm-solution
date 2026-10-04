using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Common.Postgres;

using Npgsql;

namespace Prop.Api.Portal;

/// <summary>Whose password a reset link sets.</summary>
internal static class PasswordResetKinds
{
    public const string Trader = "trader";
    public const string Admin = "admin";
    public const string Staff = "staff";
}

/// <summary>What a one-time link from an email is worth when it is opened.</summary>
public enum LinkStatus
{
    Valid,
    Used,
    Expired,
}

/// <summary>A one-time link that was found, the person it is for and whether it still works.</summary>
internal sealed record FoundLink(Guid UserId, LinkStatus Status);

/// <summary>
/// One-time links that let a trader, an administrator or one of our staff choose a new password after forgetting it.
/// Only hashes of the tokens are stored, and a new link replaces the person's older unused ones.
/// </summary>
internal sealed class PasswordResets(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    /// <summary>Long enough to find the email, short enough that a link in an old inbox is worthless.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    /// <summary>Creates a link for the person and returns its token.</summary>
    public async Task<string> CreateAsync(string kind, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        await using var batch = dataSource.CreateBatch();

        // The person's unused links, and anyone's old ones, are removed.
        var cleanUp = new NpgsqlBatchCommand("delete from password_resets where (kind = $1 and user_id = $2 and used_at is null) or expires_at < $3");
        cleanUp.Parameters.AddWithValue(kind);
        cleanUp.Parameters.AddWithValue(userId);
        cleanUp.Parameters.AddWithValue(now - TimeSpan.FromDays(30));
        batch.BatchCommands.Add(cleanUp);

        var insert = new NpgsqlBatchCommand("insert into password_resets (token_hash, kind, user_id, created_at, expires_at) values ($1, $2, $3, $4, $5)");
        insert.Parameters.AddWithValue(Hash(token));
        insert.Parameters.AddWithValue(kind);
        insert.Parameters.AddWithValue(userId);
        insert.Parameters.AddWithValue(now);
        insert.Parameters.AddWithValue(now + Lifetime);
        batch.BatchCommands.Add(insert);

        await batch.ExecuteNonQueryAsync(cancellationToken);
        return token;
    }

    /// <summary>The link's person and whether it still works, without using it. Null for a link that does not exist.</summary>
    public async Task<FoundLink?> FindAsync(string kind, string token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select user_id, used_at is not null, expires_at <= $3 from password_resets where token_hash = $1 and kind = $2");
        command.Parameters.AddWithValue(Hash(token));
        command.Parameters.AddWithValue(kind);
        command.Parameters.AddWithValue(now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new FoundLink(reader.GetGuid(0), reader.GetBoolean(1) ? LinkStatus.Used : reader.GetBoolean(2) ? LinkStatus.Expired : LinkStatus.Valid)
            : null;
    }

    /// <summary>Uses up the link and returns its person. Null if it is unknown, used or expired.</summary>
    public async Task<Guid?> UseAsync(string kind, string token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);

        // One statement, so two requests with the same link cannot both succeed.
        await using var command = dataSource.CreateCommand(
            "update password_resets set used_at = $3 where token_hash = $1 and kind = $2 and used_at is null and expires_at > $3 returning user_id");
        command.Parameters.AddWithValue(Hash(token));
        command.Parameters.AddWithValue(kind);
        command.Parameters.AddWithValue(now);
        return await command.ExecuteScalarAsync(cancellationToken) as Guid?;
    }

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
