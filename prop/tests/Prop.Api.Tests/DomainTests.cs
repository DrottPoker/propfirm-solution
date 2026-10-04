using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Firms;
using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>A firm's own domain for its portal: proved with a TXT record, pointed to us, and then the portal's address.</summary>
public sealed class DomainTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Domain = "portal.acme-firm.test";
    private const string Target = "portals.example-platform.app";

    [Fact]
    public async Task AFirmsDomainBecomesThePortalsAddressOnceItsRecordsAreThere()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);

        var waiting = await SaveAsync(admin, " Portal.Acme-Firm.test. ", HttpStatusCode.OK);
        factory.Dns.Add(waiting.GetProperty("txtName").GetString()!, DnsRecordType.Txt, waiting.GetProperty("txtValue").GetString()!);
        var halfway = await CheckAsync(admin);
        factory.Dns.Add(Domain, DnsRecordType.Cname, Target);
        var active = await CheckAsync(admin);
        var settings = await admin.GetFromJsonAsync<JsonElement>(Url("admin/firm"), TestContext.Current.CancellationToken);
        using var onTheDomain = factory.CreatePortalClient(Domain);
        var branding = await onTheDomain.GetFromJsonAsync<JsonElement>(Url("branding"), TestContext.Current.CancellationToken);
        using var anyone = factory.CreateClient();
        using var allowed = await anyone.GetAsync(new Uri($"/api/tls/allowed?domain={Domain}", UriKind.Relative), TestContext.Current.CancellationToken);
        using var notOurs = await anyone.GetAsync(new Uri("/api/tls/allowed?domain=elsewhere.example.com", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(("Pending", Domain, Target), (waiting.GetProperty("status").GetString(), waiting.GetProperty("domain").GetString(), waiting.GetProperty("cnameTarget").GetString()));
        Assert.Equal($"_prop-platform.{Domain}", waiting.GetProperty("txtName").GetString());
        Assert.StartsWith("prop-verify-", waiting.GetProperty("txtValue").GetString(), StringComparison.Ordinal);
        Assert.Equal($"The TXT record _prop-platform.{Domain} with the value {waiting.GetProperty("txtValue").GetString()} is not there yet.", waiting.GetProperty("problem").GetString());
        Assert.Equal($"{Domain} does not point to {Target} yet. Add a CNAME record for it.", halfway.GetProperty("problem").GetString());
        Assert.Equal(("Active", JsonValueKind.Null), (active.GetProperty("status").GetString(), active.GetProperty("problem").ValueKind));
        Assert.Equal($"https://{Domain}/", settings.GetProperty("portalUrl").GetString());
        Assert.Equal("Firm acme", branding.GetProperty("name").GetString());
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.NotFound), (allowed.StatusCode, notOurs.StatusCode));
        await Eventually.ThatAsync(() => Task.FromResult(factory.Trading.ListingOf("acme")?.LoginUrl == new Uri($"https://{Domain}/terminal")), "the terminal's link to the new address");
    }

    [Fact]
    public async Task ADomainThatPointsWithTheSameAddressesCountsToo()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(admin);
        factory.Dns.Add(Target, DnsRecordType.A, "203.0.113.7", "203.0.113.8");
        factory.Dns.Add(Domain, DnsRecordType.A, "203.0.113.8", "203.0.113.7");

        var saved = await SaveAsync(admin, Domain, HttpStatusCode.OK);
        factory.Dns.Add(saved.GetProperty("txtName").GetString()!, DnsRecordType.Txt, saved.GetProperty("txtValue").GetString()!);
        factory.Dns.FailNext(1);
        var dnsDown = await CheckAsync(admin);
        var active = await CheckAsync(admin);

        Assert.Equal("We could not look the domain up right now, and try again shortly.", dnsDown.GetProperty("problem").GetString());
        Assert.Equal("Active", active.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("acme-firm.test", "Use a subdomain of your own domain, such as portal.yourfirm.com. A domain without one cannot point to us with a CNAME record.")]
    [InlineData("10.0.0.1", "Write a domain such as portal.yourfirm.com.")]
    [InlineData("portal.app.localhost", "That domain is ours. Use one of your own.")]
    [InlineData("acme.portals.example-platform.app", "That domain is ours. Use one of your own.")]
    public async Task ADomainMustBeTheFirmsOwnSubdomain(string domain, string problem)
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme");

        var refused = await SaveAsync(admin, domain, HttpStatusCode.UnprocessableEntity);

        Assert.Equal(problem, refused.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ADomainBelongsToOneFirmAndGoesBackWhenRemoved()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var acme = await factory.SignUpAsync("acme");
        await PropFactory.WaitUntilProvisionedAsync(acme);
        using var other = await factory.SignUpAsync("other", "owner@other.test");
        var saved = await SaveAsync(acme, Domain, HttpStatusCode.OK);
        factory.Dns.Add(saved.GetProperty("txtName").GetString()!, DnsRecordType.Txt, saved.GetProperty("txtValue").GetString()!);
        factory.Dns.Add(Domain, DnsRecordType.Cname, Target);
        await CheckAsync(acme);

        var taken = await SaveAsync(other, Domain, HttpStatusCode.Conflict);
        using var removed = await acme.DeleteAsync(Url("admin/domain"), TestContext.Current.CancellationToken);
        var settings = await acme.GetFromJsonAsync<JsonElement>(Url("admin/firm"), TestContext.Current.CancellationToken);
        using var onTheDomain = factory.CreatePortalClient(Domain);
        using var branding = await onTheDomain.GetAsync(Url("branding"), TestContext.Current.CancellationToken);
        using var admin = await factory.LogInAsAdminAsync();
        var configured = await SaveAsync(admin, "portal.demo-firm.test", HttpStatusCode.Conflict);

        Assert.Equal("Another firm has that domain.", taken.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal("http://acme.localhost:3002/", settings.GetProperty("portalUrl").GetString());
        Assert.Equal(HttpStatusCode.NotFound, branding.StatusCode);
        Assert.Equal("Your firm's addresses come from our configuration, so they are changed there.", configured.GetProperty("title").GetString());
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static async Task<JsonElement> SaveAsync(HttpClient admin, string domain, HttpStatusCode expected)
    {
        using var response = await admin.PutAsJsonAsync(Url("admin/domain"), new { domain }, TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> CheckAsync(HttpClient admin)
    {
        using var response = await admin.PostAsync(Url("admin/domain/check"), null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }
}
