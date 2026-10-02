using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Trading.Service.Tests;

public sealed class DevelopmentOnlyTests
{
    // The API has no authentication yet, so it must never start outside a developer machine.
    [Fact]
    public void ServiceRefusesToStartOutsideDevelopment()
    {
        using var root = new WebApplicationFactory<Program>();
        using var production = root.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var exception = Assert.ThrowsAny<Exception>(() => production.CreateClient());

        Assert.Contains("may only run in the Development environment", exception.ToString(), StringComparison.Ordinal);
    }
}
