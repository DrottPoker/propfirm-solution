using System.Text.RegularExpressions;

using Microsoft.Extensions.Options;

using Trading.Engine;

namespace Trading.Service.Tenancy;

/// <summary>
/// Saves the configured firms to the tenant store at startup and loads every firm into the catalog. Startup
/// fails if the configuration is invalid, for example a group that is not configured or belongs to two firms.
/// </summary>
internal sealed partial class TenantSeeder(
    ITenantStore store,
    TenantCatalog catalog,
    EngineConfiguration engine,
    IConfiguration configuration,
    IOptions<TenancyOptions> tenancy,
    TimeProvider time) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var configured = (configuration.GetSection(TenantOptions.SectionName).Get<List<TenantOptions>>() ?? []).Select(Validate).ToList();
            Require(configured.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() == configured.Count, "Tenant ids must be unique.");
            Require(
                configured.SelectMany(t => t.Groups).Distinct(StringComparer.Ordinal).Count() == configured.Sum(t => t.Groups.Count),
                "A group can belong to one tenant only.");
            Require(
                tenancy.Value.NewTenantGroups.All(g => engine.Groups.Any(eg => eg.Id == g)),
                $"Every group in {TenancyOptions.SectionName}:{nameof(TenancyOptions.NewTenantGroups)} must be configured.");

            // The engine converts through USD, so each currency needs a pair with USD.
            Require(
                tenancy.Value.Currencies.All(c => c == "USD" || engine.Instruments.Any(i => (i.BaseCurrency, i.QuoteCurrency) == (c, "USD") || (i.BaseCurrency, i.QuoteCurrency) == ("USD", c))),
                $"Every currency in {TenancyOptions.SectionName}:{nameof(TenancyOptions.Currencies)} needs an instrument against USD.");

            foreach (var tenant in configured)
            {
                await store.SaveConfiguredAsync(tenant, time.GetUtcNow(), cancellationToken);
            }

            catalog.Load(await store.ListAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            catalog.LoadFailed(exception);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private Tenant Validate(TenantOptions options)
    {
        Require(TenantCatalog.IsValidId(options.Id), $"Tenant id '{options.Id}' must be 2 to 63 lowercase letters, digits or dashes.");
        Require(options.Name.Trim().Length > 0, $"Tenant {options.Id} needs a name.");
        Require(options.Groups.All(g => engine.Groups.Any(eg => eg.Id == g)), $"Tenant {options.Id} has a group that is not configured.");
        Require(Sha256Hex().IsMatch(options.AdminApiKeySha256), $"Tenant {options.Id} needs AdminApiKeySha256 as 64 lowercase hex characters.");
        Require(options.LoginUrl is null || TenantCatalog.IsValidLoginUrl(options.LoginUrl), $"Tenant {options.Id} needs LoginUrl as an absolute http or https address.");
        return new Tenant(options.Id, options.Name.Trim(), [.. options.Groups], Convert.FromHexString(options.AdminApiKeySha256), null, Listed: true, options.LoginUrl);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Invalid tenant configuration: {message}");
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();
}
