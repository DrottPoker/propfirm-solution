using Common.Postgres;

using Npgsql;

using Prop.Api.Challenges;
using Prop.Api.Trading;
using Prop.Rules;

namespace Prop.Api.Firms;

/// <summary>
/// Creates the server on the trading platform for each firm that signed up, through the partner API, and moves
/// the firm to the sandbox with a first challenge. Tried again until it works. If the server was created but
/// the answer was lost, the next attempt asks for a new key.
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
        var tenant = await partner.CreateTenantAsync(firm.Id, firm.Name, cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Firm {FirmId} has its server on the trading platform and is in the sandbox")]
    private static partial void LogProvisioned(ILogger logger, string firmId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Creating the trading server of firm {FirmId} failed; trying again in {Delay}")]
    private static partial void LogFailed(ILogger logger, string firmId, TimeSpan delay, Exception exception);
}
