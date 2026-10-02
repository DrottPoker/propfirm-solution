using Microsoft.AspNetCore.Http.HttpResults;

using Trading.Engine.Inputs;
using Trading.Service.Engine;

namespace Trading.Service.Api;

/// <summary>
/// Account management for the firm's systems: create accounts, set equity floors and close accounts.
/// Moves behind the internal API to the prop platform (ADR 0004) when authentication is in place.
/// </summary>
internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminApi(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/api/admin/accounts").WithTags("Admin");
        accounts.MapPost("", CreateAccountAsync);
        accounts.MapPut("/{accountId}/floors/{floorId}", SetFloorAsync);
        accounts.MapDelete("/{accountId}/floors/{floorId}", RemoveFloorAsync);
        accounts.MapPost("/{accountId}/close", CloseAccountAsync);
        return app;
    }

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> CreateAccountAsync(
        CreateAccountRequest request,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        CommandResults.From(await engine.SendAsync(
            t => new CreateAccount(t, request.AccountId, request.GroupId, request.InitialBalance),
            cancellationToken));

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> SetFloorAsync(
        string accountId,
        string floorId,
        SetFloorRequest request,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        CommandResults.From(await engine.SendAsync(t => new SetEquityFloor(t, accountId, floorId, request.Rule), cancellationToken));

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> RemoveFloorAsync(
        string accountId,
        string floorId,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        CommandResults.From(await engine.SendAsync(t => new RemoveEquityFloor(t, accountId, floorId), cancellationToken));

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> CloseAccountAsync(
        string accountId,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        CommandResults.From(await engine.SendAsync(t => new CloseAccount(t, accountId), cancellationToken));
}
