using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Prop.Api.Challenges;
using Prop.Api.Firms;
using Prop.Api.Tests.Support;
using Prop.Api.Trading;

namespace Prop.Api.Tests;

/// <summary>Challenges from start to funded account, driven by the trading platform's events.</summary>
public sealed class ChallengeFlowTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // The development challenge: 10 % and 5 % targets, 4 trading days, 5 % daily and 10 % max loss.
    private const string Phase1 = "demo-firm-1001-1";

    [Fact]
    public async Task TheDailyFloorStartsFromTheBalanceAtMidnightInStockholm()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        factory.Trading.OpenPosition(Phase1);
        factory.Trading.ClosePosition(Phase1, 2_000m);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("balance").GetDecimal() == 102_000m);

        // 22:00 UTC is midnight in Stockholm in October.
        await factory.AdvanceAsync(TimeSpan.FromHours(14));
        var account = await factory.WaitForAccountAsync(id, a => a.GetProperty("dailyFloor").GetDecimal() == 97_000m);

        Assert.Equal(2, factory.Trading.Commands.Count(c => c == $"floor {Phase1} daily"));
        Assert.Equal(90_000m, account.GetProperty("maxLossFloor").GetDecimal());
    }

    // The terminal names the account as the portal does, with the stage's target, the trading day's time zone and the way back.
    [Fact]
    public async Task TheTerminalShowsTheAccountAsThePortalNamesIt()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();

        await Eventually.ThatAsync(() => factory.Trading.DetailsOf(Phase1) is not null, "the account to be described");
        var details = factory.Trading.DetailsOf(Phase1)!;

        Assert.Equal(("#1001 Two-step 100K · Phase 1", 110_000m, "Europe/Stockholm"), (details.Label, details.ProfitTarget, details.TimeZone));
        Assert.Equal(new Uri($"http://localhost:3002/accounts/{id}"), details.DetailsUrl);
    }

    // The terminal shows the trading days and deadlines, and is told again only when they change (ADR 0052).
    [Fact]
    public async Task TheTerminalKnowsTheRulesAndWhenTheyChange()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        await factory.StartActiveAccountAsync();
        await Eventually.ThatAsync(() => factory.Trading.RulesOf(Phase1) is not null, "the rules to be told");
        var started = factory.Trading.RulesOf(Phase1)!;

        // Into the next trading day, which changes nothing the terminal shows, and a position on it.
        await factory.AdvanceAsync(TimeSpan.FromDays(1));
        factory.Trading.OpenPosition(Phase1);
        await Eventually.ThatAsync(() => factory.Trading.RulesOf(Phase1)!.TradingDaysCounted == 1, "the trading day to be told");
        var traded = factory.Trading.RulesOf(Phase1)!;

        // 31 days after the first day, at midnight in Stockholm, which is on winter time by then.
        Assert.Equal(new TradingAccountRules(false, 4, 0, null, new DateTimeOffset(2026, 11, 4, 23, 0, 0, TimeSpan.Zero), null, null), started);
        Assert.Equal(started with { TradingDaysCounted = 1, OpenPositionBy = new DateTimeOffset(2026, 11, 5, 23, 0, 0, TimeSpan.Zero) }, traded);
        Assert.Equal(2, factory.Trading.Commands.Count(c => c == $"rules {Phase1}"));
    }

    [Fact]
    public async Task AnAccountOpenedBeforeTheTerminalCouldShowItIsDescribedOnce()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        await factory.StartActiveAccountAsync();
        await Eventually.ThatAsync(() => factory.Trading.DetailsOf(Phase1) is not null, "the account to be described");
        var service = factory.Services.GetRequiredService<ChallengeService>();
        var firm = factory.Services.GetRequiredService<FirmCatalog>().ById("demo-firm")!;

        var alreadyDescribed = await service.DescribeOpenAccountsAsync(firm, TestContext.Current.CancellationToken);
        await factory.ScalarAsync("update challenge_accounts set described_account_id = null");
        var describedAgain = await service.DescribeOpenAccountsAsync(firm, TestContext.Current.CancellationToken);
        await factory.ScalarAsync("update challenge_accounts set described_rules = null");
        var rulesToldAgain = await service.DescribeOpenAccountsAsync(firm, TestContext.Current.CancellationToken);
        var thenNot = await service.DescribeOpenAccountsAsync(firm, TestContext.Current.CancellationToken);

        Assert.Equal((0, 1, 1, 0), (alreadyDescribed, describedAgain, rulesToldAgain, thenNot));
        await Eventually.ThatAsync(() => factory.Trading.Commands.Count(c => c == $"describe {Phase1}") == 2, "the account to be described again");
        await Eventually.ThatAsync(() => factory.Trading.Commands.Count(c => c == $"rules {Phase1}") == 2, "the rules to be told again");
    }

    [Fact]
    public async Task PassingPhaseOneClosesItsAccountOpensPhaseTwoAndTellsTheFirm()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithWebhook());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();

        await PassStageAsync(factory, id, Phase1, stage: 0, finalProfit: 7_000m);
        var account = await factory.WaitForAccountAsync(id, a => a.GetProperty("status").GetString() == "Active" && a.GetProperty("stage").GetInt32() == 1);

        Assert.Equal(("Phase 2", "demo-firm-1001-2", 105_000m), (account.GetProperty("stageName").GetString(), account.GetProperty("tradingAccountId").GetString(), account.GetProperty("profitTarget").GetDecimal()));
        await Eventually.ThatAsync(() => factory.Trading.AccountOf(Phase1).Disabled, "phase 1's account to be closed");
        var passed = await WebhookAsync(factory, "account.passed");
        Assert.Equal((0, 110_000m, 4), (passed.GetProperty("data").GetProperty("stage").GetInt32(), passed.GetProperty("data").GetProperty("balance").GetDecimal(), passed.GetProperty("data").GetProperty("tradingDays").GetInt32()));
        Assert.Equal(id, passed.GetProperty("account").GetProperty("id").GetGuid());
        var email = await factory.Emails.WaitForAsync("anna@test.example", "You passed Phase 1 of your Two-step 100K");
        Assert.Contains("Phase 2 starts now", email.Body, StringComparison.Ordinal);
        Assert.Contains($"http://localhost:3002/accounts/{id}", email.Body, StringComparison.Ordinal);
    }

    // The firm may send its own emails, so each kind of ours can be turned off.
    [Fact]
    public async Task AnEmailTheFirmTurnedOffIsNotSent()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithWebhook());
        using var admin = await factory.LogInAsAdminAsync();
        using var unknown = await admin.PutAsJsonAsync(new Uri("/api/portal/admin/firm/email-settings", UriKind.Relative), new { settings = new { noSuchEmail = false } }, TestContext.Current.CancellationToken);
        using var saved = await admin.PutAsJsonAsync(new Uri("/api/portal/admin/firm/email-settings", UriKind.Relative), new { settings = new { traderStagePassed = false } }, TestContext.Current.CancellationToken);
        var settings = (await saved.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("emailSettings");
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();

        await PassStageAsync(factory, id, Phase1, stage: 0, finalProfit: 7_000m);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("stage").GetInt32() == 1);

        Assert.Equal((HttpStatusCode.UnprocessableEntity, HttpStatusCode.OK), (unknown.StatusCode, saved.StatusCode));
        Assert.Equal((false, true), (settings.GetProperty("traderStagePassed").GetBoolean(), settings.GetProperty("traderEnded").GetBoolean()));
        Assert.Equal(13, settings.EnumerateObject().Count());
        Assert.Equal(0L, await factory.ScalarAsync("select count(*) from email_outbox where kind = 'traderStagePassed'"));
    }

    // Reminded once, a few days before the challenge would end, and not again however often the worker looks.
    [Fact]
    public async Task ATraderWithoutANewTradeIsRemindedBeforeTheChallengeEnds()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        await factory.StartActiveAccountAsync();
        const string subject = "Open a trade by 4 Nov 2026";

        // The last day to open a trade is 4 November, so the reminder comes on 1 November, which starts at 23:00 UTC.
        await factory.AdvanceUntilAsync(TimeSpan.FromDays(25), () => factory.Emails.Sent.Any(e => e.Subject.StartsWith(subject, StringComparison.Ordinal)), "the reminder", TimeSpan.FromHours(1));
        var remindedAt = factory.Time.GetUtcNow();
        for (var hour = 0; hour < 24; hour++)
        {
            await factory.AdvanceAsync(TimeSpan.FromHours(1));
        }

        Assert.InRange(remindedAt, new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero));
        var reminder = Assert.Single(factory.Emails.Sent, e => e.Subject.StartsWith(subject, StringComparison.Ordinal));
        Assert.Equal("anna@test.example", reminder.To);
    }

    [Fact]
    public async Task ABreachFailsTheAccountWithTheTradingPlatformsEvidence()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithWebhook());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();

        factory.Trading.Breach(Phase1, "daily", 94_900m);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("status").GetString() == "Failed");

        using var firm = factory.CreateFirmClient();
        var history = await firm.GetFromJsonAsync<JsonElement>(new Uri($"/api/firm/v1/accounts/{id}/history", UriKind.Relative), TestContext.Current.CancellationToken);
        var breach = history.EnumerateArray().Single(s => s.GetProperty("input").GetProperty("kind").GetString() == "FloorBreached");
        Assert.Equal("EquityFloorBreached", breach.GetProperty("sourceEvent").GetProperty("kind").GetString());
        Assert.Equal("ChallengeFailed", breach.GetProperty("outputs")[0].GetProperty("kind").GetString());

        var breached = await WebhookAsync(factory, "account.breached");
        Assert.Equal(("DailyLoss", 95_000m, 94_900m), (breached.GetProperty("data").GetProperty("reason").GetString(), breached.GetProperty("data").GetProperty("level").GetDecimal(), breached.GetProperty("data").GetProperty("equity").GetDecimal()));
    }

    [Fact]
    public async Task AfterBothPhasesTheFirmApprovesTheFundedAccount()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        await PassStageAsync(factory, id, Phase1, stage: 0, finalProfit: 7_000m);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("stage").GetInt32() == 1 && a.GetProperty("dailyFloor").ValueKind == JsonValueKind.Number);
        await PassStageAsync(factory, id, "demo-firm-1001-2", stage: 1, finalProfit: 2_000m);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("status").GetString() == "AwaitingFunding");
        using var firm = factory.CreateFirmClient();

        using var approved = await firm.PostAsync(new Uri($"/api/firm/v1/accounts/{id}/approve-funding", UriKind.Relative), null, TestContext.Current.CancellationToken);
        var funded = await factory.WaitForAccountAsync(id, a => a.GetProperty("status").GetString() == "Active");
        using var again = await firm.PostAsync(new Uri($"/api/firm/v1/accounts/{id}/approve-funding", UriKind.Relative), null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal((true, "demo-firm-1001-3", JsonValueKind.Null), (funded.GetProperty("funded").GetBoolean(), funded.GetProperty("tradingAccountId").GetString(), funded.GetProperty("profitTarget").ValueKind));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        await factory.Emails.WaitForAsync(PropFactory.AdminEmail, "anna@test.example passed Two-step 100K");
        await factory.Emails.WaitForAsync("anna@test.example", "You passed your Two-step 100K");
        await factory.Emails.WaitForAsync("anna@test.example", "Your funded account with");
    }

    [Fact]
    public async Task CancellingClosesTheTradingAccount()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var firm = factory.CreateFirmClient();

        using var response = await firm.PostAsJsonAsync(new Uri($"/api/firm/v1/accounts/{id}/cancel", UriKind.Relative), new { reason = "Refunded" }, TestContext.Current.CancellationToken);

        Assert.Equal("Cancelled", (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("status").GetString());
        await Eventually.ThatAsync(() => factory.Trading.AccountOf(Phase1).Disabled, "the trading account to be closed");
    }

    [Fact]
    public async Task CommandsWaitOutAnOutageAndKeepTheirOrder()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        factory.Trading.FailNext(3);
        using var firm = factory.CreateFirmClient();

        using var response = await firm.PostAsJsonAsync(
            new Uri("/api/firm/v1/accounts", UriKind.Relative),
            new { email = "anna@test.example", challengeId = "two-step-100k" },
            TestContext.Current.CancellationToken);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();

        // The worker waits longer after each failure, on the test's clock.
        await Eventually.ThatAsync(
            async () =>
            {
                factory.Time.Advance(TimeSpan.FromSeconds(1));
                var account = await factory.GetAccountAsync(id);
                return account.GetProperty("dailyFloor").ValueKind == JsonValueKind.Number;
            },
            "the account to open after the outage");

        Assert.Equal(
            ["user anna@test.example", "open demo-firm-1001-1", "describe demo-firm-1001-1", "floor demo-firm-1001-1 max-loss", "floor demo-firm-1001-1 daily", "rules demo-firm-1001-1"],
            factory.Trading.Commands);
    }

    [Fact]
    public async Task AfterARestartEveryEventIsStillHandledExactlyOnce()
    {
        var database = await postgres.CreateDatabaseAsync();
        PropFactory restarted;
        Guid id;
        await using (var factory = PropFactory.Create(database))
        {
            id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
            factory.Trading.OpenPosition(Phase1);
            await factory.WaitForAccountAsync(id, a => a.GetProperty("openPositions").GetInt32() == 1);
            restarted = factory.Restart();
        }

        await using (restarted)
        {
            restarted.Trading.ClosePosition(Phase1, 1_000m);
            var account = await restarted.WaitForAccountAsync(id, a => a.GetProperty("balance").GetDecimal() == 101_000m);

            // Read again from the start, the open position would count twice and still be open.
            Assert.Equal((0, 1), (account.GetProperty("openPositions").GetInt32(), account.GetProperty("tradingDays").GetInt32()));
        }
    }

    [Fact]
    public async Task WebhooksAreSignedAndTriedAgainUntilTheFirmAcceptsThem()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithWebhook());
        factory.Webhooks.AnswerWith(HttpStatusCode.InternalServerError);
        using var firm = factory.CreateFirmClient();
        await firm.PostAsJsonAsync(
            new Uri("/api/firm/v1/accounts", UriKind.Relative),
            new { email = "anna@test.example", challengeId = "two-step-100k" },
            TestContext.Current.CancellationToken);

        await Eventually.ThatAsync(
            () =>
            {
                factory.Time.Advance(TimeSpan.FromSeconds(10));
                return factory.Webhooks.Delivered("account.stage_started").Count == 1;
            },
            "the webhook to be delivered after a failed try");

        var attempts = factory.Webhooks.Received.Where(r => r.EventType == "account.stage_started").ToList();
        Assert.Equal([HttpStatusCode.InternalServerError, HttpStatusCode.OK], attempts.Select(a => a.Status));
        Assert.All(attempts, a => Assert.True(IsSignedWithTheFirmsSecret(a)));
        Assert.Equal(attempts[0].Body, attempts[1].Body);
    }

    /// <summary>Trades on four days, a profit of 1 000 a day and <paramref name="finalProfit"/> on the last, enough for the target.</summary>
    private static async Task PassStageAsync(PropFactory factory, Guid id, string tradingAccountId, int stage, decimal finalProfit)
    {
        for (var day = 1; day <= 4; day++)
        {
            if (day > 1)
            {
                await NextTradingDayAsync(factory);
            }

            factory.Trading.OpenPosition(tradingAccountId);
            factory.Trading.ClosePosition(tradingAccountId, day < 4 ? 1_000m : finalProfit);
            var days = day;
            await factory.WaitForAccountAsync(id, a => a.GetProperty("stage").GetInt32() > stage || a.GetProperty("tradingDays").GetInt32() == days);
        }
    }

    // To the next midnight in Stockholm, then a few hours into the day.
    private static async Task NextTradingDayAsync(PropFactory factory)
    {
        var now = factory.Time.GetUtcNow();
        var midnight = new DateTimeOffset(now.Date.AddHours(22), TimeSpan.Zero);
        await factory.AdvanceAsync((now < midnight ? midnight : midnight.AddDays(1)) - now + TimeSpan.FromHours(10));
    }

    private static async Task<JsonElement> WebhookAsync(PropFactory factory, string eventType)
    {
        await Eventually.ThatAsync(() => factory.Webhooks.Delivered(eventType).Count > 0, $"the {eventType} webhook");
        return factory.Webhooks.Delivered(eventType)[0].Json;
    }

    private static bool IsSignedWithTheFirmsSecret(Received webhook)
    {
        var parts = webhook.Signature.Split(',').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(PropFactory.WebhookSecret), Encoding.UTF8.GetBytes($"{parts["t"]}.{webhook.Body}"));
        return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(parts["v1"]));
    }
}
