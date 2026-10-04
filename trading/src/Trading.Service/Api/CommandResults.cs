using Microsoft.AspNetCore.Http.HttpResults;

using Trading.Engine;
using Trading.Engine.Events;
using Trading.Service.Engine;

namespace Trading.Service.Api;

/// <summary>Maps command events to HTTP results. A rejection becomes a problem response with the reason.</summary>
internal static class CommandResults
{
    public const string ReasonExtension = "reason";

    /// <summary>
    /// The command's events, or a problem when it was rejected. A rejection for <paramref name="alreadyDone"/> means
    /// the account is already as the command wants it, which answers with no events, so the command is safe to repeat.
    /// </summary>
    public static Results<Ok<CommandResponse>, ProblemHttpResult> From(IReadOnlyList<EventEnvelope> events, RejectReason? alreadyDone = null)
    {
        if (events.Select(e => e.Event).OfType<InputRejected>().FirstOrDefault() is not { } rejected)
        {
            return TypedResults.Ok(new CommandResponse(events));
        }

        if (rejected.Reason == alreadyDone)
        {
            return TypedResults.Ok(new CommandResponse([]));
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
        RejectReason.DuplicateId or RejectReason.SymbolInUse => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status422UnprocessableEntity,
    };
}
