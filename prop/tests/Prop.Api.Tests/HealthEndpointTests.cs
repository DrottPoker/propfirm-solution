using System.Net;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

public sealed class HealthEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task HealthReturnsOk()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
