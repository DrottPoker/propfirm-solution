using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Prop.Api.Firms;
using Prop.Api.Payments;
using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>Slots: how many challenges a firm can have open at once, and the orders that hold a slot while the buyer pays.</summary>
public sealed class SlotTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly Dictionary<string, string> TwoSlots = new() { ["Firms:0:Slots"] = "2" };

    private static readonly Dictionary<string, string> OneSlot = new() { ["Firms:0:Slots"] = "1" };

    [Fact]
    public async Task AConfiguredFirmWithoutSlotsHasNoLimit()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        await StartAsync(factory, "anna@test.example");

        var slots = await SlotsAsync(factory);

        Assert.Equal(("Unlimited", JsonValueKind.Null, 1, JsonValueKind.Null, true), (
            slots.GetProperty("limit").GetString(),
            slots.GetProperty("slots").ValueKind,
            slots.GetProperty("used").GetInt32(),
            slots.GetProperty("free").ValueKind,
            slots.GetProperty("paid").GetBoolean()));
    }

    [Fact]
    public async Task NoChallengeStartsWhenEverySlotIsTakenUntilOneEnds()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), TwoSlots);
        var first = await StartAsync(factory, "anna@test.example");
        await StartAsync(factory, "bert@test.example");

        using var full = await StartResponseAsync(factory, "cecilia@test.example");
        var slots = await SlotsAsync(factory);
        using var firm = factory.CreateFirmClient();
        using var cancelled = await firm.PostAsJsonAsync(new Uri($"/api/firm/v1/accounts/{first}/cancel", UriKind.Relative), new { reason = "Refunded" }, TestContext.Current.CancellationToken);
        using var afterCancel = await StartResponseAsync(factory, "cecilia@test.example");

        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        Assert.StartsWith("Every slot is taken.", await TitleAsync(full), StringComparison.Ordinal);
        Assert.Equal((2, 2, 0, true), (slots.GetProperty("slots").GetInt32(), slots.GetProperty("used").GetInt32(), slots.GetProperty("free").GetInt32(), slots.GetProperty("warning").GetBoolean()));
        Assert.Equal(HttpStatusCode.Created, afterCancel.StatusCode);
    }

    [Fact]
    public async Task AnOrderWaitingForPaymentHoldsTheLastSlotForItsBuyer()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), OneSlot);
        using var buyer = factory.CreatePortalClient();
        var (orderId, token) = await OrderAsync(buyer, "buyer@test.example");

        using var start = await StartResponseAsync(factory, "anna@test.example");
        var shop = await buyer.GetFromJsonAsync<JsonElement>(Url("shop"), TestContext.Current.CancellationToken);
        using var secondOrder = await buyer.PostAsJsonAsync(Url("orders"), new { challengeId = "two-step-100k", email = "other@test.example", acceptTerms = true }, TestContext.Current.CancellationToken);
        var slots = await SlotsAsync(factory);
        using var paid = await buyer.PostAsJsonAsync(Url($"orders/{orderId}/test-payment"), new { token }, TestContext.Current.CancellationToken);
        var order = await paid.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, start.StatusCode);
        Assert.Equal((false, true), (shop.GetProperty("open").GetBoolean(), shop.GetProperty("full").GetBoolean()));
        Assert.Equal(HttpStatusCode.Conflict, secondOrder.StatusCode);
        Assert.Equal((0, 1, 0), (slots.GetProperty("used").GetInt32(), slots.GetProperty("reserved").GetInt32(), slots.GetProperty("free").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal(JsonValueKind.String, order.GetProperty("accountId").ValueKind);
        Assert.Equal((1, 0), ((await SlotsAsync(factory)).GetProperty("used").GetInt32(), (await SlotsAsync(factory)).GetProperty("reserved").GetInt32()));
    }

    [Fact]
    public async Task AnOrderThatExpiresGivesItsSlotBack()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), OneSlot);
        using var buyer = factory.CreatePortalClient();
        await OrderAsync(buyer, "buyer@test.example");

        await factory.AdvanceAsync(TimeSpan.FromHours(1));
        using var start = await StartResponseAsync(factory, "anna@test.example");

        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
    }

    [Fact]
    public async Task APaymentThatComesWhenEverySlotIsTakenIsPaidWithoutAnAccount()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), OneSlot);
        using var buyer = factory.CreatePortalClient();
        var (orderId, _) = await OrderAsync(buyer, "buyer@test.example");

        // The order expires, a challenge takes the slot, and then the payment comes after all.
        await factory.AdvanceAsync(TimeSpan.FromHours(1));
        await StartAsync(factory, "anna@test.example");
        using var firm = factory.CreateFirmClient();
        var order = await firm.GetFromJsonAsync<JsonElement>(new Uri($"/api/firm/v1/orders/{orderId}", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal("Expired", order.GetProperty("order").GetProperty("status").GetString());
        await PayAsExternalAsync(factory, orderId);

        var paid = await firm.GetFromJsonAsync<JsonElement>(new Uri($"/api/firm/v1/orders/{orderId}", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(("Paid", JsonValueKind.Null), (paid.GetProperty("order").GetProperty("status").GetString(), paid.GetProperty("order").GetProperty("accountId").ValueKind));
        Assert.StartsWith("Every slot was taken when the payment came", paid.GetProperty("order").GetProperty("problem").GetString(), StringComparison.Ordinal);
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static async Task<JsonElement> SlotsAsync(PropFactory factory)
    {
        using var firm = factory.CreateFirmClient();
        return await firm.GetFromJsonAsync<JsonElement>(new Uri("/api/firm/v1/slots", UriKind.Relative), TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> StartResponseAsync(PropFactory factory, string email)
    {
        using var firm = factory.CreateFirmClient();
        return await firm.PostAsJsonAsync(new Uri("/api/firm/v1/accounts", UriKind.Relative), new { email, challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> StartAsync(PropFactory factory, string email)
    {
        using var response = await StartResponseAsync(factory, email);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
    }

    private static async Task<(Guid OrderId, string Token)> OrderAsync(HttpClient buyer, string email)
    {
        using var response = await buyer.PostAsJsonAsync(Url("orders"), new { challengeId = "two-step-100k", email, acceptTerms = true }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return (created.GetProperty("orderId").GetGuid(), created.GetProperty("checkoutUrl").GetString()!.Split("token=")[1]);
    }

    // A test order's payment that arrives late, as a provider's message would. The buyer's test page refuses expired orders.
    private static async Task PayAsExternalAsync(PropFactory factory, Guid orderId)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<OrderService>();
        var firm = scope.ServiceProvider.GetRequiredService<FirmCatalog>().ById("demo-firm")!;
        await service.MarkPaidAsync(firm, orderId, new PaymentConfirmation(PaymentProvider.Test, "test", null, null, null, null), TestContext.Current.CancellationToken);
    }

    private static async Task<string> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("title").GetString()!;
}
