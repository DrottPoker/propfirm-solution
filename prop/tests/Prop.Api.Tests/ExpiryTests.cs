using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>Challenges that run out of time: after days without a new position, or at a stage's time limit.</summary>
public sealed class ExpiryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Phase1 = "demo-firm-1001-1";

    [Fact]
    public async Task AChallengeWithoutANewPositionFor30DaysExpiresAndClosesItsAccount()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithWebhook());
        var started = await factory.StartActiveAccountAsync();
        var id = started.GetProperty("id").GetGuid();

        // The stage started on Monday 5 October, so 4 November is the last day to open a position.
        await factory.AdvanceAsync(TimeSpan.FromDays(30));
        await Eventually.ThatAsync(() => factory.Trading.Commands.Count(c => c == $"floor {Phase1} daily") == 2, "the daily floor of 4 November");
        var lastDay = await factory.GetAccountAsync(id);
        await factory.AdvanceAsync(TimeSpan.FromDays(1));
        await factory.WaitForAccountAsync(id, a => a.GetProperty("status").GetString() == "Failed");

        Assert.Equal(("2026-11-05", JsonValueKind.Null), (started.GetProperty("inactivityDeadline").GetString(), started.GetProperty("stageDeadline").ValueKind));
        Assert.Equal("Active", lastDay.GetProperty("status").GetString());
        await Eventually.ThatAsync(() => factory.Trading.AccountOf(Phase1).Disabled, "the trading account to be closed");
        var expired = await WebhookAsync(factory, "account.expired");
        Assert.Equal(("Inactivity", "2026-11-05", 0), (expired.GetProperty("data").GetProperty("reason").GetString(), expired.GetProperty("data").GetProperty("day").GetString(), expired.GetProperty("data").GetProperty("stage").GetInt32()));
        using var admin = await factory.LogInAsAdminAsync();
        var details = await admin.GetFromJsonAsync<JsonElement>(new Uri($"/api/portal/admin/accounts/{id}", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(("Inactivity", JsonValueKind.Null), (details.GetProperty("expiry").GetProperty("reason").GetString(), details.GetProperty("breach").ValueKind));
    }

    // The service cannot read the trading platform's events when the day ends, for example after a restart.
    [Fact]
    public async Task ATradeOnTheLastDayCountsEvenWhenItsEventArrivesLate()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        await factory.AdvanceAsync(TimeSpan.FromDays(30));
        await Eventually.ThatAsync(() => factory.Trading.Commands.Count(c => c == $"floor {Phase1} daily") == 2, "the daily floor of 4 November");

        factory.Trading.FailNext(1_000_000);
        factory.Trading.OpenPosition(Phase1);
        await factory.AdvanceAsync(TimeSpan.FromDays(1));
        await Task.Delay(500, TestContext.Current.CancellationToken);
        var whileBehind = await factory.GetAccountAsync(id);
        factory.Trading.FailNext(0);
        await factory.AdvanceAsync(TimeSpan.FromMinutes(1));
        var account = await factory.WaitForAccountAsync(id, a => a.GetProperty("inactivityDeadline").GetString() == "2026-12-05");

        Assert.Equal("Active", whileBehind.GetProperty("status").GetString());
        Assert.Equal("Active", account.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ANewPositionMovesTheLastDayToTrade()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();

        await factory.AdvanceAsync(TimeSpan.FromDays(20));
        factory.Trading.OpenPosition(Phase1);
        var account = await factory.WaitForAccountAsync(id, a => a.GetProperty("inactivityDeadline").GetString() != "2026-11-05");

        Assert.Equal("2026-11-25", account.GetProperty("inactivityDeadline").GetString());
    }

    [Fact]
    public async Task AStageWithATimeLimitExpiresWhenItRunsOut()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        await SaveLimitedChallengeAsync(factory, "limited-100k", maxDays: 10);
        var started = await factory.StartActiveAccountAsync(challengeId: "limited-100k");
        var id = started.GetProperty("id").GetGuid();

        await factory.AdvanceAsync(TimeSpan.FromDays(11));
        await factory.WaitForAccountAsync(id, a => a.GetProperty("status").GetString() == "Failed");

        Assert.Equal("2026-10-16", started.GetProperty("stageDeadline").GetString());
        using var firm = factory.CreateFirmClient();
        var history = await firm.GetFromJsonAsync<JsonElement>(new Uri($"/api/firm/v1/accounts/{id}/history", UriKind.Relative), TestContext.Current.CancellationToken);
        var expiry = history.EnumerateArray().SelectMany(s => s.GetProperty("outputs").EnumerateArray()).Single(o => o.GetProperty("kind").GetString() == "ChallengeExpired");
        Assert.Equal(("TimeLimit", "2026-10-16"), (expiry.GetProperty("reason").GetString(), expiry.GetProperty("day").GetString()));
    }

    [Fact]
    public async Task ATimeLimitShorterThanTheTradingDaysIsRefused()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());

        using var response = await PutLimitedChallengeAsync(factory, "too-short", maxDays: 3);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("time limit of at least its minimum trading days", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    /// <summary>The development firm's two-step challenge with a time limit on both evaluation stages.</summary>
    private static async Task SaveLimitedChallengeAsync(PropFactory factory, string id, int maxDays)
    {
        using var response = await PutLimitedChallengeAsync(factory, id, maxDays);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PutLimitedChallengeAsync(PropFactory factory, string id, int maxDays)
    {
        using var firm = factory.CreateFirmClient();
        var challenges = await firm.GetFromJsonAsync<JsonArray>(new Uri("/api/firm/v1/challenges", UriKind.Relative), TestContext.Current.CancellationToken);
        var challenge = challenges!.Single(c => c!["id"]!.GetValue<string>() == "two-step-100k")!.DeepClone().AsObject();
        challenge["id"] = id;
        foreach (var stage in challenge["evaluation"]!.AsArray())
        {
            stage!["maxDays"] = maxDays;
        }

        return await firm.PutAsJsonAsync(new Uri($"/api/firm/v1/challenges/{id}", UriKind.Relative), challenge, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> WebhookAsync(PropFactory factory, string eventType)
    {
        await Eventually.ThatAsync(() => factory.Webhooks.Delivered(eventType).Count > 0, $"the {eventType} webhook");
        return factory.Webhooks.Delivered(eventType)[0].Json;
    }
}
