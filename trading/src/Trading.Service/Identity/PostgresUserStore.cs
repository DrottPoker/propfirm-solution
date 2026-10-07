using System.Text.Json;

using Common.Postgres;

using Npgsql;

using NpgsqlTypes;

namespace Trading.Service.Identity;

internal sealed class PostgresUserStore(NpgsqlDataSource dataSource, DatabaseSchema schema) : IUserStore
{
    private const string UniqueViolation = "23505";

    private static readonly JsonSerializerOptions RulesJson = new(JsonSerializerDefaults.Web);

    public async Task<User?> CreateAsync(string tenantId, string email, string passwordHash, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
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

    public async Task<bool> SetPasswordHashAsync(Guid userId, string passwordHash, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("update users set password_hash = $2 where id = $1");
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(passwordHash);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> AddAccountAsync(Guid userId, string accountId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            "insert into account_owners (account_id, user_id) values ($1, $2) on conflict (account_id) do nothing");
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(userId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task RemoveAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("delete from account_owners where account_id = $1");
        command.Parameters.AddWithValue(accountId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> OwnsAsync(Guid userId, string accountId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select exists (select 1 from account_owners where account_id = $1 and user_id = $2)");
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<IReadOnlyList<string>> AccountsOfAsync(Guid userId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
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

    public async Task SetAccountDetailsAsync(AccountDetails details, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            insert into account_details (account_id, label, profit_target, time_zone, details_url, updated_at) values ($1, $2, $3, $4, $5, $6)
            on conflict (account_id) do update set label = excluded.label, profit_target = excluded.profit_target, time_zone = excluded.time_zone,
                details_url = excluded.details_url, updated_at = excluded.updated_at
            """);
        command.Parameters.AddWithValue(details.AccountId);
        command.Parameters.AddWithValue((object?)details.Label ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)details.ProfitTarget ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)details.TimeZone ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)details.DetailsUrl?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccountDetails>> AccountDetailsOfAsync(Guid userId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            select o.account_id, d.label, d.profit_target, d.time_zone, d.details_url
            from account_owners o left join account_details d on d.account_id = o.account_id
            where o.user_id = $1
            order by o.created_at, o.account_id
            """);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var details = new List<AccountDetails>();
        while (await reader.ReadAsync(cancellationToken))
        {
            details.Add(new AccountDetails(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : new Uri(reader.GetString(4))));
        }

        return details;
    }

    public async Task SetAccountRulesAsync(string accountId, AccountRules rules, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            insert into account_rules (account_id, rules, updated_at) values ($1, $2, $3)
            on conflict (account_id) do update set rules = excluded.rules, updated_at = excluded.updated_at
            """);
        command.Parameters.AddWithValue(accountId);
        command.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.Serialize(rules, RulesJson), NpgsqlDbType = NpgsqlDbType.Jsonb });
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AccountRules?> AccountRulesOfAsync(string accountId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select rules::text from account_rules where account_id = $1");
        command.Parameters.AddWithValue(accountId);
        return await command.ExecuteScalarAsync(cancellationToken) is string json ? JsonSerializer.Deserialize<AccountRules>(json, RulesJson) : null;
    }

    public async Task SetTenantNoticeAsync(string tenantId, TerminalNotice? notice, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = notice is null
            ? dataSource.CreateCommand("delete from tenant_notices where tenant_id = $1")
            : dataSource.CreateCommand(
                """
                insert into tenant_notices (tenant_id, notice, updated_at) values ($1, $2, $3)
                on conflict (tenant_id) do update set notice = excluded.notice, updated_at = excluded.updated_at
                """);
        command.Parameters.AddWithValue(tenantId);
        if (notice is not null)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.Serialize(notice, RulesJson), NpgsqlDbType = NpgsqlDbType.Jsonb });
            command.Parameters.AddWithValue(notice.UpdatedAt);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<TerminalNotice?> TenantNoticeOfAsync(string tenantId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select notice::text from tenant_notices where tenant_id = $1");
        command.Parameters.AddWithValue(tenantId);
        return await command.ExecuteScalarAsync(cancellationToken) is string json ? JsonSerializer.Deserialize<TerminalNotice>(json, RulesJson) : null;
    }

    public async Task<IReadOnlyDictionary<string, string>> SettingsOfAsync(Guid userId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select key, value::text from user_settings where user_id = $1 order by key");
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            settings[reader.GetString(0)] = reader.GetString(1);
        }

        return settings;
    }

    public async Task<bool> SetSettingAsync(Guid userId, string key, string? json, int maxSettings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        if (json is null)
        {
            await using var delete = dataSource.CreateCommand("delete from user_settings where user_id = $1 and key = $2");
            delete.Parameters.AddWithValue(userId);
            delete.Parameters.AddWithValue(key);
            await delete.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }

        // A key already stored is always replaced. A new one is added only while the user has room for it.
        await using var command = dataSource.CreateCommand(
            """
            insert into user_settings (user_id, key, value, updated_at)
            select $1, $2, $3, $4
            where exists (select 1 from user_settings where user_id = $1 and key = $2)
               or (select count(*) from user_settings where user_id = $1) < $5
            on conflict (user_id, key) do update set value = excluded.value, updated_at = excluded.updated_at
            """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(key);
        command.Parameters.Add(new NpgsqlParameter { Value = json, NpgsqlDbType = NpgsqlDbType.Jsonb });
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(maxSettings);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private async Task<User?> FindAsync(string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
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
