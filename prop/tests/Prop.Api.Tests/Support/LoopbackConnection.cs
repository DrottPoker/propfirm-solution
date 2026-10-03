using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Prop.Api.Tests.Support;

/// <summary>
/// The test server leaves the caller's address empty. Requests here come from loopback instead, like from a
/// portal on the same machine, so its forwarded headers are trusted as in development.
/// </summary>
internal sealed class LoopbackConnection : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress ??= IPAddress.Loopback;
            return nextMiddleware(context);
        });
        next(app);
    };
}
