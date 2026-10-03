using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;

using Trading.Engine;
using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Service.Configuration;
using Trading.Service.Engine;
using Trading.Service.Identity;
using Trading.Service.Persistence;
using Trading.Service.Tenancy;

namespace Trading.Service.Api;

/// <summary>
/// For the firm's own systems, such as the prop platform: traders, accounts, equity floors, login links and
/// the firm's events. Every request carries the firm's API key and can only reach the firm's own groups.
/// Versioned, since systems outside our control depend on it.
/// </summary>
internal static class AdminEndpoints
{
    public const int MaxEventsPerRequest = 1_000;

    public static readonly TimeSpan MaxEventWait = TimeSpan.FromSeconds(30);

    public static IEndpointRouteBuilder MapAdminApi(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/v1").WithTags("Admin").AddEndpointFilter<AdminApiKeyFilter>();
        admin.MapPost("/users", CreateUserAsync);
        admin.MapGet("/users", FindUserAsync);
        admin.MapPut("/users/{userId:guid}/password", SetPasswordAsync);
        admin.MapPost("/users/{userId:guid}/login-links", CreateLoginLinkAsync);
        admin.MapPost("/accounts", CreateAccountAsync);
        admin.MapGet("/accounts/{accountId}", GetAccountAsync);
        admin.MapPut("/accounts/{accountId}/floors/{floorId}", SetFloorAsync);
        admin.MapDelete("/accounts/{accountId}/floors/{floorId}", RemoveFloorAsync);
        admin.MapPost("/accounts/{accountId}/close", CloseAccountAsync);
        admin.MapPost("/accounts/{accountId}/balance-operations", AdjustBalanceAsync);
        admin.MapGet("/events", GetEventsAsync);
        return app;
    }

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> CreateUserAsync(
        CreateUserRequest request,
        HttpContext context,
        IUserStore users,
        IPasswordHasher<User> hasher,
        IOptions<LoginOptions> login,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@', StringComparison.Ordinal))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "A valid email address is required.");
        }

        if (PasswordProblem(request.Password, login.Value) is { } problem)
        {
            return problem;
        }

        var tenant = AdminApiKeyFilter.TenantOf(context);
        var user = await users.CreateAsync(tenant.Id, request.Email, hasher.HashPassword(null!, request.Password!), cancellationToken);
        return user is null
            ? Problem(StatusCodes.Status409Conflict, "The email address is already used at this firm.")
            : TypedResults.Ok(new UserResponse(user.Id, user.Email));
    }

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> FindUserAsync(
        string? email,
        HttpContext context,
        IUserStore users,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "An email address is required.");
        }

        return await users.FindByEmailAsync(AdminApiKeyFilter.TenantOf(context).Id, email, cancellationToken) is { } user
            ? TypedResults.Ok(new UserResponse(user.Id, user.Email))
            : UnknownUser();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SetPasswordAsync(
        Guid userId,
        SetPasswordRequest request,
        HttpContext context,
        IUserStore users,
        IPasswordHasher<User> hasher,
        IOptions<LoginOptions> login,
        CancellationToken cancellationToken)
    {
        if (PasswordProblem(request.Password, login.Value) is { } problem)
        {
            return problem;
        }

        if (await FirmUserAsync(context, users, userId, cancellationToken) is not { } user)
        {
            return UnknownUser();
        }

        await users.SetPasswordHashAsync(user.Id, hasher.HashPassword(user, request.Password!), cancellationToken);
        return TypedResults.NoContent();
    }

    // The link carries a one-time token. Only its hash is stored.
    private static async Task<Results<Ok<LoginLinkResponse>, ProblemHttpResult>> CreateLoginLinkAsync(
        Guid userId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateLoginLinkRequest? request,
        HttpContext context,
        IUserStore users,
        ILoginLinkStore links,
        TimeProvider time,
        IOptions<TerminalOptions> terminal,
        CancellationToken cancellationToken)
    {
        if (await FirmUserAsync(context, users, userId, cancellationToken) is not { } user)
        {
            return UnknownUser();
        }

        var accountId = string.IsNullOrEmpty(request?.AccountId) ? null : request.AccountId;
        if (accountId is not null && !await users.OwnsAsync(user.Id, accountId, cancellationToken))
        {
            return UnknownAccount();
        }

        var token = LoginLinkTokens.Create();
        var expiresAt = time.GetUtcNow() + LoginLinkTokens.Lifetime;
        await links.CreateAsync(LoginLinkTokens.Hash(token), user.Id, accountId, expiresAt, cancellationToken);

        var query = accountId is null ? $"token={token}" : $"token={token}&account={Uri.EscapeDataString(accountId)}";
        return TypedResults.Ok(new LoginLinkResponse(new Uri(terminal.Value.Url!, $"login/link?{query}"), expiresAt));
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

        if (await FirmUserAsync(context, users, request.OwnerUserId, cancellationToken) is not { } owner)
        {
            return UnknownUser();
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

    private static async Task<Results<Ok<AccountSnapshot>, ProblemHttpResult>> GetAccountAsync(
        string accountId,
        HttpContext context,
        EngineHost engine,
        CancellationToken cancellationToken)
    {
        var account = await engine.QueryAsync(e => e.GetAccount(accountId), cancellationToken);
        return account is not null && IsFirmGroup(context, account.GroupId) ? TypedResults.Ok(account) : UnknownAccount();
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

    /// <summary>A deposit or withdrawal, for example a trader's payout. Floors measured from the account move with the balance.</summary>
    private static async Task<Results<Ok<CommandResponse>, ProblemHttpResult>> AdjustBalanceAsync(
        string accountId,
        BalanceOperationRequest request,
        HttpContext context,
        EngineHost engine,
        CancellationToken cancellationToken) =>
        await IsFirmAccountAsync(context, engine, accountId, cancellationToken)
            ? CommandResults.From(await engine.SendAsync(
                t => new AdjustBalance(t, accountId, request.OperationId ?? "", request.Amount, request.MinBalance),
                cancellationToken))
            : UnknownAccount();

    /// <summary>
    /// The firm's events after <c>after</c>, oldest first. With <c>wait</c> seconds, the request waits for
    /// new events when there are none yet, so the firm's systems hear about them at once.
    /// </summary>
    private static async Task<Results<Ok<FirmEventsResponse>, ProblemHttpResult>> GetEventsAsync(
        HttpContext context,
        IEngineJournal journal,
        EventLog eventLog,
        TimeProvider time,
        CancellationToken cancellationToken,
        long after = 0,
        int limit = 100,
        int wait = 0)
    {
        if (after < 0 || limit is < 1 or > MaxEventsPerRequest || wait < 0 || wait > MaxEventWait.TotalSeconds)
        {
            return Problem(
                StatusCodes.Status422UnprocessableEntity,
                $"after cannot be negative, limit must be 1 to {MaxEventsPerRequest} and wait 0 to {MaxEventWait.TotalSeconds:0} seconds.");
        }

        var groups = AdminApiKeyFilter.TenantOf(context).Groups;
        var deadline = time.GetUtcNow() + TimeSpan.FromSeconds(wait);
        while (true)
        {
            var nextStored = eventLog.NextStored;
            var events = await journal.ReadGroupEventsAsync([.. groups], after, limit, cancellationToken);
            var remaining = deadline - time.GetUtcNow();
            if (events.Count > 0 || remaining <= TimeSpan.Zero)
            {
                return TypedResults.Ok(new FirmEventsResponse(events, events.Count > 0 ? events[^1].Sequence : after));
            }

            // Other firms' events also wake the request. It then reads again and waits for the rest of the time.
            try
            {
                await nextStored.WaitAsync(remaining, time, cancellationToken);
            }
            catch (TimeoutException)
            {
                return TypedResults.Ok(new FirmEventsResponse([], after));
            }
        }
    }

    // Accounts and users of other firms look like they do not exist.
    private static async Task<bool> IsFirmAccountAsync(HttpContext context, EngineHost engine, string accountId, CancellationToken cancellationToken) =>
        IsFirmGroup(context, await engine.QueryAsync(e => e.GetGroupId(accountId), cancellationToken));

    private static bool IsFirmGroup(HttpContext context, string? groupId) =>
        groupId is not null && AdminApiKeyFilter.TenantOf(context).Groups.Contains(groupId, StringComparer.Ordinal);

    private static async Task<User?> FirmUserAsync(HttpContext context, IUserStore users, Guid userId, CancellationToken cancellationToken) =>
        await users.FindByIdAsync(userId, cancellationToken) is { } user && user.TenantId == AdminApiKeyFilter.TenantOf(context).Id ? user : null;

    private static ProblemHttpResult? PasswordProblem(string? password, LoginOptions login) =>
        password is null || password.Length < login.MinimumPasswordLength
            ? Problem(
                StatusCodes.Status422UnprocessableEntity,
                login.MinimumPasswordLength == 1 ? "A password is required." : $"The password needs at least {login.MinimumPasswordLength} characters.")
            : null;

    private static ProblemHttpResult UnknownUser() => Problem(StatusCodes.Status404NotFound, "The firm has no such user.");

    private static ProblemHttpResult UnknownAccount() =>
        Problem(StatusCodes.Status404NotFound, RejectReason.UnknownAccount.ToString(), RejectReason.UnknownAccount.ToString());

    private static ProblemHttpResult Problem(int statusCode, string title, string? reason = null) =>
        TypedResults.Problem(
            statusCode: statusCode,
            title: title,
            extensions: reason is null ? null : new Dictionary<string, object?> { [CommandResults.ReasonExtension] = reason });
}
