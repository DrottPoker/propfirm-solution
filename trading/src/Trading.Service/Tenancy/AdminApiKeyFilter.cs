namespace Trading.Service.Tenancy;

/// <summary>Admin requests carry the firm's API key. The firm it belongs to is the only one the request may act on.</summary>
internal sealed class AdminApiKeyFilter(TenantCatalog tenants) : IEndpointFilter
{
    public const string HeaderName = "X-Api-Key";

    private static readonly object TenantKey = new();

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (tenants.ByAdminApiKey(http.Request.Headers[HeaderName]) is not { } tenant)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: $"A valid {HeaderName} header is required.");
        }

        http.Items[TenantKey] = tenant;
        return await next(context);
    }

    public static Tenant TenantOf(HttpContext context) =>
        context.Items[TenantKey] as Tenant ?? throw new InvalidOperationException("The admin API key filter did not run.");
}
