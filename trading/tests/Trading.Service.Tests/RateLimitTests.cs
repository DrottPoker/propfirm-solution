using System.Net;
using System.Text.Json;

using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>Each caller has an allowance of its own (ADR 0059), so a busy or broken client slows only itself.</summary>
public sealed class RateLimitTests
{
    // 8 a minute: 2 at once, then one more a second. Eight requests in a row get at most three or four through.
    private const string Small = "8";

    [Fact]
    public async Task ATraderWhoAsksTooOftenWaitsWhileOtherTradersGoOn()
    {
        using var factory = new ServiceFactory(settings: new Dictionary<string, string> { ["RateLimits:TraderPerMinute"] = Small });
        using var busy = await factory.CreateTraderClientAsync("T1");
        using var other = await factory.CreateTraderClientAsync("T2");

        var answers = await RepeatAsync(busy, "/api/accounts/T1", 8);
        var refused = answers.First(a => a.StatusCode == HttpStatusCode.TooManyRequests);

        Assert.True(answers.Count(a => a.StatusCode == HttpStatusCode.TooManyRequests) >= 4);
        Assert.True(refused.Headers.RetryAfter?.Delta >= TimeSpan.FromSeconds(1));
        var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        Assert.Equal("TooManyRequests", problem.GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.OK, await StatusOfAsync(other, "/api/accounts/T2"));
    }

    [Fact]
    public async Task AFirmsSystemThatAsksTooOftenWaitsWhileOtherFirmsGoOn()
    {
        var settings = new Dictionary<string, string>(SecondFirm.Settings) { ["RateLimits:FirmPerMinute"] = Small };
        using var factory = new ServiceFactory(settings: settings);
        using var busy = factory.CreateAdminClient();
        using var other = factory.CreateAdminClient(SecondFirm.ApiKey);

        var answers = await RepeatAsync(busy, "/api/admin/v1/groups", 8);

        Assert.True(answers.Count(a => a.StatusCode == HttpStatusCode.TooManyRequests) >= 4);
        Assert.Equal(HttpStatusCode.OK, await StatusOfAsync(other, "/api/admin/v1/groups"));
    }

    [Fact]
    public async Task PartnersAndServerLookupsHaveAllowancesToo()
    {
        using var factory = new ServiceFactory(settings: new Dictionary<string, string> { ["RateLimits:PartnerPerMinute"] = Small, ["RateLimits:PublicPerMinute"] = Small });
        using var partner = factory.CreatePartnerClient();
        using var anonymous = factory.CreateClient();

        var partnerAnswers = await RepeatAsync(partner, "/api/partner/v1/price-feed", 8);
        var lookups = await RepeatAsync(anonymous, "/api/servers?search=demo", 8);

        Assert.True(partnerAnswers.Count(a => a.StatusCode == HttpStatusCode.TooManyRequests) >= 4);
        Assert.True(lookups.Count(a => a.StatusCode == HttpStatusCode.TooManyRequests) >= 4);
    }

    [Fact]
    public async Task TheDefaultAllowanceCoversATerminalOpeningAndZeroTurnsItOff()
    {
        using var standard = new ServiceFactory();
        using var trader = await standard.CreateTraderClientAsync("T1");
        using var unlimited = new ServiceFactory(settings: new Dictionary<string, string> { ["RateLimits:TraderPerMinute"] = "0" });
        using var free = await unlimited.CreateTraderClientAsync("T1");

        // A terminal that opens asks for about sixty things at once: the account, the charts and a day for each symbol.
        // Three reloads in a row stay within the allowance.
        Assert.All(await RepeatAsync(trader, "/api/accounts/T1", 180), a => Assert.Equal(HttpStatusCode.OK, a.StatusCode));
        Assert.All(await RepeatAsync(free, "/api/accounts/T1", 300), a => Assert.Equal(HttpStatusCode.OK, a.StatusCode));
    }

    private static async Task<List<HttpResponseMessage>> RepeatAsync(HttpClient client, string url, int times)
    {
        var answers = new List<HttpResponseMessage>(times);
        for (var i = 0; i < times; i++)
        {
            answers.Add(await client.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken));
        }

        return answers;
    }

    private static async Task<HttpStatusCode> StatusOfAsync(HttpClient client, string url) =>
        (await client.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode;
}
