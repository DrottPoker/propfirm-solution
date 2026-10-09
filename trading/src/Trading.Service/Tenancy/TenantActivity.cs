using System.Collections.Concurrent;

namespace Trading.Service.Tenancy;

/// <summary>
/// When each firm's system last used its admin key and last read its events, for the staff panel (ADR 0057). Kept in
/// memory, so after a restart nothing is known until the system calls again.
/// </summary>
internal sealed class TenantActivity(TimeProvider time)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _keyUsed = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, EventRead> _eventsRead = new(StringComparer.Ordinal);

    public void KeyUsed(string tenantId) => _keyUsed[tenantId] = time.GetUtcNow();

    /// <summary>Notes that the firm's system asked for its events after the sequence number.</summary>
    public void EventsRead(string tenantId, long after) => _eventsRead[tenantId] = new EventRead(time.GetUtcNow(), after);

    public DateTimeOffset? KeyLastUsed(string tenantId) => _keyUsed.TryGetValue(tenantId, out var at) ? at : null;

    public EventRead? LastEventRead(string tenantId) => _eventsRead.TryGetValue(tenantId, out var read) ? read : null;
}

/// <summary>When the firm's system last asked for its events, and the sequence number it asked for events after.</summary>
public sealed record EventRead(DateTimeOffset At, long After);
