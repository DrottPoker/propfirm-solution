using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>
/// A forgotten password for traders, administrators and our staff: an emailed link that works once within an hour, and
/// logs out the other sessions. Also the checks of invitation links before anyone types a password.
/// </summary>
public sealed class PasswordResetTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task ATraderChoosesANewPasswordWithTheEmailedLink()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var trader = factory.CreatePortalClient();

        using var asked = await trader.PostAsJsonAsync(Url("password-reset"), new { email = " Anna@Test.example " }, TestContext.Current.CancellationToken);
        var email = await factory.Emails.WaitForAsync("anna@test.example", "Choose a new password");
        var token = FakeEmailSender.TokenIn(email);
        var valid = await CheckAsync(trader, "password-reset/check", token);
        using var tooShort = await trader.PostAsJsonAsync(Url("password-reset/confirm"), new { token, password = "short" }, TestContext.Current.CancellationToken);
        using var confirmed = await trader.PostAsJsonAsync(Url("password-reset/confirm"), new { token, password = "a-new-password" }, TestContext.Current.CancellationToken);
        var accounts = await trader.GetFromJsonAsync<JsonElement>(Url("accounts"), TestContext.Current.CancellationToken);
        var used = await CheckAsync(trader, "password-reset/check", token);
        using var again = await trader.PostAsJsonAsync(Url("password-reset/confirm"), new { token, password = "a-third-password" }, TestContext.Current.CancellationToken);
        using var other = factory.CreatePortalClient();
        using var login = await other.PostAsJsonAsync(Url("login"), new { email = "anna@test.example", password = "a-new-password" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.Contains("http://localhost:3002/reset-password?token=", email.Body, StringComparison.Ordinal);
        Assert.Contains("1 hour", email.Body, StringComparison.Ordinal);
        Assert.Equal(("Valid", "anna@test.example", false), valid);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, HttpStatusCode.OK), (tooShort.StatusCode, confirmed.StatusCode));
        Assert.Equal(id, Assert.Single(accounts.EnumerateArray()).GetProperty("account").GetProperty("id").GetGuid());
        Assert.Equal(("Used", "anna@test.example", true), used);
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.OK), (again.StatusCode, login.StatusCode));
    }

    // The answer is the same, so it never tells who has an account with the firm.
    [Fact]
    public async Task AnUnknownEmailGetsTheSameAnswerAndNoEmail()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var trader = factory.CreatePortalClient();

        using var unknown = await trader.PostAsJsonAsync(Url("password-reset"), new { email = "nobody@test.example" }, TestContext.Current.CancellationToken);
        using var empty = await trader.PostAsJsonAsync(Url("password-reset"), new { email = "" }, TestContext.Current.CancellationToken);
        using var noLink = await trader.PostAsJsonAsync(Url("password-reset/check"), new { token = "not-a-token" }, TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.NotFound), (unknown.StatusCode, empty.StatusCode, noLink.StatusCode));
        Assert.Equal(0L, await factory.ScalarAsync("select count(*) from email_outbox"));
    }

    [Fact]
    public async Task TheLinkWorksForAnHour()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        await factory.StartActiveAccountAsync();
        using var trader = factory.CreatePortalClient();
        (await trader.PostAsJsonAsync(Url("password-reset"), new { email = "anna@test.example" }, TestContext.Current.CancellationToken)).Dispose();
        var token = FakeEmailSender.TokenIn(await factory.Emails.WaitForAsync("anna@test.example", "Choose a new password"));

        await factory.AdvanceAsync(TimeSpan.FromMinutes(61));
        var expired = await CheckAsync(trader, "password-reset/check", token);
        using var confirmed = await trader.PostAsJsonAsync(Url("password-reset/confirm"), new { token, password = "a-new-password" }, TestContext.Current.CancellationToken);

        Assert.Equal("Expired", expired.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, confirmed.StatusCode);
    }

    // Whoever knew the old password, for example on a shared computer, is logged out.
    [Fact]
    public async Task ANewPasswordLogsOutTheOtherSessions()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var before = await factory.LogInAsTraderAsync(id);
        using var trader = factory.CreatePortalClient();
        (await trader.PostAsJsonAsync(Url("password-reset"), new { email = "anna@test.example" }, TestContext.Current.CancellationToken)).Dispose();
        var token = FakeEmailSender.TokenIn(await factory.Emails.WaitForAsync("anna@test.example", "Choose a new password"));

        using var loggedIn = await before.GetAsync(Url("me"), TestContext.Current.CancellationToken);
        using var confirmed = await trader.PostAsJsonAsync(Url("password-reset/confirm"), new { token, password = "a-new-password" }, TestContext.Current.CancellationToken);
        using var loggedOut = await before.GetAsync(Url("me"), TestContext.Current.CancellationToken);
        using var stillIn = await trader.GetAsync(Url("me"), TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (loggedIn.StatusCode, confirmed.StatusCode));
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.OK), (loggedOut.StatusCode, stillIn.StatusCode));
    }

    // A link from one firm's email does nothing on another firm's portal, and is still good on its own.
    [Fact]
    public async Task AnAdministratorChoosesANewPasswordOnTheFirmsPortalOnly()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var owner = await factory.SignUpAsync("acme");
        using var acme = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        using var demo = factory.CreatePortalClient();

        using var asked = await acme.PostAsJsonAsync(Url("admin/password-reset"), new { email = "owner@firm.test" }, TestContext.Current.CancellationToken);
        var email = await factory.Emails.WaitForAsync("owner@firm.test", "Choose a new password");
        var token = FakeEmailSender.TokenIn(email);
        using var otherFirmCheck = await demo.PostAsJsonAsync(Url("admin/password-reset/check"), new { token }, TestContext.Current.CancellationToken);
        using var otherFirm = await demo.PostAsJsonAsync(Url("admin/password-reset/confirm"), new { token, password = "a-new-password" }, TestContext.Current.CancellationToken);
        using var asTrader = await acme.PostAsJsonAsync(Url("password-reset/confirm"), new { token, password = "a-new-password" }, TestContext.Current.CancellationToken);
        var valid = await CheckAsync(acme, "admin/password-reset/check", token);
        using var confirmed = await acme.PostAsJsonAsync(Url("admin/password-reset/confirm"), new { token, password = "a-new-password" }, TestContext.Current.CancellationToken);
        var me = await acme.GetFromJsonAsync<JsonElement>(Url("admin/me"), TestContext.Current.CancellationToken);
        using var oldSession = await owner.GetAsync(Url("admin/me"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.Contains("http://acme.localhost:3002/admin/reset-password?token=", email.Body, StringComparison.Ordinal);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (otherFirmCheck.StatusCode, otherFirm.StatusCode, asTrader.StatusCode));
        Assert.Equal(("Valid", "owner@firm.test", true), valid);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(("owner@firm.test", "admin"), (me.GetProperty("email").GetString(), me.GetProperty("role").GetString()));
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
    }

    [Fact]
    public async Task OurStaffChooseANewPasswordOnOurAdminView()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var ops = factory.CreatePortalClient(PropFactory.OpsHost);
        using var firm = factory.CreatePortalClient();

        using var onAFirm = await firm.PostAsJsonAsync(Url("ops/password-reset"), new { email = PropFactory.StaffEmail }, TestContext.Current.CancellationToken);
        using var asked = await ops.PostAsJsonAsync(Url("ops/password-reset"), new { email = PropFactory.StaffEmail }, TestContext.Current.CancellationToken);
        var email = await factory.Emails.WaitForAsync(PropFactory.StaffEmail, "Choose a new password");
        var token = FakeEmailSender.TokenIn(email);
        var valid = await CheckAsync(ops, "ops/password-reset/check", token);
        using var confirmed = await ops.PostAsJsonAsync(Url("ops/password-reset/confirm"), new { token, password = "a-new-staff-password" }, TestContext.Current.CancellationToken);
        using var me = await ops.GetAsync(Url("ops/me"), TestContext.Current.CancellationToken);
        using var other = factory.CreatePortalClient(PropFactory.OpsHost);
        using var oldPassword = await other.PostAsJsonAsync(Url("ops/login"), new { email = PropFactory.StaffEmail, password = PropFactory.StaffPassword }, TestContext.Current.CancellationToken);
        using var newPassword = await other.PostAsJsonAsync(Url("ops/login"), new { email = PropFactory.StaffEmail, password = "a-new-staff-password" }, TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.Accepted), (onAFirm.StatusCode, asked.StatusCode));
        Assert.Single(factory.Emails.Sent);
        Assert.Contains("/ops/reset-password?token=", email.Body, StringComparison.Ordinal);
        Assert.Equal(("Valid", PropFactory.StaffEmail, true), valid);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (confirmed.StatusCode, me.StatusCode));
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.OK), (oldPassword.StatusCode, newPassword.StatusCode));
    }

    // So a used invitation says so, and offers to log in, before anyone types a password.
    [Fact]
    public async Task AnInvitationIsCheckedWhenItIsOpened()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        var token = await factory.InviteAsync(id);
        using var trader = factory.CreatePortalClient();
        using var otherFirm = factory.CreatePortalClient(PropFactory.OtherFirmHost);

        var fresh = await CheckAsync(trader, "invites/check", token);
        using var elsewhere = await otherFirm.PostAsJsonAsync(Url("invites/check"), new { token }, TestContext.Current.CancellationToken);
        (await trader.PostAsJsonAsync(Url("invites/accept"), new { token, password = PropFactory.TraderPassword }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var used = await CheckAsync(trader, "invites/check", token);

        Assert.Equal(("Valid", "anna@test.example", false), fresh);
        Assert.Equal(HttpStatusCode.NotFound, elsewhere.StatusCode);
        Assert.Equal(("Used", "anna@test.example", true), used);
    }

    [Fact]
    public async Task AnInvitationToAdministerIsCheckedWhenItIsOpened()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var owner = await factory.SignUpAsync("acme");
        (await owner.PostAsJsonAsync(Url("admin/admins/invites"), new { email = "second@firm.test" }, TestContext.Current.CancellationToken)).Dispose();
        var token = factory.Emails.TokenFor("second@firm.test");
        using var acme = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        using var demo = factory.CreatePortalClient();

        var fresh = await CheckAsync(acme, "admin/invites/check", token);
        using var elsewhere = await demo.PostAsJsonAsync(Url("admin/invites/check"), new { token }, TestContext.Current.CancellationToken);
        (await acme.PostAsJsonAsync(Url("admin/invites/accept"), new { token, password = "a-second-password" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var used = await CheckAsync(acme, "admin/invites/check", token);

        Assert.Equal(("Valid", "second@firm.test", false), fresh);
        Assert.Equal(HttpStatusCode.NotFound, elsewhere.StatusCode);
        Assert.Equal(("Used", "second@firm.test", true), used);
    }

    // For an administrator who does not remember the firm's address: one login link for each firm the email administers.
    [Fact]
    public async Task TheFrontPageEmailsALoginLinkForEachFirm()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var acme = await factory.SignUpAsync("acme", "anna@firm.test");
        using var beta = await factory.SignUpAsync("beta", "anna@firm.test");
        using var platform = factory.CreatePlatformClient();
        using var firm = factory.CreatePortalClient();

        using var onAFirm = await firm.PostAsJsonAsync(Url("login-help"), new { email = "anna@firm.test" }, TestContext.Current.CancellationToken);
        using var unknown = await platform.PostAsJsonAsync(Url("login-help"), new { email = "nobody@firm.test" }, TestContext.Current.CancellationToken);
        using var asked = await platform.PostAsJsonAsync(Url("login-help"), new { email = "Anna@Firm.test" }, TestContext.Current.CancellationToken);
        var email = await factory.Emails.WaitForAsync("anna@firm.test", "Log in to your firms");
        var links = System.Text.RegularExpressions.Regex.Matches(email.Body, @"(http://\S+/admin/welcome\?token=\S+)").Select(m => new Uri(m.Value)).ToList();
        using var welcomed = await factory.WelcomeAsync(links.Single(l => l.Host == PropFactory.HostOf("beta")));
        var me = await welcomed.GetFromJsonAsync<JsonElement>(Url("admin/me"), TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.Accepted, HttpStatusCode.Accepted), (onAFirm.StatusCode, unknown.StatusCode, asked.StatusCode));
        Assert.Single(factory.Emails.Sent, e => e.Subject.StartsWith("Log in to", StringComparison.Ordinal));
        Assert.Equal([PropFactory.HostOf("acme"), PropFactory.HostOf("beta")], links.Select(l => l.Host));
        Assert.Contains("Firm acme", email.Body, StringComparison.Ordinal);
        Assert.Equal(("anna@firm.test", "Firm beta"), (me.GetProperty("email").GetString(), me.GetProperty("firmName").GetString()));
    }

    private static async Task<(string Status, string Email, bool HasPassword)> CheckAsync(HttpClient client, string path, string token)
    {
        using var response = await client.PostAsJsonAsync(Url(path), new { token }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var check = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return (check.GetProperty("status").GetString()!, check.GetProperty("email").GetString()!, check.GetProperty("hasPassword").GetBoolean());
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);
}
