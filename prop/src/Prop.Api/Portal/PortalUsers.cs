using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Common.Postgres;

using Npgsql;

using Prop.Api.Challenges;

namespace Prop.Api.Portal;

/// <summary>
/// Someone who logs in to a firm's portal: a trader, or one of the firm's administrators. A trader has the
/// <paramref name="Name"/> and <paramref name="Country"/> given when buying, and <paramref name="EmailConfirmedAt"/> once
/// they opened a link from an email to them.
/// </summary>
internal sealed record PortalUser(
    Guid Id,
    string FirmId,
    string Email,
    string Role,
    string? PasswordHash,
    string? Name = null,
    string? Country = null,
    DateTimeOffset? EmailConfirmedAt = null);

/// <summary>An invitation to choose a password for the portal. The token is in the link and is never stored.</summary>
internal sealed record PortalInvite(string Token, DateTimeOffset ExpiresAt);

internal static class PortalRoles
{
    public const string Trader = "trader";
    public const string Admin = "admin";
}

/// <summary>Traders and administrators of the firms' portals, and the invitations that let traders choose a password.</summary>
internal sealed class PortalUsers(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    /// <summary>Long enough for a trader to find the email, short enough that a forgotten one is soon worthless.</summary>
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    private const string SelectTrader = "select id, firm_id, email, password_hash, name, country, email_confirmed_at from traders";

    public Task<PortalUser?> FindTraderAsync(string firmId, string email, CancellationToken cancellationToken) =>
        FindAsync(
            $"{SelectTrader} where firm_id = $1 and normalized_email = $2",
            [firmId, Emails.Normalize(email)],
            PortalRoles.Trader,
            cancellationToken);

    public Task<PortalUser?> FindAdminAsync(string firmId, string email, CancellationToken cancellationToken) =>
        FindAsync(
            "select id, firm_id, email, password_hash from firm_admins where firm_id = $1 and normalized_email = $2",
            [firmId, Emails.Normalize(email)],
            PortalRoles.Admin,
            cancellationToken);

    public Task<PortalUser?> FindByIdAsync(Guid id, string role, CancellationToken cancellationToken) =>
        FindAsync(
            role == PortalRoles.Admin
                ? "select id, firm_id, email, password_hash from firm_admins where id = $1"
                : $"{SelectTrader} where id = $1",
            [id],
            role,
            cancellationToken);

    /// <summary>The trader's new password. Sessions from before it stop working.</summary>
    public Task SetTraderPasswordAsync(Guid traderId, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken) =>
        SetPasswordAsync("traders", traderId, passwordHash, now, cancellationToken);

    /// <summary>The trader opened a link from an email to them, so the email is theirs.</summary>
    public async Task ConfirmTraderEmailAsync(Guid traderId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("update traders set email_confirmed_at = coalesce(email_confirmed_at, $2) where id = $1");
        command.Parameters.AddWithValue(traderId);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// The trader's name, for a trader without one, such as a trader the firm started a challenge for. False when the trader
    /// has a name already: it is kept from the first order that gave it, as the firm knows the trader by it.
    /// </summary>
    public async Task<bool> SetTraderNameAsync(Guid traderId, string name, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("update traders set name = $2 where id = $1 and name is null");
        command.Parameters.AddWithValue(traderId);
        command.Parameters.AddWithValue(name);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>The trader's name as the firm corrects it, whether the trader had one or not.</summary>
    public async Task ChangeTraderNameAsync(Guid traderId, string name, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("update traders set name = $2 where id = $1");
        command.Parameters.AddWithValue(traderId);
        command.Parameters.AddWithValue(name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Whether the account is the trader's only one, so the trader has nothing at the firm from before it.</summary>
    public async Task<bool> IsOnlyAccountAsync(Guid traderId, Guid accountId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select not exists (select 1 from challenge_accounts where trader_id = $1 and id <> $2)");
        command.Parameters.AddWithValue(traderId);
        command.Parameters.AddWithValue(accountId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>The administrator's new password. Sessions from before it stop working.</summary>
    public Task SetAdminPasswordAsync(Guid adminId, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken) =>
        SetPasswordAsync("firm_admins", adminId, passwordHash, now, cancellationToken);

    /// <summary>
    /// The trader the firm's invitation is for, and whether it still works, without using it. Null if it is unknown or another
    /// firm's.
    /// </summary>
    public async Task<FoundLink?> FindInviteAsync(string firmId, string token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            select i.trader_id, i.used_at is not null, i.expires_at <= $3
            from portal_invites i join traders t on t.id = i.trader_id
            where i.token_hash = $1 and t.firm_id = $2
            """);
        command.Parameters.AddWithValue(Hash(token));
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new FoundLink(reader.GetGuid(0), reader.GetBoolean(1) ? LinkStatus.Used : reader.GetBoolean(2) ? LinkStatus.Expired : LinkStatus.Valid)
            : null;
    }

    /// <summary>Creates the firm's administrator, or gives an existing one the password.</summary>
    public Task SaveAdminAsync(string firmId, string email, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken) =>
        SaveAsync(
            """
            insert into firm_admins (id, firm_id, email, normalized_email, password_hash, created_at) values ($1, $2, $3, $4, $5, $6)
            on conflict (firm_id, normalized_email) do update set password_hash = excluded.password_hash
            """,
            firmId,
            email,
            passwordHash,
            now,
            cancellationToken);

    /// <summary>Creates the firm's configured trader, or gives an existing one the password. Its email is ours to vouch for, so it counts as confirmed.</summary>
    public Task SaveTraderAsync(string firmId, string email, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken) =>
        SaveAsync(
            """
            insert into traders (id, firm_id, email, normalized_email, password_hash, created_at, email_confirmed_at) values ($1, $2, $3, $4, $5, $6, $6)
            on conflict (firm_id, normalized_email) do update set password_hash = excluded.password_hash,
                email_confirmed_at = coalesce(traders.email_confirmed_at, excluded.email_confirmed_at)
            """,
            firmId,
            email,
            passwordHash,
            now,
            cancellationToken);

    /// <summary>
    /// Creates an invitation for the trader and returns it. Only the token's hash is stored. The trader's older
    /// unused invitations stop working, so only the newest one the firm sent can be used.
    /// </summary>
    public async Task<PortalInvite> CreateInviteAsync(Guid traderId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        var invite = new PortalInvite(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)), now + InviteLifetime);
        await using var batch = dataSource.CreateBatch();

        // Unused invitations of the trader, and used or expired ones of anyone after a while, are removed.
        var cleanUp = new NpgsqlBatchCommand("delete from portal_invites where (trader_id = $1 and used_at is null) or expires_at < $2");
        cleanUp.Parameters.AddWithValue(traderId);
        cleanUp.Parameters.AddWithValue(now - TimeSpan.FromDays(30));
        batch.BatchCommands.Add(cleanUp);

        var insert = new NpgsqlBatchCommand("insert into portal_invites (token_hash, trader_id, expires_at) values ($1, $2, $3)");
        insert.Parameters.AddWithValue(Hash(invite.Token));
        insert.Parameters.AddWithValue(traderId);
        insert.Parameters.AddWithValue(invite.ExpiresAt);
        batch.BatchCommands.Add(insert);

        await batch.ExecuteNonQueryAsync(cancellationToken);
        return invite;
    }

    /// <summary>Uses up the firm's invitation and returns its trader. Null if it is unknown, another firm's, used or expired.</summary>
    public async Task<Guid?> UseInviteAsync(string firmId, string token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);

        // One statement, so two requests with the same invitation cannot both succeed.
        await using var command = dataSource.CreateCommand(
            """
            update portal_invites i set used_at = $2
            from traders t
            where i.token_hash = $1 and t.id = i.trader_id and t.firm_id = $3 and i.used_at is null and i.expires_at > $2
            returning i.trader_id
            """);
        command.Parameters.AddWithValue(Hash(token));
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(firmId);
        return await command.ExecuteScalarAsync(cancellationToken) as Guid?;
    }

    private async Task SaveAsync(string sql, string firmId, string email, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(Guid.CreateVersion7(now));
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(email.Trim());
        command.Parameters.AddWithValue(Emails.Normalize(email));
        command.Parameters.AddWithValue(passwordHash);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SetPasswordAsync(string table, Guid id, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand($"update {table} set password_hash = $2, password_changed_at = $3 where id = $1");
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(passwordHash);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private async Task<PortalUser?> FindAsync(string sql, object[] parameters, string role, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(sql);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PortalUser(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                role,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.FieldCount > 4 && !reader.IsDBNull(4) ? reader.GetString(4) : null,
                reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetString(5) : null,
                reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetFieldValue<DateTimeOffset>(6) : null)
            : null;
    }
}
