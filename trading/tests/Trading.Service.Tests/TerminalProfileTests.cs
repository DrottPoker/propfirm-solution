using System.Net;
using System.Text.Json.Nodes;

using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>How each server's terminal works for its traders, set by the firm (ADR 0058), and the trader's name.</summary>
public sealed class TerminalProfileTests
{
    private static readonly object Broker = new
    {
        kind = "Broker",
        modules = new { rulebook = false, ownLimits = true, riskSizing = true, tradeDetails = true, breachReports = false },
        confirmOrders = true,
        startingSize = new { kind = "Lots", value = 0.1m },
        passwordLogin = true,
        links = new
        {
            help = "https://broker.example.com/help",
            support = "https://broker.example.com/support",
            terms = "https://broker.example.com/terms",
            privacy = "https://broker.example.com/privacy",
            passwordReset = "https://broker.example.com/reset",
        },
        riskWarning = "  CFDs are complex instruments and come with a high risk of losing money rapidly.  ",
    };

    [Fact]
    public async Task AServerStartsAsAPropFirmWithEveryPartAndPasswordLogin()
    {
        using var factory = new ServiceFactory();
        using var admin = factory.CreateAdminClient();

        var profile = await admin.GetJsonAsync("/api/admin/v1/terminal-profile");

        Assert.Equal(("Prop", true, false, "Smallest"), (
            profile.GetProperty("kind").GetString(),
            profile.GetProperty("passwordLogin").GetBoolean(),
            profile.GetProperty("confirmOrders").GetBoolean(),
            profile.GetProperty("startingSize").GetProperty("kind").GetString()));
        Assert.All(profile.GetProperty("modules").EnumerateObject(), module => Assert.True(module.Value.GetBoolean()));
    }

    [Fact]
    public async Task TheFirmSetsItsProfileAndEveryTerminalAndTheLoginSeeIt()
    {
        var backend = new InMemoryBackend();
        using (var factory = new ServiceFactory(backend))
        {
            using var admin = factory.CreateAdminClient();
            var saved = await admin.PutJsonAsync("/api/admin/v1/terminal-profile", Broker);
            Assert.Equal("CFDs are complex instruments and come with a high risk of losing money rapidly.", saved.GetProperty("riskWarning").GetString());
        }

        // Kept over a restart, and part of the server's public facts and of the trader's login.
        using var restarted = new ServiceFactory(backend);
        using var trader = await restarted.CreateTraderClientAsync("T1");
        var server = await restarted.CreateClient().GetJsonAsync($"/api/servers/{ServiceFactory.DemoServer}");
        var me = await trader.GetJsonAsync("/api/auth/me");

        foreach (var profile in new[] { server.GetProperty("profile"), me.GetProperty("server").GetProperty("profile") })
        {
            Assert.Equal(("Broker", true, 0.1m), (profile.GetProperty("kind").GetString(), profile.GetProperty("confirmOrders").GetBoolean(), profile.GetProperty("startingSize").GetProperty("value").GetDecimal()));
            Assert.False(profile.GetProperty("modules").GetProperty("rulebook").GetBoolean());
            Assert.Equal("https://broker.example.com/reset", profile.GetProperty("links").GetProperty("passwordReset").GetString());
        }
    }

    [Theory]
    [InlineData("links.help", "javascript:alert(1)")]
    [InlineData("startingSize", """{ "kind": "Lots", "value": 0 }""")]
    [InlineData("startingSize", """{ "kind": "Smallest", "value": 1 }""")]
    [InlineData("riskWarning", "   ")]
    public async Task AProfileThatCannotBeUsedIsRefused(string path, string value)
    {
        using var factory = new ServiceFactory();
        using var admin = factory.CreateAdminClient();
        var profile = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(Broker))!;
        var parts = path.Split('.');
        var parent = parts.Length == 1 ? profile : profile[parts[0]]!;
        parent[parts[^1]] = value.StartsWith('{') ? JsonNode.Parse(value) : JsonValue.Create(value);

        await admin.PutJsonAsync("/api/admin/v1/terminal-profile", profile, HttpStatusCode.UnprocessableEntity);
    }

    // Sizing from the room needs the order panel's risk sizing.
    [Fact]
    public async Task AStartingSizeFromTheRoomNeedsRiskSizing()
    {
        using var factory = new ServiceFactory();
        using var admin = factory.CreateAdminClient();
        var profile = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(Broker))!;
        profile["startingSize"] = JsonNode.Parse("""{ "kind": "RiskOfRoom", "value": 0.5 }""");
        profile["modules"]!["riskSizing"] = false;

        var problem = await admin.PutJsonAsync("/api/admin/v1/terminal-profile", profile, HttpStatusCode.UnprocessableEntity);

        Assert.Contains("risk sizing", problem.GetProperty("title").GetString(), StringComparison.Ordinal);
    }

    // A configured firm can be told its kind, here a broker whose orders ask first.
    [Fact]
    public async Task AConfiguredFirmTakesItsKindFromTheConfiguration()
    {
        using var factory = new ServiceFactory(settings: new Dictionary<string, string> { ["Tenants:0:Terminal:Kind"] = "Broker", ["Tenants:0:Terminal:PasswordLogin"] = "true" });

        var server = await factory.CreateClient().GetJsonAsync($"/api/servers/{ServiceFactory.DemoServer}");

        Assert.Equal(("Broker", true), (server.GetProperty("profile").GetProperty("kind").GetString(), server.GetProperty("profile").GetProperty("confirmOrders").GetBoolean()));
    }

    // The terminal shows the trader's name from the firm, for example in its menu and initials, and the email without it.
    [Fact]
    public async Task TheFirmTellsTheTradersName()
    {
        using var factory = new ServiceFactory();
        using var trader = await factory.CreateTraderClientAsync("T1");
        var userId = (await trader.GetJsonAsync("/api/auth/me")).GetProperty("userId").GetGuid();
        using var admin = factory.CreateAdminClient();

        await admin.PutJsonAsync($"/api/admin/v1/users/{userId}/name", new { name = "  Maja Lind " }, HttpStatusCode.NoContent);
        var named = await trader.GetJsonAsync("/api/auth/me");
        await admin.PutJsonAsync($"/api/admin/v1/users/{userId}/name", new { name = new string('x', 101) }, HttpStatusCode.UnprocessableEntity);
        await admin.PutJsonAsync($"/api/admin/v1/users/{Guid.NewGuid()}/name", new { name = "Nobody" }, HttpStatusCode.NotFound);
        await admin.PutJsonAsync($"/api/admin/v1/users/{userId}/name", new { name = "" }, HttpStatusCode.NoContent);
        var unnamed = await trader.GetJsonAsync("/api/auth/me");

        Assert.Equal("Maja Lind", named.GetProperty("name").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, unnamed.GetProperty("name").ValueKind);
    }
}
