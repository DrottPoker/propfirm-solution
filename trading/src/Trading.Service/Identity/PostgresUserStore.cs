using Npgsql;

namespace Trading.Service.Identity;

internal sealed class PostgresUserStore(NpgsqlDataSource dataSource) : IUserStore
{
    private const string UniqueViolation = "23505";

    public async Task<User?> CreateAsync(string tenantId, string email, string passwordHash, CancellationToken cancellationToken)
    {
        var user = new User(Guid.CreateVersion7(), tenantId, email.Trim(), passwordHash);
        await using var command = dataSource.CreateCommand(
            "insert into users (id, tenant_id, email, normalized_email, password_hash) values ($1, $2, $3, $4, $5)");
        command.Parameters.AddWithValue(user.Id);
        command.Parameters.AddWithValue(tenantId);
        command.Parameters.AddWithValue(user.Email);
        command.Parameters.AddWithValue(Emails.Normalize(email));
        command.Parameters.AddWithValue(passwordHash);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return user;
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            return null;
        }
    }

    public Task<User?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken) =>
        FindAsync(
            "select id, tenant_id, email, password_hash from users where tenant_id = $1 and normalized_email = $2",
            [tenantId, Emails.Normalize(email)],
            cancellationToken);

    public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        FindAsync("select id, tenant_id, email, password_hash from users where id = $1", [userId], cancellationToken);

    public async Task<bool> AddAccountAsync(Guid userId, string accountId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "insert into account_owners (account_id, user_id) values ($1, $2) on conflict (account_id) do nothing");
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(userId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task RemoveAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("delete from account_owners where account_id = $1");
        command.Parameters.AddWithValue(accountId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> OwnsAsync(Guid userId, string accountId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("select exists (select 1 from account_owners where account_id = $1 and user_id = $2)");
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<IReadOnlyList<string>> AccountsOfAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("select account_id from account_owners where user_id = $1 order by created_at, account_id");
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var accounts = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add(reader.GetString(0));
        }

        return accounts;
    }

    private async Task<User?> FindAsync(string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new User(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3))
            : null;
    }
}
