using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

using Trading.Engine;
using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Service.Engine;
using Trading.Service.Identity;
using Trading.Service.Tenancy;

namespace Trading.Service.Api;

/// <summary>
/// For the firm's own systems, such as the prop platform: create traders and accounts, set equity floors
/// and close accounts. Every request carries the firm's API key and can only reach the firm's own groups.
/// </summary>
internal static class AdminEndpoints
{
    public const int MinimumPasswordLength = 10;

    public static IEndpointRouteBuilder MapAdminApi(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Admin").AddEndpointFilter<AdminApiKeyFilter>();
        admin.MapPost("/users", CreateUserAsync);
        admin.MapPost("/accounts", CreateAccountAsync);
        admin.MapPut("/accounts/{accountId}/floors/{floorId}", SetFloorAsync);
        admin.MapDelete("/accounts/{accountId}/floors/{floorId}", RemoveFloorAsync);
        admin.MapPost("/accounts/{accountId}/close", CloseAccountAsync);
        return app;
    }

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> CreateUserAsync(
        CreateUserRequest request,
        HttpContext context,
        IUserStore users,
        IPasswordHasher<User> hasher,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@', StringComparison.Ordinal))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "A valid email address is required.");
        }

        if (request.Password is not { Length: >= MinimumPasswordLength })
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, $"The password needs at least {MinimumPasswordLength} characters.");
        }

        var tenant = AdminApiKeyFilter.TenantOf(context);
        var user = await users.CreateAsync(tenant.Id, request.Email, hasher.HashPassword(null!, request.Password), cancellationToken);
        return user is null
            ? Problem(StatusCodes.Status409Conflict, "The email address is already used at this firm.")
            : TypedResults.Ok(new UserResponse(user.Id, user.Email));
    }

    // Ownership is recorded first and removed again if the engine rejects the account, so an account never exists without an owner.
    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> CreateAccountAsync(
        CreateAccountRequest request,
        HttpContext context,
        IUserStore users,
        EngineHost engine,
        CancellationToken cancellationToken)
    {
        var tenant = AdminApiKeyFilter.TenantOf(context);
        if (!tenant.Groups.Contains(request.GroupId, StringComparer.Ordinal))
        {
            return Problem(StatusCodes.Status404NotFound, "The firm has no such group.");
        }

        if (await users.FindByIdAsync(request.OwnerUserId, cancellationToken) is not { } owner || owner.TenantId != tenant.Id)
        {
            return Problem(StatusCodes.Status404NotFound, "The firm has no such user.");
        }

        if (string.IsNullOrEmpty(request.AccountId))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "An account id is required.", RejectReason.InvalidId.ToString());
        }

        if (!await users.AddAccountAsync(owner.Id, request.AccountId, cancellationToken))
        {
            return Problem(StatusCodes.Status409Conflict, "The account already exists.", RejectReason.DuplicateId.ToString());
        }

        var events = await engine.SendAsync(t => new CreateAccount(t, request.AccountId, request.GroupId, request.InitialBalance), cancellationToken);
        if (events.Any(e => e.Event is InputRejected))
        {
            await users.RemoveAccountAsync(request.AccountId, CancellationToken.None);
        }

        return CommandResults.From(events);
    }

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> SetFloorAsync(
        string accountId,
        string floorId,
        SetFloorRequest request,
        HttpContext context,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        await IsFirmAccountAsync(context, engine, accountId, cancellationToken)
            ? CommandResults.From(await engine.SendAsync(t => new SetEquityFloor(t, accountId, floorId, request.Rule), cancellationToken))
            : UnknownAccount();

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> RemoveFloorAsync(
        string accountId,
        string floorId,
        HttpContext context,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        await IsFirmAccountAsync(context, engine, accountId, cancellationToken)
            ? CommandResults.From(await engine.SendAsync(t => new RemoveEquityFloor(t, accountId, floorId), cancellationToken))
            : UnknownAccount();

    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> CloseAccountAsync(
        string accountId,
        HttpContext context,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        await IsFirmAccountAsync(context, engine, accountId, cancellationToken)
            ? CommandResults.From(await engine.SendAsync(t => new CloseAccount(t, accountId), cancellationToken))
            : UnknownAccount();

    // Accounts of other firms look like they do not exist.
    private static async Task<bool> IsFirmAccountAsync(HttpContext context, EngineHost engine, string accountId, CancellationToken cancellationToken)
    {
        var groupId = await engine.QueryAsync(e => e.GetAccount(accountId)?.GroupId, cancellationToken);
        return groupId is not null && AdminApiKeyFilter.TenantOf(context).Groups.Contains(groupId, StringComparer.Ordinal);
    }

    private static ProblemHttpResult UnknownAccount() =>
        Problem(StatusCodes.Status404NotFound, RejectReason.UnknownAccount.ToString(), RejectReason.UnknownAccount.ToString());

    private static ProblemHttpResult Problem(int statusCode, string title, string? reason = null) =>
        TypedResults.Problem(
            statusCode: statusCode,
            title: title,
            extensions: reason is null ? null : new Dictionary<string, object?> { [CommandResults.ReasonExtension] = reason });
}
