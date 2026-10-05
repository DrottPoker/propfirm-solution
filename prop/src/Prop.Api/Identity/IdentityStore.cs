using Common.Postgres;

using Npgsql;

using NpgsqlTypes;

namespace Prop.Api.Identity;

/// <summary>The firms' choices for ID checks, the checks started with providers and each trader's outcome. Changes that must happen together run in the caller's transaction.</summary>
internal sealed class IdentityStore(NpgsqlDataSource dataSource, DatabaseSchema schema)
{
    private const string SelectTrader =
        """
        select trader_id, provider, session_id, status, full_name, date_of_birth, country, address_checked, sanctions_checked, reason, decided_at, updated_at
        from trader_identity
        """;

    private const string SelectSession =
        "select id, firm_id, trader_id, provider, provider_session_id, url, check_address, check_sanctions, status, created_at, checked_at from identity_sessions";

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        return await dataSource.OpenConnectionAsync(cancellationToken);
    }

    /// <summary>The firm's choice, or null when it has not chosen yet.</summary>
    public async Task<IdentitySettings?> GetSettingsAsync(string firmId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            "select mode, required_before, check_address, check_sanctions, external_url, built_in_since, external_tested_at from firm_identity_settings where firm_id = $1",
            [firmId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new IdentitySettings(
                Enum.Parse<IdentityMode>(reader.GetString(0)),
                Enum.Parse<IdentityRequirement>(reader.GetString(1)),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6))
            : null;
    }

    /// <summary>
    /// Saves the firm's choice. The built-in checks keep the time they were turned on while they stay on. The firm's own
    /// service keeps when its address was set, and when it worked through the whole flow, while the address stays the same.
    /// </summary>
    public async Task SaveSettingsAsync(string firmId, IdentitySettings settings, string adminEmail, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteAsync(
            connection,
            """
            insert into firm_identity_settings (
                firm_id, mode, required_before, check_address, check_sanctions, external_url, updated_at, updated_by, built_in_since, external_since)
            values ($1, $2, $3, $4, $5, $6, $7, $8, case when $2 = 'BuiltIn' then $7::timestamptz end, case when $2 = 'External' then $7::timestamptz end)
            on conflict (firm_id) do update set
                mode = excluded.mode, required_before = excluded.required_before, check_address = excluded.check_address,
                check_sanctions = excluded.check_sanctions, external_url = excluded.external_url, updated_at = excluded.updated_at, updated_by = excluded.updated_by,
                built_in_since = case
                    when excluded.mode <> 'BuiltIn' then null
                    when firm_identity_settings.mode = 'BuiltIn' then firm_identity_settings.built_in_since
                    else excluded.updated_at end,
                external_since = case
                    when excluded.mode <> 'External' then null
                    when firm_identity_settings.mode = 'External' and firm_identity_settings.external_url = excluded.external_url then firm_identity_settings.external_since
                    else excluded.updated_at end,
                external_tested_at = case
                    when excluded.mode = 'External' and firm_identity_settings.mode = 'External' and firm_identity_settings.external_url = excluded.external_url
                        then firm_identity_settings.external_tested_at
                    end
            """,
            [firmId, settings.Mode.ToString(), settings.RequiredBefore.ToString(), settings.CheckAddress, settings.CheckSanctions, Nullable(settings.ExternalUrl, NpgsqlDbType.Text), now, adminEmail],
            cancellationToken);
    }

    /// <summary>The trader's ID check, or null when none was started or reported.</summary>
    public async Task<TraderIdentity?> GetTraderAsync(Guid traderId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return (await ReadTradersAsync(connection, $"{SelectTrader} where trader_id = $1", [traderId], cancellationToken)).SingleOrDefault();
    }

    /// <summary>The traders' ID checks, by trader.</summary>
    public async Task<Dictionary<Guid, TraderIdentity>> ListTradersAsync(Guid[] traderIds, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return (await ReadTradersAsync(connection, $"{SelectTrader} where trader_id = any($1)", [traderIds], cancellationToken)).ToDictionary(t => t.TraderId);
    }

    /// <summary>Whether the provider's check is one of ours.</summary>
    public async Task<bool> HasSessionAsync(IdentityProvider provider, string providerSessionId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, "select exists (select 1 from identity_sessions where provider = $1 and provider_session_id = $2)", [provider.ToString(), providerSessionId]);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>The firm's check, or null when it has none with the id.</summary>
    public async Task<IdentitySession?> GetSessionAsync(string firmId, Guid sessionId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, $"{SelectSession} where firm_id = $1 and id = $2", [firmId, sessionId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSession(reader) : null;
    }

    /// <summary>The trader's newest check that is not sent in yet, to send the trader back to it instead of starting another.</summary>
    public async Task<IdentitySession?> OpenSessionAsync(Guid traderId, IdentityProvider provider, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            $"{SelectSession} where trader_id = $1 and provider = $2 and status = 'Pending' and created_at >= $3 order by created_at desc limit 1",
            [traderId, provider.ToString(), since]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSession(reader) : null;
    }

    /// <summary>The check, locked until the caller's transaction ends. Null when the provider has no such check with us.</summary>
    public static async Task<IdentitySession?> LockSessionAsync(NpgsqlConnection connection, IdentityProvider provider, string providerSessionId, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, $"{SelectSession} where provider = $1 and provider_session_id = $2 for update", [provider.ToString(), providerSessionId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSession(reader) : null;
    }

    public static Task<int> InsertSessionAsync(NpgsqlConnection connection, IdentitySession session, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            """
            insert into identity_sessions (id, firm_id, trader_id, provider, provider_session_id, url, check_address, check_sanctions, status, created_at)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            """,
            [
                session.Id, session.FirmId, session.TraderId, session.Provider.ToString(), session.ProviderSessionId, session.Url.ToString(),
                session.CheckAddress, session.CheckSanctions, session.Status.ToString(), session.CreatedAt,
            ],
            cancellationToken);

    /// <summary>
    /// The check's status as the provider told it. A check sent in (in review or decided) is then something the provider
    /// charges for, once.
    /// </summary>
    /// <summary>
    /// The firm's own service decided about the trader. When the trader's latest check sent to the firm's page waits, it
    /// is decided, and when it was started at the service's current address the firm's whole flow has worked.
    /// </summary>
    public static Task<int> CompleteExternalFlowAsync(NpgsqlConnection connection, string firmId, Guid traderId, IdentityStatus status, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            """
            with flow as (
                update identity_sessions set status = $3, checked_at = $4, decided_at = $4
                where id = (
                    select id from identity_sessions
                    where firm_id = $1 and trader_id = $2 and provider = 'External' and status = 'Pending'
                    order by created_at desc limit 1)
                returning created_at)
            update firm_identity_settings s set external_tested_at = $4
            from flow
            where s.firm_id = $1 and s.mode = 'External' and s.external_tested_at is null and flow.created_at >= s.external_since
            """,
            [firmId, traderId, status.ToString(), now],
            cancellationToken);

    public static Task<int> UpdateSessionAsync(NpgsqlConnection connection, Guid sessionId, IdentityStatus status, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var submitted = status is IdentityStatus.InReview or IdentityStatus.Approved or IdentityStatus.Declined;
        var decided = status is IdentityStatus.Approved or IdentityStatus.Declined;
        return ExecuteAsync(
            connection,
            """
            update identity_sessions set status = $2, checked_at = $3,
                submitted_at = case when $4 then coalesce(submitted_at, $3) else submitted_at end,
                decided_at = case when $5 then coalesce(decided_at, $3) else decided_at end
            where id = $1
            """,
            [sessionId, status.ToString(), now, submitted, decided],
            cancellationToken);
    }

    /// <summary>We asked the provider about the check now, so it is not asked again at once.</summary>
    public async Task MarkCheckedAsync(Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "update identity_sessions set checked_at = $2 where id = $1", [sessionId, now], cancellationToken);
    }

    public static Task<int> SaveTraderAsync(NpgsqlConnection connection, string firmId, TraderIdentity identity, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            """
            insert into trader_identity (trader_id, firm_id, provider, session_id, status, full_name, date_of_birth, country, address_checked, sanctions_checked, reason, decided_at, updated_at)
            values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)
            on conflict (trader_id) do update set
                provider = excluded.provider, session_id = excluded.session_id, status = excluded.status, full_name = excluded.full_name,
                date_of_birth = excluded.date_of_birth, country = excluded.country, address_checked = excluded.address_checked,
                sanctions_checked = excluded.sanctions_checked, reason = excluded.reason, decided_at = excluded.decided_at, updated_at = excluded.updated_at
            """,
            [
                identity.TraderId, firmId, identity.Provider.ToString(), Nullable(identity.SessionId, NpgsqlDbType.Uuid), identity.Status.ToString(),
                Nullable(identity.FullName, NpgsqlDbType.Text), Nullable(identity.DateOfBirth, NpgsqlDbType.Date), Nullable(identity.Country, NpgsqlDbType.Text),
                identity.AddressChecked, identity.SanctionsChecked, Nullable(identity.Reason, NpgsqlDbType.Text),
                Nullable(identity.DecidedAt, NpgsqlDbType.TimestampTz), identity.UpdatedAt,
            ],
            cancellationToken);

    public static async Task<TraderIdentity?> LockTraderAsync(NpgsqlConnection connection, Guid traderId, CancellationToken cancellationToken) =>
        (await ReadTradersAsync(connection, $"{SelectTrader} where trader_id = $1 for update", [traderId], cancellationToken)).SingleOrDefault();

    /// <summary>The firm's checks through Didit that were sent in and are not billed yet: their ids, and how many also checked the address and sanctions.</summary>
    public static async Task<(List<Guid> Ids, int Address, int Sanctions)> UnbilledAsync(NpgsqlConnection connection, string firmId, CancellationToken cancellationToken)
    {
        await using var command = Command(
            connection,
            """
            select id, check_address, check_sanctions from identity_sessions
            where firm_id = $1 and provider = 'Didit' and submitted_at is not null and billed_charge_id is null
            for update
            """,
            [firmId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var (ids, address, sanctions) = (new List<Guid>(), 0, 0);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
            address += reader.GetBoolean(1) ? 1 : 0;
            sanctions += reader.GetBoolean(2) ? 1 : 0;
        }

        return (ids, address, sanctions);
    }

    public async Task<int> CountUnbilledAsync(string firmId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            "select count(*) from identity_sessions where firm_id = $1 and provider = 'Didit' and submitted_at is not null and billed_charge_id is null",
            [firmId]);
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public static Task<int> MarkBilledAsync(NpgsqlConnection connection, List<Guid> sessionIds, Guid chargeId, CancellationToken cancellationToken) =>
        ExecuteAsync(connection, "update identity_sessions set billed_charge_id = $2 where id = any($1)", [sessionIds.ToArray(), chargeId], cancellationToken);

    private static async Task<List<TraderIdentity>> ReadTradersAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var identities = new List<TraderIdentity>();
        while (await reader.ReadAsync(cancellationToken))
        {
            identities.Add(new TraderIdentity(
                reader.GetGuid(0),
                Enum.Parse<IdentityProvider>(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                Enum.Parse<IdentityStatus>(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                reader.GetFieldValue<DateTimeOffset>(11)));
        }

        return identities;
    }

    private static IdentitySession ReadSession(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            Enum.Parse<IdentityProvider>(reader.GetString(3)),
            reader.GetString(4),
            new Uri(reader.GetString(5)),
            reader.GetBoolean(6),
            reader.GetBoolean(7),
            Enum.Parse<IdentityStatus>(reader.GetString(8)),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10));

    private static NpgsqlParameter Nullable(object? value, NpgsqlDbType type) => new() { Value = value ?? DBNull.Value, NpgsqlDbType = type };

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, string sql, object[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter as NpgsqlParameter ?? new NpgsqlParameter { Value = parameter });
        }

        return command;
    }
}
