using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>The API the firm's own systems use: its challenges and its traders' accounts.</summary>
public sealed class FirmApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task TheFirmApiNeedsTheFirmsKey(string? apiKey)
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var client = apiKey is null ? factory.CreateClient() : factory.CreateFirmClient(apiKey);

        using var response = await client.GetAsync(new Uri("/api/firm/v1/challenges", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TheConfiguredChallengesAreThereFromTheStart()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var firm = factory.CreateFirmClient();

        var challenges = await firm.GetFromJsonAsync<JsonElement>(new Uri("/api/firm/v1/challenges", UriKind.Relative), TestContext.Current.CancellationToken);

        var challenge = Assert.Single(challenges.EnumerateArray());
        Assert.Equal("two-step-100k", challenge.GetProperty("id").GetString());
        Assert.Equal(100_000m, challenge.GetProperty("initialBalance").GetDecimal());
    }

    [Fact]
    public async Task TheFirmCreatesItsOwnChallenges()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var firm = factory.CreateFirmClient();
        var definition = ChallengeJson("one-step-50k", 50_000m, "America/New_York");

        using var saved = await firm.PutAsJsonAsync(new Uri("/api/firm/v1/challenges/one-step-50k", UriKind.Relative), definition, TestContext.Current.CancellationToken);
        var challenges = await firm.GetFromJsonAsync<JsonElement>(new Uri("/api/firm/v1/challenges", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(["one-step-50k", "two-step-100k"], challenges.EnumerateArray().Select(c => c.GetProperty("id").GetString()));
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons", "one-step-50k", "Unknown time zone")]
    [InlineData("Europe/Stockholm", "another-id", "The id in the body must be the id in the address.")]
    public async Task InvalidChallengesAreRefused(string timeZone, string id, string error)
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var firm = factory.CreateFirmClient();

        using var response = await firm.PutAsJsonAsync(
            new Uri($"/api/firm/v1/challenges/{id}", UriKind.Relative),
            ChallengeJson("one-step-50k", 50_000m, timeZone),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(error, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAnAccountOpensItsTradingAccountWithTheLossFloors()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var firm = factory.CreateFirmClient();

        using var response = await firm.PostAsJsonAsync(
            new Uri("/api/firm/v1/accounts", UriKind.Relative),
            new { email = "anna@test.example", challengeId = "two-step-100k" },
            TestContext.Current.CancellationToken);
        var started = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var active = await factory.WaitForAccountAsync(
            started.GetProperty("id").GetGuid(),
            a => a.GetProperty("maxLossFloor").ValueKind == JsonValueKind.Number && a.GetProperty("dailyFloor").ValueKind == JsonValueKind.Number);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(("OpeningAccount", 1001L), (started.GetProperty("status").GetString(), started.GetProperty("number").GetInt64()));
        Assert.Equal(("Active", "Phase 1", "demo-firm-1001-1"), (active.GetProperty("status").GetString(), active.GetProperty("stageName").GetString(), active.GetProperty("tradingAccountId").GetString()));
        Assert.Equal((110_000m, 95_000m, 90_000m), (active.GetProperty("profitTarget").GetDecimal(), active.GetProperty("dailyFloor").GetDecimal(), active.GetProperty("maxLossFloor").GetDecimal()));

        // The account is opened before its floors are set.
        Assert.Equal(["user anna@test.example", "open demo-firm-1001-1", "floor demo-firm-1001-1 max-loss", "floor demo-firm-1001-1 daily"], factory.Trading.Commands);
    }

    [Fact]
    public async Task RepeatingTheFirmsReferenceReturnsTheSameAccount()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var firm = factory.CreateFirmClient();
        var request = new { email = "anna@test.example", challengeId = "two-step-100k", reference = "order-17" };

        using var first = await firm.PostAsJsonAsync(new Uri("/api/firm/v1/accounts", UriKind.Relative), request, TestContext.Current.CancellationToken);
        using var repeated = await firm.PostAsJsonAsync(new Uri("/api/firm/v1/accounts", UriKind.Relative), request, TestContext.Current.CancellationToken);
        using var other = await firm.PostAsJsonAsync(
            new Uri("/api/firm/v1/accounts", UriKind.Relative),
            new { email = "anna@test.example", challengeId = "two-step-100k" },
            TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Created), (first.StatusCode, repeated.StatusCode, other.StatusCode));
        Assert.Equal(await IdOf(first), await IdOf(repeated));
        Assert.Equal(1002L, (await other.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("number").GetInt64());
    }

    [Fact]
    public async Task BadStartsAreRefused()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var firm = factory.CreateFirmClient();

        using var unknown = await firm.PostAsJsonAsync(
            new Uri("/api/firm/v1/accounts", UriKind.Relative),
            new { email = "anna@test.example", challengeId = "no-such-challenge" },
            TestContext.Current.CancellationToken);
        using var noEmail = await firm.PostAsJsonAsync(
            new Uri("/api/firm/v1/accounts", UriKind.Relative),
            new { email = "anna", challengeId = "two-step-100k" },
            TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity), (unknown.StatusCode, noEmail.StatusCode));
    }

    [Fact]
    public async Task FirmsCannotSeeEachOthersAccounts()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithOtherFirm());
        var account = await factory.StartActiveAccountAsync();
        using var other = factory.CreateFirmClient(PropFactory.OtherFirmKey);
        var id = account.GetProperty("id").GetGuid();

        using var get = await other.GetAsync(new Uri($"/api/firm/v1/accounts/{id}", UriKind.Relative), TestContext.Current.CancellationToken);
        using var cancel = await other.PostAsJsonAsync(new Uri($"/api/firm/v1/accounts/{id}/cancel", UriKind.Relative), new { reason = "x" }, TestContext.Current.CancellationToken);
        var byEmail = await other.GetFromJsonAsync<JsonElement>(new Uri("/api/firm/v1/accounts?email=anna@test.example", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (get.StatusCode, cancel.StatusCode));
        Assert.Empty(byEmail.EnumerateArray());
    }

    [Fact]
    public async Task ALoginLinkOpensTheCurrentTradingAccount()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var account = await factory.StartActiveAccountAsync();
        using var firm = factory.CreateFirmClient();

        var link = await (await firm.PostAsync(
            new Uri($"/api/firm/v1/accounts/{account.GetProperty("id").GetGuid()}/login-link", UriKind.Relative),
            null,
            TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.EndsWith("account=demo-firm-1001-1", link.GetProperty("url").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvitationLeadsToTheFirmsPortal()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var account = await factory.StartActiveAccountAsync();
        using var firm = factory.CreateFirmClient();

        using var response = await firm.PostAsync(
            new Uri($"/api/firm/v1/accounts/{account.GetProperty("id").GetGuid()}/invite", UriKind.Relative),
            null,
            TestContext.Current.CancellationToken);
        var invite = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("http://localhost:3002/invite?token=", invite.GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.Equal(PropFactory.Start.AddDays(7), invite.GetProperty("expiresAt").GetDateTimeOffset());
    }

    private static async Task<Guid> IdOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

    private static object ChallengeJson(string id, decimal initialBalance, string timeZone)
    {
        var stage = new { dailyLoss = new { percent = 4m, reference = "HigherOfBalanceAndEquity" }, maxLoss = new { percent = 8m, kind = "Trailing" } };
        return new
        {
            id,
            name = "One step",
            currency = "USD",
            initialBalance,
            tradingDay = new { timeZone, start = "17:00:00" },
            evaluation = new[] { new { name = "Evaluation", profitTargetPercent = (decimal?)8m, minTradingDays = 3, stage.dailyLoss, stage.maxLoss } },
            funded = new { name = "Funded", profitTargetPercent = (decimal?)null, minTradingDays = 0, stage.dailyLoss, stage.maxLoss },
        };
    }
}
