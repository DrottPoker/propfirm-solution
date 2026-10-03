using System.Text.Json;

using Common.Postgres;

using Npgsql;

using NpgsqlTypes;

using Prop.Api.Payments;

namespace Prop.Api.Firms;

/// <summary>
/// The firms in the database. Secrets are encrypted before they are stored and decrypted when firms are read.
/// Whoever changes a firm puts the firm as read back into the catalog.
/// </summary>
internal sealed class FirmStore(NpgsqlDataSource dataSource, DatabaseSchema schema, SecretProtector secrets)
{
    private const string SelectFirm =
        """
        select f.id, f.name, f.status, f.api_key_sha256, f.trading_server, f.trading_api_key, f.trading_group, f.trading_currency,
               f.webhook_url, f.webhook_secret, f.portal_url, f.logo_url, f.colors,
               coalesce(array_agg(h.host order by h.host) filter (where h.host is not null), '{}'),
               f.payment_provider, f.stripe_secret_key, f.stripe_webhook_secret, f.checkout_url, f.shop_terms_url
        from firms f left join firm_hosts h on h.firm_id = f.id
        """;

    public async Task<IReadOnlyList<Firm>> ListAsync(CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand($"{SelectFirm} group by f.id order by f.id");
        return await ReadAsync(command, cancellationToken);
    }

    public async Task<Firm?> GetAsync(string firmId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand($"{SelectFirm} where f.id = $1 group by f.id");
        command.Parameters.AddWithValue(firmId);
        return (await ReadAsync(command, cancellationToken)).SingleOrDefault();
    }

    /// <summary>
    /// Saves a configured firm as the configuration has it, live, with its hosts. Throws if a firm that signed up
    /// has the id, or if a host belongs to another firm.
    /// </summary>
    public async Task SaveConfiguredAsync(Firm firm, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var trading = firm.Trading!;
        var saved = await ExecuteAsync(
            connection,
            """
            insert into firms (id, name, status, configured, api_key_sha256, trading_server, trading_api_key, trading_group, trading_currency,
                               webhook_url, webhook_secret, portal_url, logo_url, colors, created_at, updated_at,
                               payment_provider, stripe_secret_key, stripe_webhook_secret, checkout_url, shop_terms_url)
            values ($1, $2, $3, true, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $14, $15, $16, $17, $18, $19)
            on conflict (id) do update set
                name = excluded.name, status = excluded.status, api_key_sha256 = excluded.api_key_sha256,
                trading_server = excluded.trading_server, trading_api_key = excluded.trading_api_key,
                trading_group = excluded.trading_group, trading_currency = excluded.trading_currency,
                webhook_url = excluded.webhook_url, webhook_secret = excluded.webhook_secret, portal_url = excluded.portal_url,
                logo_url = excluded.logo_url, colors = excluded.colors, updated_at = excluded.updated_at,
                payment_provider = excluded.payment_provider, stripe_secret_key = excluded.stripe_secret_key,
                stripe_webhook_secret = excluded.stripe_webhook_secret, checkout_url = excluded.checkout_url, shop_terms_url = excluded.shop_terms_url
            where firms.configured
            """,
            [
                firm.Id, firm.Name, firm.Status.ToString(), (object?)firm.ApiKeyHash ?? DBNull.Value,
                trading.Server, secrets.Protect(trading.ApiKey, TradingKeyPurpose(firm.Id)), trading.Group, trading.Currency,
                (object?)firm.Webhook?.Url.ToString() ?? DBNull.Value,
                firm.Webhook is { } webhook ? secrets.Protect(webhook.Secret, WebhookSecretPurpose(firm.Id)) : DBNull.Value,
                firm.Portal.Url.ToString(), (object?)firm.Portal.Branding.LogoUrl ?? DBNull.Value, Colors(firm.Portal.Branding.Colors), now,
                .. PaymentValues(firm.Id, firm.Payments),
            ],
            cancellationToken);
        if (saved == 0)
        {
            throw new InvalidOperationException($"Invalid firm configuration: firm {firm.Id} signed up and cannot be configured.");
        }

        await ExecuteAsync(connection, "delete from firm_hosts where firm_id = $1", [firm.Id], cancellationToken);
        try
        {
            foreach (var host in firm.Portal.Hosts)
            {
                await ExecuteAsync(connection, "insert into firm_hosts (host, firm_id) values ($1, $2)", [host.ToLowerInvariant(), firm.Id], cancellationToken);
            }
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException($"Invalid firm configuration: a portal host of firm {firm.Id} belongs to another firm.", exception);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Inserts a firm that signed up, in the caller's transaction, with its portal on one host. False if the
    /// short name or the host is taken; the caller's transaction is then aborted.
    /// </summary>
    public static async Task<bool> InsertSignedUpAsync(
        NpgsqlConnection connection,
        string firmId,
        string name,
        Uri portalUrl,
        string termsVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(
                connection,
                """
                insert into firms (id, name, status, configured, portal_url, colors, terms_version, terms_accepted_at, created_at, updated_at)
                values ($1, $2, $3, false, $4, '{}', $5, $6, $6, $6)
                """,
                [firmId, name, FirmStatus.Provisioning.ToString(), portalUrl.ToString(), termsVersion, now],
                cancellationToken);
            await ExecuteAsync(connection, "insert into firm_hosts (host, firm_id) values ($1, $2)", [portalUrl.Host.ToLowerInvariant(), firmId], cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    /// <summary>The firm's server on the trading platform exists, so the firm moves on to the sandbox. Done in the caller's transaction.</summary>
    public Task SetTradingAsync(NpgsqlConnection connection, string firmId, FirmTrading trading, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            """
            update firms set trading_server = $2, trading_api_key = $3, trading_group = $4, trading_currency = $5,
                status = case when status = $6 then $7 else status end, updated_at = $8
            where id = $1
            """,
            [
                firmId, trading.Server, secrets.Protect(trading.ApiKey, TradingKeyPurpose(firmId)), trading.Group, trading.Currency,
                FirmStatus.Provisioning.ToString(), FirmStatus.Sandbox.ToString(), now,
            ],
            cancellationToken);

    /// <summary>The firm has paid and goes live. Done in the caller's transaction.</summary>
    public static Task SetLiveAsync(NpgsqlConnection connection, string firmId, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync(connection, "update firms set status = $2, updated_at = $3 where id = $1", [firmId, FirmStatus.Live.ToString(), now], cancellationToken);

    /// <summary>How the firm's portal takes payment. Stripe's keys are kept when <paramref name="payments"/> has none.</summary>
    public Task SetPaymentsAsync(string firmId, FirmPayments payments, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var values = PaymentValues(firmId, payments);
        return payments.Stripe is null
            ? UpdateAsync("payment_provider = $2, checkout_url = $3, shop_terms_url = $4", firmId, [values[0], values[3], values[4]], now, cancellationToken)
            : UpdateAsync(
                "payment_provider = $2, stripe_secret_key = $3, stripe_webhook_secret = $4, checkout_url = $5, shop_terms_url = $6",
                firmId,
                values,
                now,
                cancellationToken);
    }

    public Task SetBrandingAsync(string firmId, string? logoUrl, IReadOnlyDictionary<string, string> colors, DateTimeOffset now, CancellationToken cancellationToken) =>
        UpdateAsync("logo_url = $2, colors = $3", firmId, [(object?)logoUrl ?? DBNull.Value, Colors(colors)], now, cancellationToken);

    public Task SetApiKeyHashAsync(string firmId, byte[] apiKeyHash, DateTimeOffset now, CancellationToken cancellationToken) =>
        UpdateAsync("api_key_sha256 = $2", firmId, [apiKeyHash], now, cancellationToken);

    /// <summary>Sets where webhooks go. The secret is kept when <paramref name="secret"/> is null.</summary>
    public Task SetWebhookAsync(string firmId, Uri? url, string? secret, DateTimeOffset now, CancellationToken cancellationToken) =>
        secret is null
            ? UpdateAsync("webhook_url = $2", firmId, [(object?)url?.ToString() ?? DBNull.Value], now, cancellationToken)
            : UpdateAsync(
                "webhook_url = $2, webhook_secret = $3",
                firmId,
                [(object?)url?.ToString() ?? DBNull.Value, secrets.Protect(secret, WebhookSecretPurpose(firmId))],
                now,
                cancellationToken);

    public Task SetWebhookSecretAsync(string firmId, string secret, DateTimeOffset now, CancellationToken cancellationToken) =>
        UpdateAsync("webhook_secret = $2", firmId, [secrets.Protect(secret, WebhookSecretPurpose(firmId))], now, cancellationToken);

    /// <summary>Whether the firm has a secret for its webhooks.</summary>
    public async Task<bool> HasWebhookSecretAsync(string firmId, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var command = dataSource.CreateCommand("select webhook_secret is not null from firms where id = $1");
        command.Parameters.AddWithValue(firmId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static string TradingKeyPurpose(string firmId) => $"firm:{firmId}:trading-api-key";

    private static string WebhookSecretPurpose(string firmId) => $"firm:{firmId}:webhook-secret";

    private static string StripeKeyPurpose(string firmId) => $"firm:{firmId}:stripe-secret-key";

    private static string StripeWebhookSecretPurpose(string firmId) => $"firm:{firmId}:stripe-webhook-secret";

    // The provider, Stripe's encrypted keys, the checkout page and the terms, in the order of their columns.
    private object[] PaymentValues(string firmId, FirmPayments payments) =>
    [
        Text(payments.Provider?.ToString()),
        Text(payments.Stripe is { } stripe ? secrets.Protect(stripe.SecretKey, StripeKeyPurpose(firmId)) : null),
        Text(payments.Stripe is { } keys ? secrets.Protect(keys.WebhookSecret, StripeWebhookSecretPurpose(firmId)) : null),
        Text(payments.CheckoutUrl?.ToString()),
        Text(payments.TermsUrl?.ToString()),
    ];

    private static NpgsqlParameter Text(string? value) => new() { Value = (object?)value ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text };

    private static NpgsqlParameter Colors(IReadOnlyDictionary<string, string> colors) =>
        new() { Value = JsonSerializer.Serialize(colors), NpgsqlDbType = NpgsqlDbType.Jsonb };

    private async Task UpdateAsync(string set, string firmId, object[] values, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var nowParameter = values.Length + 2;
        await ExecuteAsync(connection, $"update firms set {set}, updated_at = ${nowParameter} where id = $1", [firmId, .. values, now], cancellationToken);
    }

    private async Task<IReadOnlyList<Firm>> ReadAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var firms = new List<Firm>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var name = reader.GetString(1);
            var trading = reader.IsDBNull(4)
                ? null
                : new FirmTrading(reader.GetString(4), secrets.Unprotect(reader.GetString(5), TradingKeyPurpose(id)), reader.GetString(6), reader.GetString(7));
            var webhook = reader.IsDBNull(8) || reader.IsDBNull(9)
                ? null
                : new FirmWebhook(new Uri(reader.GetString(8)), secrets.Unprotect(reader.GetString(9), WebhookSecretPurpose(id)));
            var colors = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(12)) ?? [];
            var stripe = reader.IsDBNull(15) || reader.IsDBNull(16)
                ? null
                : new StripeKeys(secrets.Unprotect(reader.GetString(15), StripeKeyPurpose(id)), secrets.Unprotect(reader.GetString(16), StripeWebhookSecretPurpose(id)));
            var payments = new FirmPayments(
                reader.IsDBNull(14) ? null : Enum.Parse<PaymentProvider>(reader.GetString(14)),
                stripe,
                reader.IsDBNull(17) ? null : new Uri(reader.GetString(17)),
                reader.IsDBNull(18) ? null : new Uri(reader.GetString(18)));
            firms.Add(new Firm(
                id,
                name,
                Enum.Parse<FirmStatus>(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<byte[]>(3),
                trading,
                webhook,
                new FirmPortal(new Uri(reader.GetString(10)), reader.GetFieldValue<string[]>(13), new Branding(name, reader.IsDBNull(11) ? null : reader.GetString(11), colors)),
                payments));
        }

        return firms;
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter as NpgsqlParameter ?? new NpgsqlParameter { Value = parameter });
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
