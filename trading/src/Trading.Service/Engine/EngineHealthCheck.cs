using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Trading.Service.Engine;

/// <summary>Healthy once the journal is replayed, and as long as it can be written.</summary>
internal sealed class EngineHealthCheck(EngineHost engine) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(
            engine.JournalFailed ? HealthCheckResult.Unhealthy("The journal cannot be written.")
            : engine.Ready.IsCompletedSuccessfully ? HealthCheckResult.Healthy()
            : engine.Ready.IsFaulted ? HealthCheckResult.Unhealthy("Recovery failed.")
            : HealthCheckResult.Unhealthy("Recovering from the journal."));
}
