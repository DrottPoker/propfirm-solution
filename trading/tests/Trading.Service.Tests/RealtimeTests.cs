using System.Text.Json;

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

public sealed class RealtimeTests
{
    [Fact]
    public async Task SubscriberReceivesTheAccountItsEventsAndNewPrices()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync("T1");
        await factory.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);

        await using var connection = factory.CreateHubConnection(await factory.LoginCookieAsync(ServiceFactory.EmailOf("T1"), ServiceFactory.TraderPassword));
        var account = Expect(connection, "Account");
        var initialPrices = Expect(connection, "Prices");
        await connection.StartAsync(cancellationToken);
        await connection.InvokeAsync("Subscribe", "T1", cancellationToken);

        Assert.Equal("T1", (await Eventually.Within(account)).GetProperty("accountId").GetString());
        Assert.Contains((await Eventually.Within(initialPrices)).EnumerateArray(), p => p.GetProperty("symbol").GetString() == "EURUSD");

        // Events are pushed as they happen
        var events = Expect(connection, "Events");
        await client.PostJsonAsync("/api/accounts/T1/orders", new { orderId = "O1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });
        Assert.Equal(["PositionOpened"], (await Eventually.Within(events)).EventKinds());

        // Prices are pushed on the next price interval
        var newPrice = Expect(connection, "Prices", prices => prices.EnumerateArray().Any(p => p.GetProperty("bid").GetDecimal() > 1.0805m));
        await factory.PushQuoteAsync("EURUSD", 1.08100m, 1.08110m);
        factory.Time.Advance(TimeSpan.FromMilliseconds(100));
        await Eventually.Within(newPrice);
    }

    [Fact]
    public async Task SubscribingToAnAccountOfSomeoneElseFails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new ServiceFactory();
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        (await factory.CreateTraderClientAsync("T2")).Dispose();
        await using var connection = factory.CreateHubConnection(await factory.LoginCookieAsync(ServiceFactory.EmailOf("T1"), ServiceFactory.TraderPassword));
        await connection.StartAsync(cancellationToken);

        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("Subscribe", "T2", cancellationToken));
    }

    [Fact]
    public async Task ConnectingWithoutLoginIsRefused()
    {
        using var factory = new ServiceFactory();
        await using var connection = factory.CreateHubConnection();

        await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync(TestContext.Current.CancellationToken));
    }

    // Completes with the first message to the method that matches the filter.
    private static Task<JsonElement> Expect(HubConnection connection, string method, Func<JsonElement, bool>? filter = null)
    {
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>(method, message =>
        {
            if (filter is null || filter(message))
            {
                received.TrySetResult(message);
            }
        });
        return received.Task;
    }
}
