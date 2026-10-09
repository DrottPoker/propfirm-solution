using Common.Postgres;

using Npgsql;

using Trading.Service.Identity;

namespace Trading.Service.Staff;

/// <summary>One of our own staff, who run the platform in the staff panel (ADR 0057). Not a firm's administrator.</summary>
public sealed record StaffUser(Guid Id, string Email, string PasswordHash);

/// <summary>Our staff on the trading platform.</summary>
public interface IStaffStore
{
    Task<StaffUser?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    Task<StaffUser?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Creates the staff member, or gives an existing one the password.</summary>
    Task SaveAsync(string email, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken);
}

internal sealed class PostgresStaffStore(NpgsqlDataSource dataSource, DatabaseSchema schema) : IStaffStore
{
    private const string SelectStaff = "select id, email, password_hash from staff_users";

    public Task<StaffUser?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        FindAsync($"{SelectStaff} where normalized_email = $1", Emails.Normalize(email), cancellationToken);

    public Task<StaffUser?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        FindAsync($"{SelectStaff} where id = $1", id, cancellationToken);

    public async Task SaveAsync(string email, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            insert into staff_users (id, email, normalized_email, password_hash, created_at) values ($1, $2, $3, $4, $5)
            on conflict (normalized_email) do update set password_hash = excluded.password_hash
            """);
        command.Parameters.AddWithValue(Guid.CreateVersion7(now));
        command.Parameters.AddWithValue(email.Trim());
        command.Parameters.AddWithValue(Emails.Normalize(email));
        command.Parameters.AddWithValue(passwordHash);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<StaffUser?> FindAsync(string sql, object parameter, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new StaffUser(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)) : null;
    }
}
