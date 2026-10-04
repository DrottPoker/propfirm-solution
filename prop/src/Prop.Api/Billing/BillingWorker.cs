using Common.Postgres;

using Prop.Api.Challenges;
using Prop.Api.Firms;

namespace Prop.Api.Billing;

/// <summary>
/// The billing's background work: checkout pages that ran out of time, saved cards that are due to be charged,
/// and for every firm that pays, the coming month's charge, pausing or resuming its challenges, automatic
/// expansion and the slots warning. Firms we suspended, and firms with paused challenges, are caught up too. Runs
/// every minute and when a payment or a taken slot wakes it.
/// </summary>
internal sealed partial class BillingWorker(
    BillingService billing,
    BillingStore store,
    FirmCatalog firms,
    DatabaseSchema schema,
    WorkSignals signals,
    TimeProvider time,
    ILogger<BillingWorker> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await schema.EnsureAsync(stoppingToken);
        await firms.Ready.WaitAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }

            await signals.Billing.WaitAsync(PollInterval, time, stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        foreach (var checkout in await store.ExpiredCheckoutsAsync(time.GetUtcNow(), cancellationToken))
        {
            await billing.ExpireCheckoutAsync(checkout.Id, BillingSources.Platform, cancellationToken);
        }

        await AttemptDueChargesAsync(cancellationToken);
        var paying = await store.PaidFirmsAsync(cancellationToken);
        foreach (var firmId in paying)
        {
            await RunForFirmAsync(firmId, billing.RunFirmAsync, cancellationToken);
        }

        foreach (var firmId in (await store.FirmsToKeepStandingAsync(cancellationToken)).Except(paying))
        {
            await RunForFirmAsync(firmId, billing.KeepChallengesStandingAsync, cancellationToken);
        }

        // The months that just became due.
        await AttemptDueChargesAsync(cancellationToken);
    }

    private async Task RunForFirmAsync(string firmId, Func<Firm, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        if (firms.ById(firmId) is not { } firm)
        {
            return;
        }

        try
        {
            await work(firm, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFirmFailed(logger, firmId, exception);
        }
    }

    private async Task AttemptDueChargesAsync(CancellationToken cancellationToken)
    {
        foreach (var (chargeId, _) in await store.DueChargesAsync(time.GetUtcNow(), cancellationToken))
        {
            await billing.AttemptAsync(chargeId, notifyOnDecline: true, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The billing work failed; it is tried again shortly")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The billing work for firm {FirmId} failed; it is tried again shortly")]
    private static partial void LogFirmFailed(ILogger logger, string firmId, Exception exception);
}
