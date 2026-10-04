using System.Xml.Linq;

using Microsoft.AspNetCore.DataProtection.Repositories;

using Trading.Service.Identity;
using Trading.Service.Tenancy;

namespace Trading.Service.Tests.Support;

/// <summary>Everything the service stores, in memory. Share it between two factories to restart the service.</summary>
internal sealed class InMemoryBackend
{
    public InMemoryJournal Journal { get; init; } = new();

    public InMemoryUserStore Users { get; init; } = new();

    public InMemoryXmlRepository Keys { get; init; } = new();

    public InMemoryLoginLinkStore LoginLinks { get; init; } = new();

    public InMemoryTenantStore Tenants { get; init; } = new();

    /// <summary>What a crash leaves behind: the stored journal, users, keys, login links and firms.</summary>
    public InMemoryBackend Crashed() => new() { Journal = Journal.Clone(), Users = Users, Keys = Keys, LoginLinks = LoginLinks, Tenants = Tenants };
}

internal sealed class InMemoryTenantStore : ITenantStore
{
    private readonly Lock _lock = new();
    private readonly List<Tenant> _tenants = [];

    public Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<Tenant>>([.. _tenants.OrderBy(t => t.Id, StringComparer.Ordinal)]);
        }
    }

    public Task SaveConfiguredAsync(Tenant tenant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_tenants.Find(t => t.Id == tenant.Id) is { PartnerId: not null })
            {
                throw new InvalidOperationException($"Invalid tenant configuration: tenant {tenant.Id} was created by a partner.");
            }

            if (_tenants.Any(t => t.Id != tenant.Id && t.Groups.Intersect(tenant.Groups, StringComparer.Ordinal).Any()))
            {
                throw new InvalidOperationException($"Invalid tenant configuration: a group of tenant {tenant.Id} belongs to another tenant.");
            }

            _tenants.RemoveAll(t => t.Id == tenant.Id);
            _tenants.Add(tenant);
            return Task.CompletedTask;
        }
    }

    public Task<bool> CreateAsync(Tenant tenant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_tenants.Any(t => t.Id == tenant.Id || t.Groups.Intersect(tenant.Groups, StringComparer.Ordinal).Any()))
            {
                return Task.FromResult(false);
            }

            _tenants.Add(tenant);
            return Task.FromResult(true);
        }
    }

    public Task SetAdminApiKeyAsync(string tenantId, byte[] adminApiKeyHash, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var index = _tenants.FindIndex(t => t.Id == tenantId);
            _tenants[index] = _tenants[index] with { AdminApiKeyHash = adminApiKeyHash };
            return Task.CompletedTask;
        }
    }

    public Task SetListingAsync(string tenantId, bool listed, Uri? loginUrl, Uri? logoUrl, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var index = _tenants.FindIndex(t => t.Id == tenantId);
            _tenants[index] = _tenants[index] with { Listed = listed, LoginUrl = loginUrl, LogoUrl = logoUrl };
            return Task.CompletedTask;
        }
    }
}

internal sealed class InMemoryUserStore : IUserStore
{
    private readonly Lock _lock = new();
    private readonly List<User> _users = [];
    private readonly Dictionary<string, Guid> _owners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AccountDetails> _details = new(StringComparer.Ordinal);

    public Task<User?> CreateAsync(string tenantId, string email, string passwordHash, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (Find(tenantId, email) is not null)
            {
                return Task.FromResult<User?>(null);
            }

            var user = new User(Guid.CreateVersion7(), tenantId, email.Trim(), passwordHash);
            _users.Add(user);
            return Task.FromResult<User?>(user);
        }
    }

    public Task<User?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(Find(tenantId, email));
        }
    }

    public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_users.Find(u => u.Id == userId));
        }
    }

    public Task<bool> SetPasswordHashAsync(Guid userId, string passwordHash, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var index = _users.FindIndex(u => u.Id == userId);
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            _users[index] = _users[index] with { PasswordHash = passwordHash };
            return Task.FromResult(true);
        }
    }

    public Task<bool> AddAccountAsync(Guid userId, string accountId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_owners.TryAdd(accountId, userId));
        }
    }

    public Task RemoveAccountAsync(string accountId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _owners.Remove(accountId);
            return Task.CompletedTask;
        }
    }

    public Task<bool> OwnsAsync(Guid userId, string accountId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_owners.TryGetValue(accountId, out var owner) && owner == userId);
        }
    }

    public Task<IReadOnlyList<string>> AccountsOfAsync(Guid userId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<string> accounts = _owners.Where(o => o.Value == userId).Select(o => o.Key).Order(StringComparer.Ordinal).ToList();
            return Task.FromResult(accounts);
        }
    }

    public Task SetAccountDetailsAsync(AccountDetails details, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _details[details.AccountId] = details;
            return Task.CompletedTask;
        }
    }

    public async Task<IReadOnlyList<AccountDetails>> AccountDetailsOfAsync(Guid userId, CancellationToken cancellationToken)
    {
        var accounts = await AccountsOfAsync(userId, cancellationToken);
        lock (_lock)
        {
            return [.. accounts.Select(a => _details.TryGetValue(a, out var details) ? details : new AccountDetails(a, null, null, null, null))];
        }
    }

    private User? Find(string tenantId, string email) =>
        _users.Find(u => u.TenantId == tenantId && Emails.Normalize(u.Email) == Emails.Normalize(email));
}

internal sealed class InMemoryXmlRepository : IXmlRepository
{
    private readonly Lock _lock = new();
    private readonly List<XElement> _elements = [];

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (_lock)
        {
            return _elements.Select(e => new XElement(e)).ToList();
        }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        lock (_lock)
        {
            _elements.Add(new XElement(element));
        }
    }
}

internal sealed class InMemoryLoginLinkStore : ILoginLinkStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, (Guid UserId, string? AccountId, DateTimeOffset ExpiresAt, bool Used)> _links = new(StringComparer.Ordinal);

    public Task CreateAsync(byte[] tokenHash, Guid userId, string? accountId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _links.Add(Convert.ToHexString(tokenHash), (userId, accountId, expiresAt, false));
            return Task.CompletedTask;
        }
    }

    public Task<LoginLink?> UseAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var key = Convert.ToHexString(tokenHash);
            if (!_links.TryGetValue(key, out var link) || link.Used || link.ExpiresAt <= now)
            {
                return Task.FromResult<LoginLink?>(null);
            }

            _links[key] = link with { Used = true };
            return Task.FromResult<LoginLink?>(new LoginLink(link.UserId, link.AccountId));
        }
    }
}
