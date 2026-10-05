using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Common.Postgres;

using Npgsql;

using Prop.Api.Challenges;
using Prop.Api.Review;

namespace Prop.Api.Portal;

/// <summary>One of the firm's administrators.</summary>
internal sealed record FirmAdmin(Guid Id, string Email, DateTimeOffset CreatedAt);

/// <summary>An invitation that waits for the person to choose a password.</summary>
internal sealed record PendingAdminInvite(string Email, DateTimeOffset ExpiresAt);

internal enum AdminInviteOutcome
{
    Accepted,

    /// <summary>The invitation is unknown, another firm's, used or expired.</summary>
    Invalid,

    /// <summary>The person became an administrator some other way in the meantime.</summary>
    AlreadyAdmin,
}

/// <summary>
/// The firm's administrators beyond logging in: one-time login links, invitations and removal. Only hashes of
/// tokens are stored. A removed administrator's session stops working at once (see <see cref="PortalAuth"/>).
/// </summary>
internal sealed class FirmAdmins(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    /// <summary>Long enough to land on the portal after signing up, short enough that a leaked link is soon worthless.</summary>
    public static readonly TimeSpan LoginLinkLifetime = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    /// <summary>Creates the firm's administrator in the caller's transaction and returns its id.</summary>
    public static async Task<Guid> InsertAsync(NpgsqlConnection connection, string firmId, string email, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7(now);
        await ExecuteAsync(
            connection,
            "insert into firm_admins (id, firm_id, email, normalized_email, password_hash, created_at) values ($1, $2, $3, $4, $5, $6)",
            [id, firmId, email.Trim(), Emails.Normalize(email), passwordHash, now],
            cancellationToken);
        return id;
    }

    /// <summary>A link in an email that helps an administrator find the firm: long enough to find the email.</summary>
    public static readonly TimeSpan EmailedLoginLinkLifetime = TimeSpan.FromMinutes(30);

    /// <summary>Creates a one-time link for the administrator in the caller's transaction, and returns its token.</summary>
    public static Task<string> CreateLoginLinkAsync(NpgsqlConnection connection, Guid adminId, DateTimeOffset now, CancellationToken cancellationToken) =>
        CreateLoginLinkAsync(connection, adminId, now, LoginLinkLifetime, cancellationToken);

    /// <summary>Creates a one-time link for the administrator that works for <paramref name="lifetime"/>, and returns its token.</summary>
    public static async Task<string> CreateLoginLinkAsync(NpgsqlConnection connection, Guid adminId, DateTimeOffset now, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var token = NewToken();
        await ExecuteAsync(
            connection,
            "delete from admin_login_links where expires_at < $1",
            [now - TimeSpan.FromDays(1)],
            cancellationToken);
        await ExecuteAsync(
            connection,
            "insert into admin_login_links (token_hash, admin_id, expires_at) values ($1, $2, $3)",
            [Hash(token), adminId, now + lifetime],
            cancellationToken);
        return token;
    }

    /// <summary>Uses up the login link and returns its administrator. Null if it is unknown, another firm's, used or expired.</summary>
    public async Task<Guid?> UseLoginLinkAsync(string firmId, string token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);

        // One statement, so two requests with the same link cannot both succeed.
        await using var command = dataSource.CreateCommand(
            """
            update admin_login_links l set used_at = $2
            from firm_admins a
            where l.token_hash = $1 and a.id = l.admin_id and a.firm_id = $3 and l.used_at is null and l.expires_at > $2
            returning l.admin_id
            """);
        command.Parameters.AddWithValue(Hash(token));
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(firmId);
        return await command.ExecuteScalarAsync(cancellationToken) as Guid?;
    }

    /// <summary>
    /// Emails the person a one-time login link to each firm they administer, when there are any, so a firm's administrator who
    /// does not remember the firm's address finds it from the platform's own. Nothing is sent for an unknown email.
    /// </summary>
    public async Task SendLoginHelpAsync(string email, string platform, WorkSignals signals, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var firms = new List<(Guid AdminId, string Email, string Name, Uri PortalUrl)>();
        await using (var command = new NpgsqlCommand(
            "select a.id, a.email, f.name, f.portal_url from firm_admins a join firms f on f.id = a.firm_id where a.normalized_email = $1 order by f.name",
            connection))
        {
            command.Parameters.AddWithValue(Emails.Normalize(email));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                firms.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), new Uri(reader.GetString(3))));
            }
        }

        if (firms.Count == 0)
        {
            return;
        }

        var links = new List<(string Name, Uri Link, Uri Login)>();
        foreach (var firm in firms)
        {
            var token = await CreateLoginLinkAsync(connection, firm.AdminId, now, EmailedLoginLinkLifetime, cancellationToken);
            links.Add((firm.Name, new Uri(firm.PortalUrl, $"admin/welcome?token={token}"), new Uri(firm.PortalUrl, "admin/login")));
        }

        await Email.EmailOutbox.AddAsync(connection, Email.PlatformEmails.LoginHelp(platform, firms[0].Email, links, EmailedLoginLinkLifetime), "login_help", null, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        signals.Emails.Set();
    }

    public async Task<IReadOnlyList<FirmAdmin>> ListAsync(string firmId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select id, email, created_at from firm_admins where firm_id = $1 order by created_at, normalized_email");
        command.Parameters.AddWithValue(firmId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var admins = new List<FirmAdmin>();
        while (await reader.ReadAsync(cancellationToken))
        {
            admins.Add(new FirmAdmin(reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2)));
        }

        return admins;
    }

    /// <summary>The emails of the firm's administrators, the first first, in the caller's transaction.</summary>
    public static async Task<IReadOnlyList<string>> EmailsAsync(NpgsqlConnection connection, string firmId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select email from firm_admins where firm_id = $1 order by created_at, normalized_email", connection);
        command.Parameters.AddWithValue(firmId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var emails = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            emails.Add(reader.GetString(0));
        }

        return emails;
    }

    public async Task<bool> IsAdminAsync(string firmId, string email, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select exists (select 1 from firm_admins where firm_id = $1 and normalized_email = $2)");
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(Emails.Normalize(email));
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    /// <summary>Removes the administrator. False if the firm has no such administrator.</summary>
    public async Task<bool> RemoveAsync(string firmId, Guid adminId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("delete from firm_admins where firm_id = $1 and id = $2");
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(adminId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<PendingAdminInvite>> ListInvitesAsync(string firmId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            "select email, expires_at from admin_invites where firm_id = $1 and used_at is null and expires_at > $2 order by created_at");
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var invites = new List<PendingAdminInvite>();
        while (await reader.ReadAsync(cancellationToken))
        {
            invites.Add(new PendingAdminInvite(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1)));
        }

        return invites;
    }

    /// <summary>
    /// Creates an invitation and returns its token. Older unused invitations for the same email stop working, so
    /// only the newest one can be used. With <paramref name="limit"/>, null when the firm has made that many invitations
    /// in <see cref="FirmApproval.InvitePeriod"/>, those taken back or sent again too (ADR 0043).
    /// </summary>
    public async Task<(string Token, DateTimeOffset ExpiresAt)?> CreateInviteAsync(string firmId, string email, int? limit, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        var token = NewToken();
        var expiresAt = now + InviteLifetime;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (limit is { } most)
        {
            // The firm's invitations wait for each other, so the limit holds.
            await ExecuteAsync(connection, "select pg_advisory_xact_lock(hashtextextended('admin_invites:' || $1, 0))", [firmId], cancellationToken);
            await using var count = new NpgsqlCommand("select count(*) from admin_invites where firm_id = $1 and created_at > $2", connection);
            count.Parameters.AddWithValue(firmId);
            count.Parameters.AddWithValue(now - FirmApproval.InvitePeriod);
            if ((long)(await count.ExecuteScalarAsync(cancellationToken))! >= most)
            {
                return null;
            }
        }

        // Older invitations are expired rather than deleted, so they are counted until they are cleaned up a month later.
        await ExecuteAsync(
            connection,
            "update admin_invites set expires_at = $3 where firm_id = $1 and normalized_email = $2 and used_at is null and expires_at > $3",
            [firmId, Emails.Normalize(email), now],
            cancellationToken);
        await ExecuteAsync(connection, "delete from admin_invites where expires_at < $1", [now - TimeSpan.FromDays(30)], cancellationToken);
        await ExecuteAsync(
            connection,
            "insert into admin_invites (token_hash, firm_id, email, normalized_email, created_at, expires_at) values ($1, $2, $3, $4, $5, $6)",
            [Hash(token), firmId, email.Trim(), Emails.Normalize(email), now, expiresAt],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (token, expiresAt);
    }

    /// <summary>The email the firm's invitation is for, and whether it still works, without using it. Null if it is unknown or another firm's.</summary>
    public async Task<(LinkStatus Status, string Email)?> FindInviteAsync(string firmId, string token, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            "select email, used_at is not null, expires_at <= $3 from admin_invites where token_hash = $1 and firm_id = $2");
        command.Parameters.AddWithValue(Hash(token));
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetBoolean(1) ? LinkStatus.Used : reader.GetBoolean(2) ? LinkStatus.Expired : LinkStatus.Valid, reader.GetString(0))
            : null;
    }

    /// <summary>
    /// Takes back the firm's unused invitations to the email, so their links stop working. They are expired rather than
    /// deleted, so they still count before we approve the firm. False when there were none.
    /// </summary>
    public async Task<bool> WithdrawInvitesAsync(string firmId, string email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            "update admin_invites set expires_at = $3 where firm_id = $1 and normalized_email = $2 and used_at is null and expires_at > $3");
        command.Parameters.AddWithValue(firmId);
        command.Parameters.AddWithValue(Emails.Normalize(email));
        command.Parameters.AddWithValue(now);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <summary>Takes back an invitation whose email could not be sent.</summary>
    public async Task WithdrawInviteAsync(string token, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("delete from admin_invites where token_hash = $1");
        command.Parameters.AddWithValue(Hash(token));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Uses up the invitation and creates the administrator with the password, in one transaction.</summary>
    public async Task<(AdminInviteOutcome Outcome, Guid AdminId, string Email)> AcceptInviteAsync(
        string firmId,
        string token,
        string passwordHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        string email;
        await using (var use = new NpgsqlCommand(
            """
            update admin_invites set used_at = $2
            where token_hash = $1 and firm_id = $3 and used_at is null and expires_at > $2
            returning email
            """,
            connection))
        {
            use.Parameters.AddWithValue(Hash(token));
            use.Parameters.AddWithValue(now);
            use.Parameters.AddWithValue(firmId);
            if (await use.ExecuteScalarAsync(cancellationToken) is not string invited)
            {
                return (AdminInviteOutcome.Invalid, Guid.Empty, "");
            }

            email = invited;
        }

        Guid adminId;
        try
        {
            adminId = await InsertAsync(connection, firmId, email, passwordHash, now, cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return (AdminInviteOutcome.AlreadyAdmin, Guid.Empty, email);
        }

        await transaction.CommitAsync(cancellationToken);
        return (AdminInviteOutcome.Accepted, adminId, email);
    }

    /// <summary>256 random bits, safe in a URL.</summary>
    private static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
