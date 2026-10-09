using System.Text.Json;

using Common.Postgres;

using Npgsql;

using NpgsqlTypes;

using Trading.Service.Json;

namespace Trading.Service.Tenancy;

internal sealed class PostgresTenantStore(NpgsqlDataSource dataSource, DatabaseSchema schema) : ITenantStore
{
    private const string UniqueViolation = "23505";

    private static readonly JsonSerializerOptions Json = EngineJson.CreateOptions();

    public async Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            select t.id, t.name, t.admin_api_key_sha256, t.partner_id, t.listed,
                   coalesce(array_agg(g.group_id order by g.group_id) filter (where g.group_id is not null), '{}'), t.login_url, t.logo_url,
                   t.created_at, t.created_by, t.terminal_profile
            from tenants t left join tenant_groups g on g.tenant_id = t.id
            group by t.id
            order by t.id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tenants = new List<Tenant>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tenants.Add(new Tenant(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetFieldValue<string[]>(5),
                reader.GetFieldValue<byte[]>(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4),
                reader.IsDBNull(6) ? null : new Uri(reader.GetString(6)),
                reader.IsDBNull(7) ? null : new Uri(reader.GetString(7)),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : JsonSerializer.Deserialize<TerminalProfile>(reader.GetString(10), Json)));
        }

        return tenants;
    }

    public async Task SaveConfiguredAsync(Tenant tenant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // A firm a partner or our staff made is never taken over by the configuration.
        await using (var upsert = new NpgsqlCommand(
            """
            insert into tenants (id, name, admin_api_key_sha256, partner_id, listed, login_url, created_at, terminal_profile)
            values ($1, $2, $3, null, $4, $6, $5, $7)
            on conflict (id) do update set name = excluded.name, admin_api_key_sha256 = excluded.admin_api_key_sha256, listed = excluded.listed,
                login_url = excluded.login_url, terminal_profile = coalesce(excluded.terminal_profile, tenants.terminal_profile)
            where tenants.partner_id is null and tenants.created_by is null
            """,
            connection))
        {
            upsert.Parameters.AddWithValue(tenant.Id);
            upsert.Parameters.AddWithValue(tenant.Name);
            upsert.Parameters.AddWithValue(tenant.AdminApiKeyHash);
            upsert.Parameters.AddWithValue(tenant.Listed);
            upsert.Parameters.AddWithValue(now);
            upsert.Parameters.AddWithValue((object?)tenant.LoginUrl?.ToString() ?? DBNull.Value);
            upsert.Parameters.Add(ProfileParameter(tenant.Terminal));
            if (await upsert.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                throw new InvalidOperationException($"Invalid tenant configuration: tenant {tenant.Id} was created by a partner or our staff.");
            }
        }

        await using (var clear = new NpgsqlCommand("delete from tenant_groups where tenant_id = $1", connection))
        {
            clear.Parameters.AddWithValue(tenant.Id);
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await InsertGroupsAsync(connection, tenant, cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            throw new InvalidOperationException($"Invalid tenant configuration: a group of tenant {tenant.Id} belongs to another tenant.", exception);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> CreateAsync(Tenant tenant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var insert = new NpgsqlCommand(
                "insert into tenants (id, name, admin_api_key_sha256, partner_id, listed, login_url, created_at, created_by, terminal_profile) values ($1, $2, $3, $4, $5, $7, $6, $8, $9)",
                connection))
            {
                insert.Parameters.AddWithValue(tenant.Id);
                insert.Parameters.AddWithValue(tenant.Name);
                insert.Parameters.AddWithValue(tenant.AdminApiKeyHash);
                insert.Parameters.AddWithValue((object?)tenant.PartnerId ?? DBNull.Value);
                insert.Parameters.AddWithValue(tenant.Listed);
                insert.Parameters.AddWithValue(now);
                insert.Parameters.AddWithValue((object?)tenant.LoginUrl?.ToString() ?? DBNull.Value);
                insert.Parameters.AddWithValue((object?)tenant.CreatedBy ?? DBNull.Value);
                insert.Parameters.Add(ProfileParameter(tenant.Terminal));
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await InsertGroupsAsync(connection, tenant, cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task SetAdminApiKeyAsync(string tenantId, byte[] adminApiKeyHash, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("update tenants set admin_api_key_sha256 = $2 where id = $1");
        command.Parameters.AddWithValue(tenantId);
        command.Parameters.AddWithValue(adminApiKeyHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetListingAsync(string tenantId, bool listed, Uri? loginUrl, Uri? logoUrl, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("update tenants set listed = $2, login_url = $3, logo_url = $4 where id = $1");
        command.Parameters.AddWithValue(tenantId);
        command.Parameters.AddWithValue(listed);
        command.Parameters.AddWithValue((object?)loginUrl?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)logoUrl?.ToString() ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetTerminalProfileAsync(string tenantId, TerminalProfile profile, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("update tenants set terminal_profile = $2 where id = $1");
        command.Parameters.AddWithValue(tenantId);
        command.Parameters.Add(ProfileParameter(profile));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlParameter ProfileParameter(TerminalProfile? profile) =>
        new() { Value = profile is null ? DBNull.Value : JsonSerializer.Serialize(profile, Json), NpgsqlDbType = NpgsqlDbType.Jsonb };

    private static async Task InsertGroupsAsync(NpgsqlConnection connection, Tenant tenant, CancellationToken cancellationToken)
    {
        foreach (var group in tenant.Groups)
        {
            await using var insert = new NpgsqlCommand("insert into tenant_groups (group_id, tenant_id) values ($1, $2)", connection);
            insert.Parameters.AddWithValue(group);
            insert.Parameters.AddWithValue(tenant.Id);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
