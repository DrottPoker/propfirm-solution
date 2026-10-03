using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Trading.Service.Tenancy;

/// <summary>Healthy once the firms are loaded.</summary>
internal sealed class TenantsHealthCheck(TenantCatalog tenants) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(
            tenants.Ready.IsCompletedSuccessfully ? HealthCheckResult.Healthy()
            : tenants.Ready.IsFaulted ? HealthCheckResult.Unhealthy("The firms could not be loaded.")
            : HealthCheckResult.Unhealthy("Loading the firms."));
}
