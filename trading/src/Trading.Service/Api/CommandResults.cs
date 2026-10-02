using Microsoft.AspNetCore.Http.HttpResults;

using Trading.Engine;
using Trading.Engine.Events;
using Trading.Service.Engine;

namespace Trading.Service.Api;

/// <summary>Maps command events to HTTP results. A rejection becomes a problem response with the reason.</summary>
internal static class CommandResults
{
    public const string ReasonExtension = "reason";

    public static Results<Ok<CommandResponse>, ProblemHttpResult> From(IReadOnlyList<EventEnvelope> events)
    {
        if (events.Select(e => e.Event).OfType<InputRejected>().FirstOrDefault() is not { } rejected)
        {
            return TypedResults.Ok(new CommandResponse(events));
        }

        return TypedResults.Problem(
            title: "The command was rejected.",
            detail: rejected.Reason.ToString(),
            statusCode: StatusCodeFor(rejected.Reason),
            extensions: new Dictionary<string, object?> { [ReasonExtension] = rejected.Reason.ToString() });
    }

    private static int StatusCodeFor(RejectReason reason) => reason switch
    {
        RejectReason.UnknownAccount or RejectReason.UnknownOrder or RejectReason.UnknownPosition or RejectReason.UnknownFloor
            => StatusCodes.Status404NotFound,
        RejectReason.DuplicateId => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status422UnprocessableEntity,
    };
}
