namespace Trading.Service.Realtime;

/// <summary>Which accounts and groups have connected clients, so only those are published.</summary>
internal sealed class SubscriptionRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, HashSet<(string AccountId, string GroupId)>> _byConnection = new(StringComparer.Ordinal);

    public void Add(string connectionId, string accountId, string groupId)
    {
        lock (_lock)
        {
            if (!_byConnection.TryGetValue(connectionId, out var subscriptions))
            {
                _byConnection[connectionId] = subscriptions = [];
            }

            subscriptions.Add((accountId, groupId));
        }
    }

    public void Remove(string connectionId)
    {
        lock (_lock)
        {
            _byConnection.Remove(connectionId);
        }
    }

    public IReadOnlyList<string> Accounts()
    {
        lock (_lock)
        {
            return _byConnection.Values.SelectMany(s => s).Select(s => s.AccountId).Distinct(StringComparer.Ordinal).ToList();
        }
    }

    public IReadOnlyList<string> Groups()
    {
        lock (_lock)
        {
            return _byConnection.Values.SelectMany(s => s).Select(s => s.GroupId).Distinct(StringComparer.Ordinal).ToList();
        }
    }
}
