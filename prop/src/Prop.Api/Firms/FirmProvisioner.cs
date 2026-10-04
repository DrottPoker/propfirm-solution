using Common.Postgres;

using Npgsql;

using Prop.Api.Challenges;
using Prop.Api.Trading;
using Prop.Rules;

namespace Prop.Api.Firms;

/// <summary>
/// Creates the server on the trading platform for each firm that signed up, through the partner API, and moves
/// the firm to the sandbox with a first challenge. Tried again until it works. If the server was created but
/// the answer was lost, the next attempt asks for a new key. Also keeps the server's listing up to date (ADR 0027):
/// its traders log in through the portal's terminal page, and the server is on the platform's list once the firm is live.
/// </summary>
internal sealed partial class FirmProvisioner(
    NpgsqlDataSource dataSource,
    DatabaseSchema schema,
    FirmCatalog firms,
    FirmStore store,
    ITradingPartner partner,
    WorkSignals signals,
    TimeProvider time,
    ILogger<FirmProvisioner> logger) : BackgroundService
{
    /// <summary>The challenge a new firm starts with, so it can try the whole chain at once.</summary>
    public const string FirstChallengeId = "two-step-100k";

    public const decimal FirstChallengeBalance = 100_000m;

    /// <summary>How often firms are checked without a signal, for example after a restart.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await schema.EnsureAsync(stoppingToken);
        await firms.Ready.WaitAsync(stoppingToken);
        var retryDelay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            var failed = false;
            foreach (var firm in firms.All.Where(f => f.Status == FirmStatus.Provisioning))
            {
                try
                {
                    await ProvisionAsync(firm, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogFailed(logger, firm.Id, retryDelay, exception);
                    failed = true;
                }
            }

            try
            {
                await UpdateListingsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogListingFailed(logger, retryDelay, exception);
                failed = true;
            }

            try
            {
                if (failed)
                {
                    await Task.Delay(retryDelay, time, stoppingToken);
                    retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
                }
                else
                {
                    retryDelay = TimeSpan.FromSeconds(1);
                    await signals.Provisioning.WaitAsync(PollInterval, time, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task ProvisionAsync(Firm firm, CancellationToken cancellationToken)
    {
        var tenant = await partner.CreateTenantAsync(firm.Id, firm.Name, firm.AccountCurrency, cancellationToken);
        var apiKey = tenant?.AdminApiKey;
        if (tenant is null)
        {
            tenant = await partner.GetTenantAsync(firm.Id, cancellationToken)
                ?? throw new TradingPlatformRejectedException($"The server {firm.Id} on the trading platform belongs to someone else.");
            apiKey = await partner.ReplaceAdminKeyAsync(firm.Id, cancellationToken);
        }

        var group = tenant.Groups.Count > 0
            ? tenant.Groups[0]
            : throw new TradingPlatformRejectedException($"The server {firm.Id} on the trading platform has no group.");
        var trading = new FirmTrading(tenant.Server, apiKey!, group.Id, group.Currency);

        var now = time.GetUtcNow();
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await store.SetTradingAsync(connection, firm.Id, trading, now, cancellationToken);
            await ChallengeCatalog.SaveFirstAsync(
                connection, firm.Id, ChallengeTemplates.TwoStep(FirstChallengeId, FirstChallengeBalance, group.Currency), now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        firms.Put(await store.GetAsync(firm.Id, cancellationToken) ?? throw new InvalidOperationException($"Firm {firm.Id} disappeared."));
        LogProvisioned(logger, firm.Id);
    }

    /// <summary>Where the firm's traders log in when the terminal asks them to: the portal's page that opens it.</summary>
    public static Uri TerminalLoginOf(Uri portalUrl) => new(portalUrl, "terminal");

    // The firms that signed up whose server the trading platform has not heard about as it is now: listed or not, where
    // its traders log in, and its logo, an uploaded one at the portal's address.
    private async Task UpdateListingsAsync(CancellationToken cancellationToken)
    {
        const string Logo = "case when l.sha256 is not null then f.portal_url || 'api/portal/logo/' || encode(l.sha256, 'hex') else f.logo_url end";
        var stale = new List<(string Id, bool Listed, Uri LoginUrl, Uri? LogoUrl)>();
        await using (var command = dataSource.CreateCommand(
            $"""
            select f.id, f.status = 'Live', f.portal_url, {Logo}
            from firms f left join firm_logos l on l.firm_id = f.id
            where not f.configured and f.trading_server is not null
              and f.trading_listing is distinct from jsonb_build_object('listed', f.status = 'Live', 'loginUrl', f.portal_url || 'terminal', 'logoUrl', {Logo})
            """))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                stale.Add((
                    reader.GetString(0),
                    reader.GetBoolean(1),
                    TerminalLoginOf(new Uri(reader.GetString(2))),
                    reader.IsDBNull(3) ? null : new Uri(reader.GetString(3))));
            }
        }

        foreach (var (id, listed, loginUrl, logoUrl) in stale)
        {
            await partner.SetListingAsync(id, listed, loginUrl, logoUrl, cancellationToken);
            await using var update = dataSource.CreateCommand(
                "update firms set trading_listing = jsonb_build_object('listed', $2, 'loginUrl', $3::text, 'logoUrl', $4::text) where id = $1");
            update.Parameters.AddWithValue(id);
            update.Parameters.AddWithValue(listed);
            update.Parameters.AddWithValue(loginUrl.ToString());
            update.Parameters.Add(new NpgsqlParameter { Value = (object?)logoUrl?.ToString() ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
            await update.ExecuteNonQueryAsync(cancellationToken);
            LogListed(logger, id, listed);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Firm {FirmId} has its server on the trading platform and is in the sandbox")]
    private static partial void LogProvisioned(ILogger logger, string firmId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Creating the trading server of firm {FirmId} failed; trying again in {Delay}")]
    private static partial void LogFailed(ILogger logger, string firmId, TimeSpan delay, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "The trading platform knows where traders of firm {FirmId} log in, and lists its server: {Listed}")]
    private static partial void LogListed(ILogger logger, string firmId, bool listed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Updating the listing of a trading server failed; trying again in {Delay}")]
    private static partial void LogListingFailed(ILogger logger, TimeSpan delay, Exception exception);
}
