using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>When each of the firm's administrators last logged in, which the Team page shows: every way in counts, nothing else does.</summary>
public sealed class AdminLastLoginTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Owner = "owner@firm.test";
    private const string Second = "second@firm.test";

    [Fact]
    public async Task EachWayAnAdministratorLogsInIsKept()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var start = PropFactory.Start;

        // The link after signing up.
        using var owner = await factory.SignUpAsync("acme", Owner);
        var afterWelcome = await LastLoginsAsync(owner);

        // An invitation.
        (await owner.PostAsJsonAsync(Url("admin/admins/invites"), new { email = Second }, TestContext.Current.CancellationToken)).Dispose();
        await factory.AdvanceAsync(TimeSpan.FromHours(1));
        using var second = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        using var accepted = await second.PostAsJsonAsync(
            Url("admin/invites/accept"), new { token = factory.Emails.TokenFor(Second), password = "a-second-password" }, TestContext.Current.CancellationToken);
        var afterInvite = await LastLoginsAsync(owner);

        // The password, and a wrong one, which does not count.
        await factory.AdvanceAsync(TimeSpan.FromHours(1));
        using var again = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        using var wrong = await again.PostAsJsonAsync(Url("admin/login"), new { email = Owner, password = "a-wrong-password" }, TestContext.Current.CancellationToken);
        using var login = await again.PostAsJsonAsync(Url("admin/login"), new { email = Second, password = "a-second-password" }, TestContext.Current.CancellationToken);
        var afterPassword = await LastLoginsAsync(owner);

        // A new password, which logs in and ends the older sessions.
        await factory.AdvanceAsync(TimeSpan.FromHours(1));
        using var acme = factory.CreatePortalClient(PropFactory.HostOf("acme"));
        (await acme.PostAsJsonAsync(Url("admin/password-reset"), new { email = Owner }, TestContext.Current.CancellationToken)).Dispose();
        var token = FakeEmailSender.TokenIn(await factory.Emails.WaitForAsync(Owner, "Choose a new password"));
        using var reset = await acme.PostAsJsonAsync(Url("admin/password-reset/confirm"), new { token, password = "a-new-password" }, TestContext.Current.CancellationToken);
        var afterReset = await LastLoginsAsync(acme);

        Assert.Equal([(Owner, start)], afterWelcome);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal([(Owner, start), (Second, start.AddHours(1))], afterInvite);
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.OK), (wrong.StatusCode, login.StatusCode));
        Assert.Equal([(Owner, start), (Second, start.AddHours(2))], afterPassword);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal([(Owner, start.AddHours(3)), (Second, start.AddHours(2))], afterReset);
    }

    [Fact]
    public async Task AnAdministratorWhoHasNotLoggedInHasNoTime()
    {
        var settings = new Dictionary<string, string>
        {
            ["Firms:0:SeedAdmins:1:Email"] = "second@test.com",
            ["Firms:0:SeedAdmins:1:Password"] = "a-second-password",
        };
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), settings);
        var accountId = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var trader = await factory.LogInAsTraderAsync(accountId);

        using var admin = await factory.LogInAsAdminAsync();
        var admins = await LastLoginsAsync(admin);

        Assert.Equal([(PropFactory.AdminEmail, PropFactory.Start), ("second@test.com", null)], admins);
        Assert.Equal(1L, await factory.ScalarAsync("select count(*) from firm_admins where last_login_at is not null"));
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    // Each administrator's email and last login, as the Team page lists them.
    private static async Task<List<(string Email, DateTimeOffset? LastLoginAt)>> LastLoginsAsync(HttpClient admin)
    {
        var response = await admin.GetFromJsonAsync<JsonElement>(Url("admin/admins"), TestContext.Current.CancellationToken);
        return
        [
            .. response.GetProperty("admins").EnumerateArray().Select(a =>
            {
                var at = a.GetProperty("lastLoginAt");
                return (a.GetProperty("email").GetString()!, at.ValueKind == JsonValueKind.Null ? (DateTimeOffset?)null : at.GetDateTimeOffset());
            }),
        ];
    }
}
