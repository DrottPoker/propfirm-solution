using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Prop.Api.Firms;

/// <summary>Healthy once the firms are loaded.</summary>
internal sealed class FirmsHealthCheck(FirmCatalog firms) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(
            firms.Ready.IsCompletedSuccessfully ? HealthCheckResult.Healthy()
            : firms.Ready.IsFaulted ? HealthCheckResult.Unhealthy("The firms could not be loaded.")
            : HealthCheckResult.Unhealthy("Loading the firms."));
}
