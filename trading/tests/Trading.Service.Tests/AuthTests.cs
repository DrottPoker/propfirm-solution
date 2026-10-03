using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using Trading.Service.Tenancy;
using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

public sealed class AuthTests
{
    private const string OtherFirmKey = "other-admin-key";

    // A second firm on its own host with its own group.
    private static readonly Dictionary<string, string> TwoFirms = new()
    {
        ["Trading:Groups:1:Id"] = "other",
        ["Trading:Groups:1:Currency"] = "USD",
        ["Trading:Groups:1:StopOutLevelPercent"] = "50",
        ["Trading:Groups:1:Symbols:0:Symbol"] = "EURUSD",
        ["Trading:Groups:1:Symbols:0:Leverage"] = "100",
        ["Trading:Groups:1:Symbols:0:SpreadMarkupPoints"] = "0",
        ["Trading:Groups:1:Symbols:0:CommissionPerLotPerSide"] = "0",
        ["Tenants:1:Id"] = "other-firm",
        ["Tenants:1:Hosts:0"] = "other.example",
        ["Tenants:1:Groups:0"] = "other",
        ["Tenants:1:AdminApiKeySha256"] = TenantCatalog.HashApiKey(OtherFirmKey),
        ["Tenants:1:Branding:DisplayName"] = "Other Firm",
    };

    [Fact]
    public async Task MeShowsTheTraderAndTheirAccounts()
    {
        using var factory = new ServiceFactory();
        using var client = await factory.CreateTraderClientAsync("T1");

        var me = await client.GetJsonAsync("/api/auth/me");

        Assert.Equal(ServiceFactory.EmailOf("T1"), me.GetProperty("email").GetString());
        Assert.Equal("demo-firm", me.GetProperty("tenantId").GetString());
        Assert.Equal(["T1"], me.GetProperty("accounts").EnumerateArray().Select(a => a.GetString()));
    }

    [Theory]
    [InlineData("t1@test.example", "wrong-password")]
    [InlineData("nobody@test.example", ServiceFactory.TraderPassword)]
    public async Task WrongEmailOrPasswordIsRefusedTheSameWay(string email, string password)
    {
        using var factory = new ServiceFactory();
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var client = factory.CreateClient();

        var problem = await client.PostJsonAsync("/api/auth/login", new { email, password }, HttpStatusCode.Unauthorized);

        Assert.Equal("Wrong email or password.", problem.GetProperty("title").GetString());
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
        await client.PostJsonAsync("/api/auth/login", new { email = ServiceFactory.EmailOf("T1"), password = ServiceFactory.TraderPassword });

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
                new { email = "someone@test.example", password = "wrong-password" },
                TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        Assert.All(statuses.Take(10), s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task AdminApiNeedsTheFirmsKey(string? apiKey)
    {
        using var factory = new ServiceFactory();
        using var client = apiKey is null ? factory.CreateClient() : factory.CreateAdminClient(apiKey);

        await client.PostJsonAsync("/api/admin/users", new { email = "a@test.example", password = ServiceFactory.TraderPassword }, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UsersNeedAValidEmailAndALongEnoughPassword()
    {
        using var factory = new ServiceFactory();
        using var admin = factory.CreateAdminClient();

        await admin.PostJsonAsync("/api/admin/users", new { email = "not-an-email", password = ServiceFactory.TraderPassword }, HttpStatusCode.UnprocessableEntity);
        await admin.PostJsonAsync("/api/admin/users", new { email = "a@test.example", password = "short" }, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task EmailIsUniqueWithinAFirmButNotAcrossFirms()
    {
        using var factory = new ServiceFactory(settings: TwoFirms);
        using var demo = factory.CreateAdminClient();
        using var other = factory.CreateAdminClient(OtherFirmKey);
        var user = new { email = "same@test.example", password = ServiceFactory.TraderPassword };

        await demo.PostJsonAsync("/api/admin/users", user);
        await demo.PostJsonAsync("/api/admin/users", user, HttpStatusCode.Conflict);
        await other.PostJsonAsync("/api/admin/users", user);
    }

    [Fact]
    public async Task FirmsCannotReachEachOthersGroupsUsersOrAccounts()
    {
        using var factory = new ServiceFactory(settings: TwoFirms);
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var other = factory.CreateAdminClient(OtherFirmKey);
        var otherUser = await other.PostJsonAsync("/api/admin/users", new { email = "o@test.example", password = ServiceFactory.TraderPassword });
        var demoUserId = (await factory.CreateAdminClient().PostJsonAsync("/api/admin/users", new { email = "d@test.example", password = ServiceFactory.TraderPassword })).GetProperty("userId").GetGuid();

        // The demo firm's group, and the demo firm's user
        await other.PostJsonAsync("/api/admin/accounts", new { accountId = "X1", groupId = "standard", initialBalance = 1_000m, ownerUserId = otherUser.GetProperty("userId").GetGuid() }, HttpStatusCode.NotFound);
        await other.PostJsonAsync("/api/admin/accounts", new { accountId = "X2", groupId = "other", initialBalance = 1_000m, ownerUserId = demoUserId }, HttpStatusCode.NotFound);

        // The demo firm's account
        using var close = await other.PostAsync(new Uri("/api/admin/accounts/T1/close", UriKind.Relative), null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, close.StatusCode);
    }

    [Fact]
    public async Task TradersLogInAtTheirOwnFirmsAddressOnly()
    {
        using var factory = new ServiceFactory(settings: TwoFirms);
        (await factory.CreateTraderClientAsync("T1")).Dispose();
        using var otherHost = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://other.example") });

        await otherHost.PostJsonAsync(
            "/api/auth/login",
            new { email = ServiceFactory.EmailOf("T1"), password = ServiceFactory.TraderPassword },
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BrandingFollowsTheHost()
    {
        using var factory = new ServiceFactory(settings: TwoFirms);
        using var client = factory.CreateClient();

        var demo = await client.GetJsonAsync("/api/branding?host=localhost");
        var other = await client.GetJsonAsync("/api/branding?host=other.example");
        using var unknown = await client.GetAsync(new Uri("/api/branding?host=unknown.example", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("Demo Firm", demo.GetProperty("displayName").GetString());
        Assert.Equal("#3b82f6", demo.GetProperty("colors").GetProperty("accent").GetString());
        Assert.Equal("Other Firm", other.GetProperty("displayName").GetString());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Theory]
    [InlineData("Tenants:0:Branding:Colors:accent", "red; background: url(x)")]
    [InlineData("Tenants:0:Branding:Colors:unknown", "#ffffff")]
    [InlineData("Tenants:0:Branding:LogoUrl", "http://insecure.example/logo.png")]
    [InlineData("Tenants:0:AdminApiKeySha256", "not-a-hash")]
    public void InvalidFirmConfigurationStopsTheStart(string key, string value)
    {
        using var factory = new ServiceFactory(settings: new Dictionary<string, string> { [key] = value });

        // The firms are loaded at startup, by the development account seeder.
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Invalid tenant configuration", exception.ToString(), StringComparison.Ordinal);
    }
}
