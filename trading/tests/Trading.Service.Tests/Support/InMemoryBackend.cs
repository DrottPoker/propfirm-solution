using System.Xml.Linq;

using Microsoft.AspNetCore.DataProtection.Repositories;

using Trading.Service.Identity;

namespace Trading.Service.Tests.Support;

/// <summary>Everything the service stores, in memory. Share it between two factories to restart the service.</summary>
internal sealed class InMemoryBackend
{
    public InMemoryJournal Journal { get; init; } = new();

    public InMemoryUserStore Users { get; init; } = new();

    public InMemoryXmlRepository Keys { get; init; } = new();

    /// <summary>What a crash leaves behind: the stored journal, users and keys.</summary>
    public InMemoryBackend Crashed() => new() { Journal = Journal.Clone(), Users = Users, Keys = Keys };
}

internal sealed class InMemoryUserStore : IUserStore
{
    private readonly Lock _lock = new();
    private readonly List<User> _users = [];
    private readonly Dictionary<string, Guid> _owners = new(StringComparer.Ordinal);

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
