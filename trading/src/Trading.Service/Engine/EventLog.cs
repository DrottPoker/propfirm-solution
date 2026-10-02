using System.Threading.Channels;

using Trading.Engine.Events;
using Trading.Engine.Inputs;

namespace Trading.Service.Engine;

/// <summary>An engine event with a sequence number that orders all events in the service.</summary>
public sealed record EventEnvelope(long Sequence, EngineEvent Event);

/// <summary>
/// Hands events to realtime delivery. Only events that are already stored in the journal are published,
/// so a client never sees an event that a restart could undo.
/// </summary>
internal sealed class EventLog
{
    private readonly Channel<EventEnvelope> _published =
        Channel.CreateUnbounded<EventEnvelope>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    public ChannelReader<EventEnvelope> Published => _published.Reader;

    public void Publish(IReadOnlyList<EventEnvelope> events)
    {
        foreach (var envelope in events)
        {
            _published.Writer.TryWrite(envelope);
        }
    }

    public static string? AccountIdOf(EngineEvent engineEvent) => engineEvent switch
    {
        IAccountEvent accountEvent => accountEvent.AccountId,
        InputRejected { Input: IAccountCommand command } => command.AccountId,
        _ => null,
    };
}
