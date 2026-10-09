using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

public sealed class AuthTests
{
    [Fact]
    public async Task MeShowsTheTraderAndTheirAccounts()
    {
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync("T1");

        var me = await client.GetJsonAsync("/api/auth/me");

        Assert.Equal(ServiceFactory.EmailOf("T1"), me.GetProperty("email").GetString());
        Assert.Equal("demo-firm", me.GetProperty("server").GetProperty("id").GetString());
        Assert.Equal("Demo Firm", me.GetProperty("server").GetProperty("name").GetString());
        Assert.Equal(["T1"], me.GetProperty("accounts").EnumerateArray().Select(a => a.GetString()));
    }

    [Theory]
    [InlineData(ServiceFactory.DemoServer, "t1@test.example", "wrong-password")]
    [InlineData(ServiceFactory.DemoServer, "nobody@test.example", ServiceFactory.TraderPassword)]
    [InlineData("no-such-server", "t1@test.example", ServiceFactory.TraderPassword)]
    public async Task WrongServerEmailOrPasswordIsRefusedTheSameWay(string server, string email, string password)
    {
        using var factory = new ServiceFactory();
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var client = factory.CreateClient();

        var problem = await client.PostJsonAsync("/api/auth/login", new { server, email, password }, HttpStatusCode.Unauthorized);

        Assert.Equal("Wrong email or password.", problem.GetProperty("title").GetString());
    }

    // The trader need not know the firm's server: the email address and the password find it (ADR 0058).
    [Fact]
    public async Task ATraderLogsInWithoutNamingTheServer()
    {
        using var factory = new ServiceFactory();
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var client = factory.CreateClient();

        var me = await client.PostJsonAsync("/api/auth/login", new { email = ServiceFactory.EmailOf("T1"), password = ServiceFactory.TraderPassword });

        Assert.Equal("demo-firm", me.GetProperty("server").GetProperty("id").GetString());
    }

    // The same email address and password at two firms: the trader is told which, and chooses, without logging in.
    [Fact]
    public async Task ATraderAtTwoFirmsChoosesWhichToLogInTo()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using (var other = factory.CreateAdminClient(SecondFirm.ApiKey))
        {
            await other.PostJsonAsync("/api/admin/v1/users", new { email = ServiceFactory.EmailOf("T1"), password = ServiceFactory.TraderPassword });
        }

        using var client = factory.CreateClient();
        var choose = await client.PostJsonAsync(
            "/api/auth/login",
            new { email = ServiceFactory.EmailOf("T1"), password = ServiceFactory.TraderPassword },
            HttpStatusCode.Conflict);
        var me = await client.PostJsonAsync("/api/auth/login", new { server = SecondFirm.Server, email = ServiceFactory.EmailOf("T1"), password = ServiceFactory.TraderPassword });

        Assert.Equal(["Demo Firm", "Other Firm"], choose.GetProperty("servers").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(SecondFirm.Server, me.GetProperty("server").GetProperty("id").GetString());
    }

    // A firm that logs its traders in through its own portal turns password login off, and a password then fits nowhere.
    [Fact]
    public async Task NoPasswordLoginAtAFirmThatTurnedItOff()
    {
        using var factory = new ServiceFactory();
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using (var admin = factory.CreateAdminClient())
        {
            var profile = await admin.GetJsonAsync("/api/admin/v1/terminal-profile");
            var changed = JsonNode.Parse(profile.GetRawText())!;
            changed["passwordLogin"] = false;
            await admin.PutJsonAsync("/api/admin/v1/terminal-profile", changed);
        }

        using var client = factory.CreateClient();
        await client.PostJsonAsync("/api/auth/login", new { server = ServiceFactory.DemoServer, email = ServiceFactory.EmailOf("T1"), password = ServiceFactory.TraderPassword }, HttpStatusCode.Unauthorized);
        var server = await client.GetJsonAsync($"/api/servers/{ServiceFactory.DemoServer}");

        Assert.False(server.GetProperty("profile").GetProperty("passwordLogin").GetBoolean());
    }

    [Fact]
    public async Task EmailIsComparedWithoutCase()
    {
        using var factory = new ServiceFactory();
        (await factory.CreateTraderClientAsync("T1")).Dispose();

        using var client = await factory.LoginAsync("  T1@TEST.EXAMPLE ", ServiceFactory.TraderPassword);

        Assert.Equal(["T1"], (await client.GetJsonAsync("/api/auth/me")).GetProperty("accounts").EnumerateArray().Select(a => a.GetString()));
    }

    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/api/accounts/T1")]
    [InlineData("/api/accounts/T1/events")]
    public async Task TraderApiNeedsALogin(string url)
    {
        using var factory = new ServiceFactory();
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AccountsOfOtherTradersLookLikeTheyDoNotExist()
    {
        using var factory = new ServiceFactory();
        (await factory.CreateTraderClientAsync("T2")).Dispose();
        using var client = await factory.CreateTraderClientAsync("T1");

        using var response = await client.GetAsync(new Uri("/api/accounts/T2", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LogoutEndsTheSession()
    {
        using var factory = new ServiceFactory();
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var client = factory.CreateClient();
        await client.PostJsonAsync("/api/auth/login", new { server = ServiceFactory.DemoServer, email = ServiceFactory.EmailOf("T1"), password = ServiceFactory.TraderPassword });

        using var logout = await client.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), null, TestContext.Current.CancellationToken);
        using var me = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task LoginIsRateLimited()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
        {
            using var response = await client.PostAsJsonAsync(
                new Uri("/api/auth/login", UriKind.Relative),
                new { server = ServiceFactory.DemoServer, email = "someone@test.example", password = "wrong-password" },
                TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        Assert.All(statuses.Take(10), s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
    }

    [Fact]
    public async Task TheLoginRulesCanBeTurnedOffForDevelopment()
    {
        using var factory = new ServiceFactory(settings: new Dictionary<string, string> { ["Login:MinimumPasswordLength"] = "1", ["Login:AttemptsPerMinute"] = "0" });
        using var client = factory.CreateClient();
        using var admin = factory.CreateAdminClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 15; i++)
        {
            using var response = await client.PostAsJsonAsync(
                new Uri("/api/auth/login", UriKind.Relative),
                new { server = ServiceFactory.DemoServer, email = "someone@test.example", password = "wrong" },
                TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        await admin.PostJsonAsync("/api/admin/v1/users", new { email = "a@test.example", password = "a" });
        await admin.PostJsonAsync("/api/admin/v1/users", new { email = "b@test.example", password = "" }, HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task AdminApiNeedsTheFirmsKey(string? apiKey)
    {
        using var factory = new ServiceFactory();
        using var client = apiKey is null ? factory.CreateClient() : factory.CreateAdminClient(apiKey);

        await client.PostJsonAsync("/api/admin/v1/users", new { email = "a@test.example", password = ServiceFactory.TraderPassword }, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UsersNeedAValidEmailAndALongEnoughPassword()
    {
        using var factory = new ServiceFactory();
        using var admin = factory.CreateAdminClient();

        await admin.PostJsonAsync("/api/admin/v1/users", new { email = "not-an-email", password = ServiceFactory.TraderPassword }, HttpStatusCode.UnprocessableEntity);
        await admin.PostJsonAsync("/api/admin/v1/users", new { email = "a@test.example", password = "short" }, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task EmailIsUniqueWithinAFirmButNotAcrossFirms()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        using var demo = factory.CreateAdminClient();
        using var other = factory.CreateAdminClient(SecondFirm.ApiKey);
        var user = new { email = "same@test.example", password = ServiceFactory.TraderPassword };

        await demo.PostJsonAsync("/api/admin/v1/users", user);
        await demo.PostJsonAsync("/api/admin/v1/users", user, HttpStatusCode.Conflict);
        await other.PostJsonAsync("/api/admin/v1/users", user);
    }

    [Fact]
    public async Task FirmsCannotReachEachOthersGroupsUsersOrAccounts()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var other = factory.CreateAdminClient(SecondFirm.ApiKey);
        var otherUser = await other.PostJsonAsync("/api/admin/v1/users", new { email = "o@test.example", password = ServiceFactory.TraderPassword });
        var demoUserId = (await factory.CreateAdminClient().PostJsonAsync("/api/admin/v1/users", new { email = "d@test.example", password = ServiceFactory.TraderPassword })).GetProperty("userId").GetGuid();

        // The demo firm's group, and the demo firm's user
        await other.PostJsonAsync("/api/admin/v1/accounts", new { accountId = "X1", groupId = "standard", initialBalance = 1_000m, ownerUserId = otherUser.GetProperty("userId").GetGuid() }, HttpStatusCode.NotFound);
        await other.PostJsonAsync("/api/admin/v1/accounts", new { accountId = "X2", groupId = "other", initialBalance = 1_000m, ownerUserId = demoUserId }, HttpStatusCode.NotFound);

        // The demo firm's account
        using var close = await other.PostAsync(new Uri("/api/admin/v1/accounts/T1/close", UriKind.Relative), null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, close.StatusCode);
    }

    // The same email at two firms is two traders, each with their own password and accounts.
    [Fact]
    public async Task TradersLogInOnTheirOwnFirmsServer()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var other = factory.CreateAdminClient(SecondFirm.ApiKey);
        await other.PostJsonAsync("/api/admin/v1/users", new { email = ServiceFactory.EmailOf("T1"), password = "other-password" });
        using var client = factory.CreateClient();

        await client.PostJsonAsync(
            "/api/auth/login",
            new { server = "other-firm", email = ServiceFactory.EmailOf("T1"), password = ServiceFactory.TraderPassword },
            HttpStatusCode.Unauthorized);
        var me = await client.PostJsonAsync("/api/auth/login", new { server = "other-firm", email = ServiceFactory.EmailOf("T1"), password = "other-password" });

        Assert.Equal("other-firm", me.GetProperty("server").GetProperty("id").GetString());
        Assert.Empty(me.GetProperty("accounts").EnumerateArray());
    }

    [Fact]
    public async Task ListedFirmsAreFoundByNameAndNeverAllListed()
    {
        using var factory = new ServiceFactory(settings: SecondFirm.Settings);
        using var client = factory.CreateClient();

        var firm = await client.GetJsonAsync("/api/servers?search=FIRM");
        var other = await client.GetJsonAsync("/api/servers?search=other");
        var tooShort = await client.GetJsonAsync("/api/servers?search=f");
        var none = await client.GetJsonAsync("/api/servers");

        Assert.Equal(
            [("demo-firm", "Demo Firm"), ("other-firm", "Other Firm")],
            firm.EnumerateArray().Select(s => (s.GetProperty("id").GetString(), s.GetProperty("name").GetString())));
        Assert.Equal(["other-firm"], other.EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        Assert.Equal((0, 0), (tooShort.GetArrayLength(), none.GetArrayLength()));
    }

    [Theory]
    [InlineData("Tenants:0:Id", "Demo Firm")]
    [InlineData("Tenants:0:Name", " ")]
    [InlineData("Tenants:0:AdminApiKeySha256", "not-a-hash")]
    public void InvalidFirmConfigurationStopsTheStart(string key, string value)
    {
        using var factory = new ServiceFactory(settings: new Dictionary<string, string> { [key] = value });

        // The configured firms are saved and loaded at startup.
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Invalid tenant configuration", exception.ToString(), StringComparison.Ordinal);
    }
}
