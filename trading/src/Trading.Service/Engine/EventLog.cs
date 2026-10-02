using System.Threading.Channels;

using Trading.Engine.Events;
using Trading.Engine.Inputs;

namespace Trading.Service.Engine;

/// <summary>An engine event with a sequence number that orders all events in the service.</summary>
public sealed record EventEnvelope(long Sequence, EngineEvent Event);

/// <summary>
/// Numbers engine events, keeps them per account and publishes them for realtime delivery.
/// In memory only for now.
/// </summary>
internal sealed class EventLog
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<EventEnvelope>> _byAccount = new(StringComparer.Ordinal);
    private readonly Channel<EventEnvelope> _published =
        Channel.CreateUnbounded<EventEnvelope>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private long _sequence;

    public ChannelReader<EventEnvelope> Published => _published.Reader;

    /// <summary>Called from the engine loop only.</summary>
    public IReadOnlyList<EventEnvelope> Append(IReadOnlyList<EngineEvent> events)
    {
        if (events.Count == 0)
        {
            return [];
        }

        var envelopes = new List<EventEnvelope>(events.Count);
        lock (_lock)
        {
            foreach (var engineEvent in events)
            {
                var envelope = new EventEnvelope(++_sequence, engineEvent);
                envelopes.Add(envelope);

                // Only existing accounts get a log, so unknown ids from requests cannot grow memory.
                if (AccountIdOf(engineEvent) is not { } accountId)
                {
                    continue;
                }

                if (engineEvent is AccountCreated)
                {
                    _byAccount[accountId] = [];
                }

                if (_byAccount.TryGetValue(accountId, out var accountEvents))
                {
                    accountEvents.Add(envelope);
                }
            }
        }

        foreach (var envelope in envelopes)
        {
            _published.Writer.TryWrite(envelope);
        }

        return envelopes;
    }

    /// <summary>Events for the account with a sequence number above <paramref name="after"/>, oldest first.</summary>
    public IReadOnlyList<EventEnvelope> Read(string accountId, long after, int limit)
    {
        lock (_lock)
        {
            return _byAccount.TryGetValue(accountId, out var accountEvents)
                ? accountEvents.Where(e => e.Sequence > after).Take(limit).ToList()
                : [];
        }
    }

    public static string? AccountIdOf(EngineEvent engineEvent) => engineEvent switch
    {
        IAccountEvent accountEvent => accountEvent.AccountId,
        InputRejected { Input: IAccountCommand command } => command.AccountId,
        _ => null,
    };
}
