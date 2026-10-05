using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Prop.Api.Firms;
using Prop.Api.Review;
using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>
/// What a firm may not do before we have approved it (ADR 0043): email anyone but its administrators, send more than a few
/// invitations to its team, or put its portal on its own domain.
/// </summary>
public sealed class BeforeApprovalTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Owner = "owner@firm.test";

    private const string Colleague = "colleague@firm.test";

    [Fact]
    public async Task UntilWeApproveAFirmItsEmailsGoOnlyToItsAdministrators()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme", Owner);
        await PropFactory.WaitUntilProvisionedAsync(admin);
        var colleagues = await StartAsync(admin, Colleague);
        var owners = await StartAsync(admin, Owner);

        var preview = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{colleagues}/email-trader"), TestContext.Current.CancellationToken);
        using var refused = await admin.PostAsync(Url($"admin/accounts/{colleagues}/email-trader"), null, TestContext.Current.CancellationToken);
        var ownersPreview = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{owners}/email-trader"), TestContext.Current.CancellationToken);
        using var sent = await admin.PostAsync(Url($"admin/accounts/{owners}/email-trader"), null, TestContext.Current.CancellationToken);

        // A queued email to the colleague is kept but never sent. The owner's, queued after it, is.
        using var portal = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        (await portal.PostAsJsonAsync(Url("password-reset"), new { email = Colleague }, TestContext.Current.CancellationToken)).Dispose();
        (await portal.PostAsJsonAsync(Url("password-reset"), new { email = Owner }, TestContext.Current.CancellationToken)).Dispose();
        await factory.Emails.WaitForAsync(Owner, "Choose a new password");

        Assert.Equal(FirmApproval.TeamOnlyProblem, preview.GetProperty("withheld").GetString());
        Assert.Equal((HttpStatusCode.Conflict, FirmApproval.TeamOnlyProblem), (refused.StatusCode, await TitleAsync(refused)));
        Assert.Equal(JsonValueKind.Null, ownersPreview.GetProperty("withheld").ValueKind);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Contains(factory.Emails.Sent, e => e.To == Owner && e.Subject.Contains("has started", StringComparison.Ordinal));
        Assert.DoesNotContain(factory.Emails.Sent, e => e.To == Colleague);
        Assert.Equal(1L, await factory.ScalarAsync($"select count(*) from email_outbox where firm_id = 'acme' and to_address = '{Colleague}' and withheld_at is not null"));
        Assert.Equal(0L, await factory.ScalarAsync($"select count(*) from email_outbox where firm_id = 'acme' and to_address = '{Owner}' and withheld_at is not null"));
    }

    [Fact]
    public async Task AFirmWeHaveApprovedEmailsAnyone()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme", Owner);
        await PropFactory.WaitUntilProvisionedAsync(admin);
        var colleagues = await StartAsync(admin, Colleague);

        await factory.ApproveAsync(admin, "acme");
        var preview = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{colleagues}/email-trader"), TestContext.Current.CancellationToken);
        using var sent = await admin.PostAsync(Url($"admin/accounts/{colleagues}/email-trader"), null, TestContext.Current.CancellationToken);
        using var portal = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        (await portal.PostAsJsonAsync(Url("password-reset"), new { email = Colleague }, TestContext.Current.CancellationToken)).Dispose();

        Assert.Equal(JsonValueKind.Null, preview.GetProperty("withheld").ValueKind);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        await factory.Emails.WaitForAsync(Colleague, "Choose a new password");
        Assert.Contains(factory.Emails.Sent, e => e.To == Colleague && e.Subject.Contains("has started", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UntilWeApproveAFirmOnlyItsAdministratorsLogInAsItsTraders()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme", Owner);
        await PropFactory.WaitUntilProvisionedAsync(admin);
        var colleagues = await StartAsync(admin, Colleague);
        var owners = await StartAsync(admin, Owner);
        using var firmApi = factory.CreateFirmClient(await NewApiKeyAsync(admin));

        using var refusedLink = await admin.PostAsync(Url($"admin/accounts/{colleagues}/invite"), null, TestContext.Current.CancellationToken);
        using var refusedByApi = await firmApi.PostAsync(FirmUrl($"accounts/{colleagues}/invite"), null, TestContext.Current.CancellationToken);
        using var refusedTerminal = await firmApi.PostAsync(FirmUrl($"accounts/{colleagues}/login-link"), null, TestContext.Current.CancellationToken);
        using var ownersLink = await admin.PostAsync(Url($"admin/accounts/{owners}/invite"), null, TestContext.Current.CancellationToken);
        using var portal = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        using var accepted = await portal.PostAsJsonAsync(
            Url("invites/accept"), new { token = await TokenOfAsync(ownersLink), password = PropFactory.TraderPassword }, TestContext.Current.CancellationToken);
        using var me = await portal.GetAsync(Url("me"), TestContext.Current.CancellationToken);

        // Once we approve the firm, anyone it invites logs in.
        await factory.ApproveAsync(admin, "acme", chooseKyc: false);
        using var colleaguesLink = await admin.PostAsync(Url($"admin/accounts/{colleagues}/invite"), null, TestContext.Current.CancellationToken);
        using var colleague = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        using var colleagueAccepted = await colleague.PostAsJsonAsync(
            Url("invites/accept"), new { token = await TokenOfAsync(colleaguesLink), password = PropFactory.TraderPassword }, TestContext.Current.CancellationToken);

        foreach (var refused in new[] { refusedLink, refusedByApi, refusedTerminal })
        {
            Assert.Equal((HttpStatusCode.Conflict, FirmApproval.TeamOnlyProblem), (refused.StatusCode, await TitleAsync(refused)));
        }

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK), (ownersLink.StatusCode, accepted.StatusCode, me.StatusCode));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (colleaguesLink.StatusCode, colleagueAccepted.StatusCode));
    }

    [Fact]
    public async Task AnInvitationFromBeforeTheRuleDoesNotLetAnOutsiderIn()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme", Owner);
        await PropFactory.WaitUntilProvisionedAsync(admin);
        var colleagues = await StartAsync(admin, Colleague);

        // An invitation made before the rule, straight in the database, is refused without being used up.
        var token = await InviteInTheDatabaseAsync(factory, colleagues);
        using var portal = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        using var refused = await portal.PostAsJsonAsync(Url("invites/accept"), new { token, password = PropFactory.TraderPassword }, TestContext.Current.CancellationToken);
        await factory.ApproveAsync(admin, "acme", chooseKyc: false);
        using var accepted = await portal.PostAsJsonAsync(Url("invites/accept"), new { token, password = PropFactory.TraderPassword }, TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.Forbidden, "Firm acme is not open yet, so only its own team can log in here."), (refused.StatusCode, await TitleAsync(refused)));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task UntilWeApproveAFirmItSendsAFewInvitationsToItsTeam()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), new Dictionary<string, string> { ["Sandbox:MaxAdminInvites"] = "2" });
        using var admin = await factory.SignUpAsync("acme", Owner);
        await PropFactory.WaitUntilProvisionedAsync(admin);

        using var first = await InviteAsync(admin, "a@firm.test");
        using var withdrawn = await admin.PostAsJsonAsync(Url("admin/admins/invites/withdraw"), new { email = "a@firm.test" }, TestContext.Current.CancellationToken);
        using var second = await InviteAsync(admin, "b@firm.test");

        // One taken back still counts, so taking invitations back sends no more.
        using var third = await InviteAsync(admin, "c@firm.test");
        await factory.ApproveAsync(admin, "acme");
        using var afterApproval = await InviteAsync(admin, "c@firm.test");

        Assert.Equal(
            (HttpStatusCode.Created, HttpStatusCode.NoContent, HttpStatusCode.Created, HttpStatusCode.Conflict, HttpStatusCode.Created),
            (first.StatusCode, withdrawn.StatusCode, second.StatusCode, third.StatusCode, afterApproval.StatusCode));
        Assert.Equal("Until we have approved your firm, it can send 2 invitations in 30 days. You can send more once we have approved it.", await TitleAsync(third));
        Assert.Equal(["a@firm.test", "b@firm.test", "c@firm.test"], factory.Emails.Sent.Where(e => e.Subject.StartsWith("You are invited", StringComparison.Ordinal)).Select(e => e.To));
    }

    [Fact]
    public async Task AFirmAddsItsOwnDomainOnceWeHaveApprovedIt()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme", Owner);
        await PropFactory.WaitUntilProvisionedAsync(admin);

        var before = await admin.GetFromJsonAsync<JsonElement>(Url("admin/domain"), TestContext.Current.CancellationToken);
        using var refused = await admin.PutAsJsonAsync(Url("admin/domain"), new { domain = "portal.acme-firm.test" }, TestContext.Current.CancellationToken);
        await factory.ApproveAsync(admin, "acme");
        var after = await admin.GetFromJsonAsync<JsonElement>(Url("admin/domain"), TestContext.Current.CancellationToken);
        using var saved = await admin.PutAsJsonAsync(Url("admin/domain"), new { domain = "portal.acme-firm.test" }, TestContext.Current.CancellationToken);

        Assert.True(before.GetProperty("waitsForApproval").GetBoolean());
        Assert.Equal((HttpStatusCode.Conflict, FirmApproval.DomainProblem), (refused.StatusCode, await TitleAsync(refused)));
        Assert.False(after.GetProperty("waitsForApproval").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    [Fact]
    public async Task ADomainAddedBeforeTheRuleWaitsForOurApproval()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.SignUpAsync("acme", Owner);
        await PropFactory.WaitUntilProvisionedAsync(admin);
        const string Domain = "portal.acme-firm.test";
        await factory.ScalarAsync($"insert into firm_domains (firm_id, domain, token, status, created_at) values ('acme', '{Domain}', 'prop-verify-old', 'Pending', now())");
        factory.Dns.Add($"_kronant.{Domain}", DnsRecordType.Txt, "prop-verify-old");
        factory.Dns.Add(Domain, DnsRecordType.Cname, "portals.kronant.app");

        var waiting = await (await admin.PostAsync(Url("admin/domain/check"), null, TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        await factory.ApproveAsync(admin, "acme");
        var active = await (await admin.PostAsync(Url("admin/domain/check"), null, TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(("Pending", FirmApproval.DomainProblem), (waiting.GetProperty("status").GetString(), waiting.GetProperty("problem").GetString()));
        Assert.Equal("Active", active.GetProperty("status").GetString());
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static Uri FirmUrl(string path) => new($"/api/firm/v1/{path}", UriKind.Relative);

    private static async Task<string> NewApiKeyAsync(HttpClient admin)
    {
        using var response = await admin.PostAsync(Url("admin/firm/api-key"), null, TestContext.Current.CancellationToken);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("apiKey").GetString()!;
    }

    private static async Task<string> TokenOfAsync(HttpResponseMessage invite)
    {
        var url = (await invite.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("url").GetString()!;
        return System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["token"]!;
    }

    // An invitation for the account's trader, made the way the service makes them, without its checks.
    private static async Task<string> InviteInTheDatabaseAsync(PropFactory factory, Guid accountId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<Portal.PortalUsers>();
        var traderId = (Guid)(await factory.ScalarAsync($"select trader_id from challenge_accounts where id = '{accountId}'"))!;
        return (await users.CreateInviteAsync(traderId, factory.Time.GetUtcNow(), TestContext.Current.CancellationToken)).Token;
    }

    private static async Task<Guid> StartAsync(HttpClient admin, string email)
    {
        using var started = await admin.PostAsJsonAsync(Url("admin/accounts"), new { email, challengeId = "two-step-100k" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        return (await started.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> InviteAsync(HttpClient admin, string email) =>
        admin.PostAsJsonAsync(Url("admin/admins/invites"), new { email }, TestContext.Current.CancellationToken);

    private static async Task<string?> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("title").GetString();
}
