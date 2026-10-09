using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using Trading.Engine;
using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Service.Engine;
using Trading.Service.Staff;

namespace Trading.Service.Tenancy;

/// <summary>
/// Creates firms for partners and our staff: a copy of each template group in the engine, then the firm in the store
/// and the catalog. A group left by an attempt that failed before the firm was saved is taken over by the next attempt.
/// Every change is written to the platform's log with who made it (ADR 0057).
/// </summary>
internal sealed class TenantProvisioner(
    TenantCatalog tenants,
    ITenantStore store,
    EngineHost engine,
    EngineConfiguration configuration,
    IOptions<TenancyOptions> tenancy,
    PlatformEvents events,
    IConfiguration settings,
    TimeProvider time) : IDisposable
{
    public const int MaxNameLength = 100;

    // One creation at a time, so two requests for the same server cannot both take its groups.
    private readonly SemaphoreSlim _lock = new(1, 1);

    // The configured firms whose terminal the configuration sets, which it sets again at every start.
    private readonly Lazy<HashSet<string>> _configuredTerminals = new(() =>
        [.. (settings.GetSection(TenantOptions.SectionName).Get<List<TenantOptions>>() ?? []).Where(t => t.Terminal is not null).Select(t => t.Id)]);

    /// <summary>Whether a firm with the id can be created now.</summary>
    public bool IsAvailable(string id) =>
        TenantCatalog.IsValidId(id) && tenants.ById(id) is null && GroupIdsFor(id).All(g => tenants.ByGroup(g) is null && !IsConfiguredGroup(g));

    /// <summary>
    /// Creates the firm with its groups in <paramref name="currency"/>, one of <see cref="TenancyOptions.Currencies"/>, or
    /// in the templates' own currency when it is null. A firm made with a <paramref name="kind"/> gets that kind's
    /// terminal with password login (ADR 0058); one without has the standard until its partner sets it.
    /// </summary>
    public async Task<ProvisioningResult> CreateAsync(
        TenantMaker maker,
        string? id,
        string? name,
        string? currency,
        TerminalKind? kind,
        CancellationToken cancellationToken)
    {
        var trimmedName = name?.Trim() ?? "";
        if (!TenantCatalog.IsValidId(id) || trimmedName.Length is 0 or > MaxNameLength)
        {
            return new ProvisioningResult.Invalid(
                $"The id must be 2 to 63 lowercase letters, digits or dashes, and the name 1 to {MaxNameLength} characters.");
        }

        if (currency is not null && !tenancy.Value.Currencies.Contains(currency, StringComparer.Ordinal))
        {
            return new ProvisioningResult.InvalidCurrency($"The account currency must be one of {string.Join(", ", tenancy.Value.Currencies)}.");
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
                var copied = configuration.Groups.Single(g => g.Id == template);
                var group = copied with { Id = GroupIdFor(id!, template), Currency = currency ?? copied.Currency };
                var events = await engine.SendAsync(t => new CreateGroup(t, group), cancellationToken);
                if (events.Select(e => e.Event).OfType<InputRejected>().FirstOrDefault() is { Reason: not RejectReason.DuplicateId } rejected)
                {
                    throw new InvalidOperationException($"The engine rejected group {group.Id}: {rejected.Reason}.");
                }

                groups.Add(await engine.QueryAsync(e => e.GetGroup(group.Id)!, cancellationToken));
            }

            var apiKey = CreateApiKey();
            var now = time.GetUtcNow();
            var tenant = new Tenant(
                id!,
                trimmedName,
                [.. groups.Select(g => g.Id)],
                HashApiKey(apiKey),
                maker.PartnerId,
                Listed: false,
                CreatedAt: now,
                CreatedBy: maker.StaffEmail,
                Terminal: kind is { } chosen ? TerminalProfile.Default(chosen, passwordLogin: true) : null);
            if (!await store.CreateAsync(tenant, now, cancellationToken))
            {
                return new ProvisioningResult.Taken();
            }

            tenants.Put(tenant);
            var detail = maker.Detail(new() { ["name"] = tenant.Name, ["currency"] = groups.Count > 0 ? groups[0].Currency : "" });
            if (kind is { } made)
            {
                detail["kind"] = made.ToString();
            }

            await events.RecordAsync(PlatformEventKind.ServerCreated, tenant.Id, maker.StaffEmail, detail);
            return new ProvisioningResult.Created(tenant, groups, apiKey);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Who sets the firm's kind of business: the partner that made it, which keeps its terminal as it needs, the
    /// configuration when it sets the terminal at every start, or else our staff.
    /// </summary>
    public TerminalSetBy TerminalSetBy(Tenant tenant) =>
        tenant.PartnerId is not null ? Tenancy.TerminalSetBy.Partner
        : tenant.IsConfigured && _configuredTerminals.Value.Contains(tenant.Id) ? Tenancy.TerminalSetBy.Configuration
        : Tenancy.TerminalSetBy.Staff;

    /// <summary>
    /// Makes the firm another kind of business: its terminal takes the kind's words, parts and whether orders ask first
    /// the next time it opens, and the rest of the profile stays. Written to the platform's log.
    /// </summary>
    public async Task<Tenant> SetTerminalKindAsync(Tenant tenant, TerminalKind kind, TenantMaker by, CancellationToken cancellationToken)
    {
        var current = tenants.ById(tenant.Id) ?? tenant;
        if (current.Profile.Kind == kind)
        {
            return current;
        }

        var profile = current.Profile.WithKind(kind);
        await store.SetTerminalProfileAsync(current.Id, profile, cancellationToken);
        var changed = current with { Terminal = profile };
        tenants.Put(changed);
        await events.RecordAsync(
            PlatformEventKind.TerminalKindChanged,
            current.Id,
            by.StaffEmail,
            by.Detail(new() { ["kind"] = kind.ToString(), ["from"] = current.Profile.Kind.ToString() }));
        return changed;
    }

    /// <summary>
    /// Gives the firm a new admin API key. The old one stops working at once. <paramref name="reason"/> is why our staff
    /// replaced it, for the platform's log.
    /// </summary>
    public async Task<string> ReplaceAdminApiKeyAsync(Tenant tenant, TenantMaker by, string? reason, CancellationToken cancellationToken)
    {
        string apiKey;
        await _lock.WaitAsync(cancellationToken);
        try
        {
            apiKey = CreateApiKey();
            var hash = HashApiKey(apiKey);
            await store.SetAdminApiKeyAsync(tenant.Id, hash, cancellationToken);
            tenants.Put((tenants.ById(tenant.Id) ?? tenant) with { AdminApiKeyHash = hash });
        }
        finally
        {
            _lock.Release();
        }

        var detail = by.Detail([]);
        if (reason is not null)
        {
            detail["reason"] = reason;
        }

        await events.RecordAsync(PlatformEventKind.AdminKeyReplaced, tenant.Id, by.StaffEmail, detail);
        return apiKey;
    }

    /// <summary>Lists the firm's server or takes it off the list, and sets where its traders log in and the firm's logo.</summary>
    public async Task<Tenant> SetListingAsync(Tenant tenant, bool listed, Uri? loginUrl, Uri? logoUrl, TenantMaker by, CancellationToken cancellationToken)
    {
        Tenant before;
        Tenant changed;
        await _lock.WaitAsync(cancellationToken);
        try
        {
            before = tenants.ById(tenant.Id) ?? tenant;
            await store.SetListingAsync(tenant.Id, listed, loginUrl, logoUrl, cancellationToken);
            changed = before with { Listed = listed, LoginUrl = loginUrl, LogoUrl = logoUrl };
            tenants.Put(changed);
        }
        finally
        {
            _lock.Release();
        }

        if (before.Listed != listed)
        {
            await events.RecordAsync(listed ? PlatformEventKind.ServerListed : PlatformEventKind.ServerUnlisted, tenant.Id, by.StaffEmail, by.Detail([]));
        }

        return changed;
    }

    public void Dispose() => _lock.Dispose();

    private IEnumerable<string> GroupIdsFor(string id) => tenancy.Value.NewTenantGroups.Select(template => GroupIdFor(id, template));

    private bool IsConfiguredGroup(string groupId) => configuration.Groups.Any(g => g.Id == groupId);

    private static string GroupIdFor(string id, string template) => $"{id}-{template}";

    /// <summary>256 random bits, safe in a header.</summary>
    private static string CreateApiKey() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static byte[] HashApiKey(string apiKey) => SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
}

/// <summary>Who makes or changes a firm: a partner, such as our prop platform, or one of our staff (ADR 0057).</summary>
internal sealed record TenantMaker(string? PartnerId, string? PartnerName, string? StaffEmail)
{
    public static TenantMaker Of(Partner partner) => new(partner.Id, partner.Name, null);

    public static TenantMaker OfStaff(string email) => new(null, null, email);

    /// <summary>The detail for the platform's log, with the partner's name when a partner did it.</summary>
    public Dictionary<string, string> Detail(Dictionary<string, string> detail)
    {
        if (PartnerName is not null)
        {
            detail["partner"] = PartnerName;
        }

        return detail;
    }
}

internal abstract record ProvisioningResult
{
    public sealed record Created(Tenant Tenant, IReadOnlyList<TradingGroup> Groups, string AdminApiKey) : ProvisioningResult;

    /// <summary>The server, or one of the groups it would get, is already taken.</summary>
    public sealed record Taken : ProvisioningResult;

    public sealed record Invalid(string Problem) : ProvisioningResult;

    public sealed record InvalidCurrency(string Problem) : ProvisioningResult;
}
