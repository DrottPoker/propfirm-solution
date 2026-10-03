namespace Prop.Api.Firms;

/// <summary>Requests to the firm API carry the firm's key. The firm it belongs to is the only one the request may act on.</summary>
internal sealed class FirmApiKeyFilter(FirmCatalog firms) : IEndpointFilter
{
    public const string HeaderName = "X-Api-Key";

    private static readonly object FirmKey = new();

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        await firms.Ready.WaitAsync(http.RequestAborted);
        if (firms.ByApiKey(http.Request.Headers[HeaderName]) is not { } firm)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: $"A valid {HeaderName} header is required.");
        }

        http.Items[FirmKey] = firm;
        return await next(context);
    }

    public static Firm FirmOf(HttpContext context) =>
        context.Items[FirmKey] as Firm ?? throw new InvalidOperationException("The firm API key filter did not run.");
}
