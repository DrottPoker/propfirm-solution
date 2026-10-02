using System.Net;

using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task HealthIsOkOnceTheEngineIsReady()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
