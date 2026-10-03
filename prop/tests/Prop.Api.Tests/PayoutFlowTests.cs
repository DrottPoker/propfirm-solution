using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>
/// Funded traders' payouts, from the request through the withdrawal on the trading platform to the firm's
/// approval and payment. Uses the development challenge quick-test-100k: 0.1 % targets, no minimum trading
/// days and an 80 % profit split.
/// </summary>
public sealed class PayoutFlowTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Funded = "demo-firm-1001-3";

    [Fact]
    public async Task TheProfitIsWithdrawnAndTheFirmApprovesAndPaysTheTradersShare()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithWebhook());
        var id = await FundedWithProfitAsync(factory, 8_000m);
        using var firm = factory.CreateFirmClient();

        var quote = (await factory.GetAccountAsync(id)).GetProperty("nextPayout");
        using var response = await firm.PostAsync(Url($"accounts/{id}/payouts"), null, TestContext.Current.CancellationToken);
        var payoutId = (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        var pending = await WaitForPayoutAsync(factory, payoutId, "Pending");

        Assert.Equal((true, 8_000m, 80m, 6_400m), (quote.GetProperty("canRequest").GetBoolean(), quote.GetProperty("profit").GetDecimal(), quote.GetProperty("profitSplitPercent").GetDecimal(), quote.GetProperty("amount").GetDecimal()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new Uri($"/api/firm/v1/payouts/{payoutId}", UriKind.Relative), response.Headers.Location);
        Assert.Equal((8_000m, 6_400m, "USD", Funded), (pending.GetProperty("profit").GetDecimal(), pending.GetProperty("amount").GetDecimal(), pending.GetProperty("currency").GetString(), pending.GetProperty("tradingAccountId").GetString()));
        Assert.Equal(100_000m, factory.Trading.AccountOf(Funded).Balance);
        Assert.Contains($"withdraw {Funded} 8000", factory.Trading.Commands);
        var account = await factory.WaitForAccountAsync(id, a => a.GetProperty("balance").GetDecimal() == 100_000m);
        Assert.Equal("A payout is already in progress.", account.GetProperty("nextPayout").GetProperty("refusal").GetString());
        var requested = await WebhookAsync(factory, "payout.requested");
        Assert.Equal((payoutId, 6_400m), (requested.GetProperty("data").GetProperty("payout").GetProperty("id").GetGuid(), requested.GetProperty("data").GetProperty("payout").GetProperty("amount").GetDecimal()));

        var approved = await firm.PostJsonAsync($"payouts/{payoutId}/approve", null);
        var paid = await firm.PostJsonAsync($"payouts/{payoutId}/mark-paid", new { reference = "wire-17" });
        using var paidAgain = await firm.PostAsJsonAsync(Url($"payouts/{payoutId}/mark-paid"), new { reference = "wire-18" }, TestContext.Current.CancellationToken);

        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Equal(("Paid", "wire-17"), (paid.GetProperty("status").GetString(), paid.GetProperty("reference").GetString()));
        Assert.Equal(JsonValueKind.String, paid.GetProperty("paidAt").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, paidAgain.StatusCode);
        Assert.Equal("payout.paid", (await WebhookAsync(factory, "payout.paid")).GetProperty("type").GetString());
        await WebhookAsync(factory, "payout.approved");
        var afterwards = (await factory.GetAccountAsync(id)).GetProperty("nextPayout");
        Assert.Equal("There is no profit to pay out.", afterwards.GetProperty("refusal").GetString());
    }

    [Fact]
    public async Task APayoutIsRefusedWithTheReason()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = await FundedWithProfitAsync(factory, 8_000m);
        var evaluation = (await factory.StartActiveAccountAsync("bert@test.example", "quick-test-100k")).GetProperty("id").GetGuid();
        factory.Trading.OpenPosition(Funded);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("openPositions").GetInt32() == 1);
        using var firm = factory.CreateFirmClient();

        var notFunded = await firm.PostJsonAsync($"accounts/{evaluation}/payouts", null, HttpStatusCode.Conflict);
        var openPosition = await firm.PostJsonAsync($"accounts/{id}/payouts", null, HttpStatusCode.Conflict);

        Assert.Equal("Payouts are only for an active funded account.", notFunded.GetProperty("title").GetString());
        Assert.Equal("Close every position before asking for a payout.", openPosition.GetProperty("title").GetString());
        Assert.Empty(await firm.GetFromJsonAsync<JsonElement[]>(Url($"accounts/{id}/payouts"), TestContext.Current.CancellationToken) ?? []);
    }

    // A position closed at a loss just before the withdrawal, so the trading platform refuses it.
    [Fact]
    public async Task AWithdrawalTheTradingPlatformRefusesFailsThePayoutAndATryAfterwardsWorks()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithWebhook());
        var id = await FundedWithProfitAsync(factory, 8_000m);
        factory.Trading.ChangeBalanceQuietly(Funded, -500m);
        using var firm = factory.CreateFirmClient();

        var first = await firm.PostJsonAsync($"accounts/{id}/payouts", null, HttpStatusCode.Created);
        var failed = await WaitForPayoutAsync(factory, first.GetProperty("id").GetGuid(), "Failed");
        factory.Trading.ClosePosition(Funded, 0m);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("balance").GetDecimal() == 107_500m);
        var second = await firm.PostJsonAsync($"accounts/{id}/payouts", null, HttpStatusCode.Created);
        var pending = await WaitForPayoutAsync(factory, second.GetProperty("id").GetGuid(), "Pending");

        Assert.Equal("InsufficientFunds", failed.GetProperty("reason").GetString());
        Assert.Equal((7_500m, 6_000m), (pending.GetProperty("profit").GetDecimal(), pending.GetProperty("amount").GetDecimal()));
        Assert.Equal(100_000m, factory.Trading.AccountOf(Funded).Balance);
        await WebhookAsync(factory, "payout.requested");
        Assert.Single(factory.Webhooks.Delivered("payout.requested"));
    }

    [Fact]
    public async Task ARejectedPayoutKeepsItsProfitOffTheAccount()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithWebhook());
        var id = await FundedWithProfitAsync(factory, 8_000m);
        using var firm = factory.CreateFirmClient();
        var payoutId = (await firm.PostJsonAsync($"accounts/{id}/payouts", null, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await WaitForPayoutAsync(factory, payoutId, "Pending");

        var rejected = await firm.PostJsonAsync($"payouts/{payoutId}/reject", new { reason = "Copy trading is not allowed." });

        Assert.Equal(("Rejected", "Copy trading is not allowed."), (rejected.GetProperty("status").GetString(), rejected.GetProperty("reason").GetString()));
        Assert.Equal(100_000m, factory.Trading.AccountOf(Funded).Balance);
        var webhook = await WebhookAsync(factory, "payout.rejected");
        Assert.Equal("Copy trading is not allowed.", webhook.GetProperty("data").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task TheTraderAsksInThePortalAndTheAdministratorPays()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = await FundedWithProfitAsync(factory, 8_000m);
        var others = (await factory.StartActiveAccountAsync("bert@test.example", "quick-test-100k")).GetProperty("id").GetGuid();
        using var trader = await factory.LogInAsTraderAsync(id);
        using var admin = await factory.LogInAsAdminAsync();

        using var notMine = await trader.PostAsync(Portal($"accounts/{others}/payouts"), null, TestContext.Current.CancellationToken);
        using var asked = await trader.PostAsync(Portal($"accounts/{id}/payouts"), null, TestContext.Current.CancellationToken);
        var payoutId = (await asked.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        await WaitForPayoutAsync(factory, payoutId, "Pending");
        var waiting = await admin.GetFromJsonAsync<JsonElement>(Portal("admin/payouts?status=Pending&status=Approved"), TestContext.Current.CancellationToken);
        using var approved = await admin.PostAsync(Portal($"admin/payouts/{payoutId}/approve"), null, TestContext.Current.CancellationToken);
        using var paid = await admin.PostAsJsonAsync(Portal($"admin/payouts/{payoutId}/mark-paid"), new { reference = (string?)null }, TestContext.Current.CancellationToken);
        using var traderApproves = await trader.PostAsync(Portal($"admin/payouts/{payoutId}/approve"), null, TestContext.Current.CancellationToken);
        var details = await trader.GetFromJsonAsync<JsonElement>(Portal($"accounts/{id}"), TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.Created), (notMine.StatusCode, asked.StatusCode));
        Assert.Equal((payoutId, "anna@test.example"), (Assert.Single(waiting.EnumerateArray()).GetProperty("id").GetGuid(), waiting[0].GetProperty("email").GetString()));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Unauthorized), (approved.StatusCode, paid.StatusCode, traderApproves.StatusCode));
        var payout = Assert.Single(details.GetProperty("payouts").EnumerateArray());
        Assert.Equal(("Paid", 6_400m), (payout.GetProperty("status").GetString(), payout.GetProperty("amount").GetDecimal()));
    }

    [Fact]
    public async Task AnotherFirmCannotSeeOrDecideThePayouts()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithOtherFirm());
        var id = await FundedWithProfitAsync(factory, 8_000m);
        using var firm = factory.CreateFirmClient();
        using var other = factory.CreateFirmClient(PropFactory.OtherFirmKey);
        var payoutId = (await firm.PostJsonAsync($"accounts/{id}/payouts", null, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        await other.PostJsonAsync($"accounts/{id}/payouts", null, HttpStatusCode.NotFound);
        await other.PostJsonAsync($"payouts/{payoutId}/approve", null, HttpStatusCode.NotFound);
        var list = await other.GetFromJsonAsync<JsonElement>(Url("payouts"), TestContext.Current.CancellationToken);

        Assert.Empty(list.EnumerateArray());
    }

    [Theory]
    [InlineData("payouts?limit=0")]
    [InlineData("payouts?limit=501")]
    public async Task PayoutListsOutsideTheLimitsAreRefused(string query)
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var firm = factory.CreateFirmClient();

        using var response = await firm.GetAsync(Url(query), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>
    /// Passes both quick evaluation stages for anna@test.example, has the firm approve funding and closes a
    /// position with <paramref name="profit"/> on the funded account. Returns the challenge account's id.
    /// </summary>
    private static async Task<Guid> FundedWithProfitAsync(PropFactory factory, decimal profit)
    {
        var id = (await factory.StartActiveAccountAsync(challengeId: "quick-test-100k")).GetProperty("id").GetGuid();
        await PassAsync(factory, id, "demo-firm-1001-1", stage: 0);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("stage").GetInt32() == 1 && a.GetProperty("status").GetString() == "Active");
        await PassAsync(factory, id, "demo-firm-1001-2", stage: 1);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("status").GetString() == "AwaitingFunding");
        using var firm = factory.CreateFirmClient();
        await firm.PostJsonAsync($"accounts/{id}/approve-funding", null);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("funded").GetBoolean() && a.GetProperty("status").GetString() == "Active");

        factory.Trading.OpenPosition(Funded);
        factory.Trading.ClosePosition(Funded, profit);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("balance").GetDecimal() == 100_000m + profit);
        return id;
    }

    // 0.1 % of 100 000 is enough for the quick challenge's target.
    private static async Task PassAsync(PropFactory factory, Guid id, string tradingAccountId, int stage)
    {
        factory.Trading.OpenPosition(tradingAccountId);
        factory.Trading.ClosePosition(tradingAccountId, 100m);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("stage").GetInt32() > stage);
    }

    private static async Task<JsonElement> WaitForPayoutAsync(PropFactory factory, Guid payoutId, string status)
    {
        using var firm = factory.CreateFirmClient();
        JsonElement payout = default;
        await Eventually.ThatAsync(
            async () =>
            {
                payout = await firm.GetFromJsonAsync<JsonElement>(Url($"payouts/{payoutId}"));
                return payout.GetProperty("status").GetString() == status;
            },
            $"payout {payoutId} to be {status}");
        return payout;
    }

    private static async Task<JsonElement> WebhookAsync(PropFactory factory, string eventType)
    {
        await Eventually.ThatAsync(() => factory.Webhooks.Delivered(eventType).Count > 0, $"the {eventType} webhook");
        return factory.Webhooks.Delivered(eventType)[0].Json;
    }

    private static Uri Url(string path) => new($"/api/firm/v1/{path}", UriKind.Relative);

    private static Uri Portal(string path) => new($"/api/portal/{path}", UriKind.Relative);
}

internal static class FirmJson
{
    /// <summary>Posts to the firm API and checks the status. Returns the body, or a default element when there is none.</summary>
    public static async Task<JsonElement> PostJsonAsync(this HttpClient firm, string path, object? body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = body is null
            ? await firm.PostAsync(new Uri($"/api/firm/v1/{path}", UriKind.Relative), null)
            : await firm.PostAsJsonAsync(new Uri($"/api/firm/v1/{path}", UriKind.Relative), body);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected} but got {response.StatusCode}: {content}");
        if (content.Length == 0)
        {
            return default;
        }

        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }
}
