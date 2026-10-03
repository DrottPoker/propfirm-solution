using System.Security.Cryptography;
using System.Text;

namespace Prop.Api.Firms;

/// <summary>
/// The firms on the prop platform, kept in memory and looked up by id, portal host and API key. Loaded from the
/// database at startup and updated by whoever changes a firm. The service runs as one instance, so nothing else
/// changes the firms in the database.
/// </summary>
internal sealed class FirmCatalog
{
    private readonly Lock _lock = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IReadOnlyList<Firm> _firms = [];
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the firms are loaded. Lookups before that find nothing.</summary>
    public Task Ready => _ready.Task;

    public IReadOnlyList<Firm> All => Volatile.Read(ref _firms);

    public Firm? ById(string firmId) => All.FirstOrDefault(f => f.Id == firmId);

    /// <summary>The firm whose portal is reached on the host name, without port.</summary>
    public Firm? ByHost(string? host) =>
        host is null ? null : All.FirstOrDefault(f => f.Portal.Hosts.Contains(host, StringComparer.OrdinalIgnoreCase));

    /// <summary>The firm the key belongs to. Compared in constant time.</summary>
    public Firm? ByApiKey(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return null;
        }

        var hash = HashApiKeyBytes(apiKey);
        return All.FirstOrDefault(f => f.ApiKeyHash is { } key && CryptographicOperations.FixedTimeEquals(hash, key));
    }

    /// <summary>The firms now, and a task that completes when they change next. Nothing is missed between the two.</summary>
    public (IReadOnlyList<Firm> Firms, Task Changed) Watch()
    {
        lock (_lock)
        {
            return (_firms, _changed.Task);
        }
    }

    public void Load(IReadOnlyList<Firm> firms)
    {
        lock (_lock)
        {
            Replace(firms);
        }

        _ready.TrySetResult();
    }

    public void LoadFailed(Exception exception) => _ready.TrySetException(exception);

    /// <summary>Adds the firm, or replaces the one with the same id.</summary>
    public void Put(Firm firm)
    {
        lock (_lock)
        {
            Replace([.. _firms.Where(f => f.Id != firm.Id), firm]);
        }
    }

    public static string HashApiKey(string apiKey) => Convert.ToHexStringLower(HashApiKeyBytes(apiKey));

    public static byte[] HashApiKeyBytes(string apiKey) => SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));

    // The caller holds the lock.
    private void Replace(IReadOnlyList<Firm> firms)
    {
        Volatile.Write(ref _firms, firms);
        var changed = _changed;
        _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        changed.TrySetResult();
    }
}
