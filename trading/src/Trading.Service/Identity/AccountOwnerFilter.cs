namespace Trading.Service.Identity;

/// <summary>
/// Lets a request through only if the logged in trader owns the account in the route. Other accounts
/// get 404, so a trader cannot tell whether an account id exists.
/// </summary>
internal sealed class AccountOwnerFilter(IUserStore users) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (CurrentUser.IdOf(http.User) is not { } userId
            || http.GetRouteValue("accountId") is not string accountId
            || !await users.OwnsAsync(userId, accountId, http.RequestAborted))
        {
            return TypedResults.NotFound();
        }

        return await next(context);
    }
}
