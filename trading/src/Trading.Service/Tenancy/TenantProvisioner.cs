using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

using Trading.Engine;
using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Service.Engine;

namespace Trading.Service.Tenancy;

/// <summary>
/// Creates firms for partners: a copy of each template group in the engine, then the firm in the store and the
/// catalog. A group left by an attempt that failed before the firm was saved is taken over by the next attempt.
/// </summary>
internal sealed class TenantProvisioner(
    TenantCatalog tenants,
    ITenantStore store,
    EngineHost engine,
    EngineConfiguration configuration,
    IOptions<TenancyOptions> tenancy,
    TimeProvider time) : IDisposable
{
    public const int MaxNameLength = 100;

    // One creation at a time, so two requests for the same server cannot both take its groups.
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Whether a firm with the id can be created now.</summary>
    public bool IsAvailable(string id) =>
        TenantCatalog.IsValidId(id) && tenants.ById(id) is null && GroupIdsFor(id).All(g => tenants.ByGroup(g) is null && !IsConfiguredGroup(g));

    public async Task<ProvisioningResult> CreateAsync(Partner partner, string? id, string? name, CancellationToken cancellationToken)
    {
        var trimmedName = name?.Trim() ?? "";
        if (!TenantCatalog.IsValidId(id) || trimmedName.Length is 0 or > MaxNameLength)
        {
            return new ProvisioningResult.Invalid(
                $"The id must be 2 to 63 lowercase letters, digits or dashes, and the name 1 to {MaxNameLength} characters.");
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!IsAvailable(id!))
            {
                return new ProvisioningResult.Taken();
            }

            var groups = new List<TradingGroup>();
            foreach (var template in tenancy.Value.NewTenantGroups)
            {
                var group = configuration.Groups.Single(g => g.Id == template) with { Id = GroupIdFor(id!, template) };
                var events = await engine.SendAsync(t => new CreateGroup(t, group), cancellationToken);
                if (events.Select(e => e.Event).OfType<InputRejected>().FirstOrDefault() is { Reason: not RejectReason.DuplicateId } rejected)
                {
                    throw new InvalidOperationException($"The engine rejected group {group.Id}: {rejected.Reason}.");
                }

                groups.Add(await engine.QueryAsync(e => e.GetGroup(group.Id)!, cancellationToken));
            }

            var apiKey = CreateApiKey();
            var tenant = new Tenant(id!, trimmedName, [.. groups.Select(g => g.Id)], HashApiKey(apiKey), partner.Id, Listed: false);
            if (!await store.CreateAsync(tenant, time.GetUtcNow(), cancellationToken))
            {
                return new ProvisioningResult.Taken();
            }

            tenants.Put(tenant);
            return new ProvisioningResult.Created(tenant, groups, apiKey);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Gives the firm a new admin API key. The old one stops working at once.</summary>
    public async Task<string> ReplaceAdminApiKeyAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var apiKey = CreateApiKey();
            var hash = HashApiKey(apiKey);
            await store.SetAdminApiKeyAsync(tenant.Id, hash, cancellationToken);
            tenants.Put((tenants.ById(tenant.Id) ?? tenant) with { AdminApiKeyHash = hash });
            return apiKey;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private IEnumerable<string> GroupIdsFor(string id) => tenancy.Value.NewTenantGroups.Select(template => GroupIdFor(id, template));

    private bool IsConfiguredGroup(string groupId) => configuration.Groups.Any(g => g.Id == groupId);

    private static string GroupIdFor(string id, string template) => $"{id}-{template}";

    /// <summary>256 random bits, safe in a header.</summary>
    private static string CreateApiKey() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static byte[] HashApiKey(string apiKey) => SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
}

internal abstract record ProvisioningResult
{
    public sealed record Created(Tenant Tenant, IReadOnlyList<TradingGroup> Groups, string AdminApiKey) : ProvisioningResult;

    /// <summary>The server, or one of the groups it would get, is already taken.</summary>
    public sealed record Taken : ProvisioningResult;

    public sealed record Invalid(string Problem) : ProvisioningResult;
}
