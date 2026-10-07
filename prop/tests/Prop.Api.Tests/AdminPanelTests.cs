using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Tests.Support;
using Prop.Api.Trading;

using static Prop.Api.Tests.Support.TestAccounts;

namespace Prop.Api.Tests;

/// <summary>
/// The admin panel's own views of the firm (ADR 0023): the overview, the account search, an account's trader and
/// trading history, the payout queue and the challenges' figures. Uses the development challenge quick-test-100k,
/// with two quick evaluation stages and an 80 % profit split, and the demo firm's shop with test payments.
/// </summary>
public sealed class AdminPanelTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string TwoStep = "two-step-100k";

    // The chart starts in the week the firm started, and goes back at most 12 weeks once the firm is older.
    [Fact]
    public async Task TheWeeksStartWhenTheFirmDidAndAreAtMostTwelve()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());

        var first = await WeekStartsAsync();
        await factory.AdvanceAsync(TimeSpan.FromDays(22));
        var later = await WeekStartsAsync();
        await factory.AdvanceAsync(TimeSpan.FromDays(100));
        var older = await WeekStartsAsync();

        Assert.Equal(["2026-10-05"], first);
        Assert.Equal(["2026-10-05", "2026-10-12", "2026-10-19", "2026-10-26"], later);
        Assert.Equal(12, older.Count);
        Assert.Equal(("2026-11-16", "2027-02-01"), (older[0], older[^1]));

        // Logged in each time, since a session does not last for months.
        async Task<List<string?>> WeekStartsAsync()
        {
            using var admin = await factory.LogInAsAdminAsync();
            var overview = await admin.GetFromJsonAsync<JsonElement>(Url("admin/overview"), TestContext.Current.CancellationToken);
            return [.. overview.GetProperty("weeks").EnumerateArray().Select(w => w.GetProperty("start").GetString())];
        }
    }

    [Fact]
    public async Task TheOverviewShowsTheAccountsThePayoutsTheSalesAndWhatHappened()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var anna = await FundedAsync(factory, "anna@test.example", 1001, 8_000m);
        var bert = (await factory.StartActiveAccountAsync("bert@test.example", TwoStep)).GetProperty("id").GetGuid();
        factory.Trading.Breach("demo-firm-1002-1", "daily", 94_900m);
        await factory.WaitForAccountAsync(bert, a => a.GetProperty("status").GetString() == "Failed");
        await BuyAsync(factory, "cora@test.example");
        await PayOutAsync(factory, anna, "wire-1");
        using var admin = await factory.LogInAsAdminAsync();

        var overview = await admin.GetFromJsonAsync<JsonElement>(Url("admin/overview"), TestContext.Current.CancellationToken);

        Assert.Equal((3, 1, 0, 1, 1), Counts(overview.GetProperty("accounts")));
        var payouts = overview.GetProperty("payouts");
        Assert.Equal((0, 0, 1), (payouts.GetProperty("toApprove").GetProperty("count").GetInt32(), payouts.GetProperty("toPay").GetProperty("count").GetInt32(), payouts.GetProperty("paidLast30Days").GetProperty("count").GetInt32()));
        Assert.Equal([("USD", 6_400m)], Totals(payouts.GetProperty("paidLast30Days").GetProperty("totals")));
        Assert.Equal(0d, payouts.GetProperty("averageDaysToPay").GetDouble());
        Assert.Equal(1, overview.GetProperty("sales").GetProperty("orders").GetInt32());
        Assert.Equal([("USD", 9m)], Totals(overview.GetProperty("sales").GetProperty("totals")));

        // Anna passed every evaluation stage and Bert failed his first, so one of two evaluations passed.
        Assert.Equal((1, 2), (overview.GetProperty("passRate").GetProperty("passed").GetInt32(), overview.GetProperty("passRate").GetProperty("ended").GetInt32()));

        // The firm started this week, so there are no empty weeks from before it.
        var weeks = overview.GetProperty("weeks").EnumerateArray().ToList();
        Assert.Single(weeks);
        Assert.Equal("2026-10-05", weeks[^1].GetProperty("start").GetString());
        Assert.Equal([("USD", 9m)], Totals(weeks[^1].GetProperty("sales")));
        Assert.Equal([("USD", 6_400m)], Totals(weeks[^1].GetProperty("payouts")));
        Assert.All(weeks[..^1], w => Assert.Empty(w.GetProperty("sales").EnumerateArray()));

        var activity = overview.GetProperty("activity").EnumerateArray().ToList();
        Assert.Equal(
            ["ChallengeBought", "ChallengeFailed", "ChallengeStarted", "ChallengeStarted", "EvaluationPassed", "FundedStarted", "PayoutPaid", "PayoutRequested", "StagePassed"],
            activity.Select(a => a.GetProperty("kind").GetString()).Order(StringComparer.Ordinal));
        var failed = activity.Single(a => a.GetProperty("kind").GetString() == "ChallengeFailed");
        Assert.Equal(("bert@test.example", 1002L, "DailyLoss", "Phase 1"), (failed.GetProperty("email").GetString(), failed.GetProperty("accountNumber").GetInt64(), failed.GetProperty("reason").GetString(), failed.GetProperty("stageName").GetString()));
        var bought = activity.Single(a => a.GetProperty("kind").GetString() == "ChallengeBought");
        Assert.Equal(("cora@test.example", 9m, "USD"), (bought.GetProperty("email").GetString(), bought.GetProperty("amount").GetDecimal(), bought.GetProperty("currency").GetString()));
        var paid = activity.Single(a => a.GetProperty("kind").GetString() == "PayoutPaid");
        Assert.Equal((6_400m, "wire-1"), (paid.GetProperty("amount").GetDecimal(), paid.GetProperty("reference").GetString()));
        var passed = activity.Single(a => a.GetProperty("kind").GetString() == "StagePassed");
        Assert.Equal((anna, 100m), (passed.GetProperty("accountId").GetGuid(), passed.GetProperty("amount").GetDecimal()));
    }

    [Fact]
    public async Task TheAdminFindsAccountsByEmailNumberReferenceChallengeAndGroup()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var firm = factory.CreateFirmClient();
        await firm.PostJsonAsync("accounts", new { email = "anna@test.example", challengeId = TwoStep, reference = "shop-order-17" }, HttpStatusCode.Created);
        await factory.StartActiveAccountAsync("bert@test.example", QuickTest);
        var cora = (await factory.StartActiveAccountAsync("cora@test.example", TwoStep)).GetProperty("id").GetGuid();
        await firm.PostJsonAsync($"accounts/{cora}/cancel", new { reason = "Refunded." });
        using var admin = await factory.LogInAsAdminAsync();

        var all = await SearchAsync(admin, "");
        var byEmail = await SearchAsync(admin, "?search=ANNA@");
        var byNumber = await SearchAsync(admin, "?search=%231002");
        var byPlainNumber = await SearchAsync(admin, "?search=1002");
        var byReference = await SearchAsync(admin, "?search=order-1");
        var byChallenge = await SearchAsync(admin, $"?challengeId={QuickTest}");
        var ended = await SearchAsync(admin, "?group=Ended");
        var evaluationOfTwoStep = await SearchAsync(admin, $"?group=Evaluation&challengeId={TwoStep}");
        var firstPage = await SearchAsync(admin, "?limit=2");
        var secondPage = await SearchAsync(admin, $"?limit=2&before={firstPage.GetProperty("next").GetInt64()}");
        using var tooMany = await admin.GetAsync(Url("admin/accounts?limit=201"), TestContext.Current.CancellationToken);

        Assert.Equal([1003L, 1002L, 1001L], Numbers(all));
        Assert.Equal((3, 2, 0, 0, 1), Counts(all.GetProperty("counts")));
        Assert.Equal([1001L], Numbers(byEmail));
        Assert.Equal([1002L], Numbers(byNumber));
        Assert.Equal([1002L], Numbers(byPlainNumber));
        Assert.Equal([1001L], Numbers(byReference));
        Assert.Equal([1002L], Numbers(byChallenge));
        Assert.Equal((1, 1, 0, 0, 0), Counts(byChallenge.GetProperty("counts")));
        Assert.Equal([1003L], Numbers(ended));
        Assert.Equal([1001L], Numbers(evaluationOfTwoStep));
        Assert.Equal([1003L, 1002L], Numbers(firstPage));
        Assert.Equal([1001L], Numbers(secondPage));
        Assert.Equal(JsonValueKind.Null, secondPage.GetProperty("next").ValueKind);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMany.StatusCode);
    }

    [Fact]
    public async Task TheAdminSeesTheTraderBehindAnAccount()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var funded = await FundedAsync(factory, "anna@test.example", 1001, 8_000m);
        await PayOutAsync(factory, funded, null);
        var bought = await BuyAsync(factory, "Anna@Test.example");
        await factory.StartActiveAccountAsync("bert@test.example", TwoStep);
        using var admin = await factory.LogInAsAdminAsync();

        var trader = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{bought}/trader"), TestContext.Current.CancellationToken);
        var fundedTrader = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{funded}/trader"), TestContext.Current.CancellationToken);
        using var unknown = await admin.GetAsync(Url($"admin/accounts/{Guid.NewGuid()}/trader"), TestContext.Current.CancellationToken);

        Assert.Equal(("anna@test.example", false), (trader.GetProperty("email").GetString(), trader.GetProperty("hasPassword").GetBoolean()));
        Assert.Equal([1002L, 1001L], trader.GetProperty("accounts").EnumerateArray().Select(a => a.GetProperty("number").GetInt64()));
        Assert.Equal(1, trader.GetProperty("orders").GetInt32());
        Assert.Equal([("USD", 9m)], Totals(trader.GetProperty("bought")));
        Assert.Equal([("USD", 6_400m)], Totals(trader.GetProperty("paidOut")));
        var order = trader.GetProperty("order");
        Assert.Equal((9m, "USD", "Test"), (order.GetProperty("amount").GetDecimal(), order.GetProperty("currency").GetString(), order.GetProperty("provider").GetString()));
        Assert.Equal(JsonValueKind.Null, fundedTrader.GetProperty("order").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task TheAdminEmailsTheTraderAnInvitationOrThatTheChallengeStarted()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var first = (await factory.StartActiveAccountAsync("anna@test.example", TwoStep)).GetProperty("id").GetGuid();
        using var admin = await factory.LogInAsAdminAsync();

        var invited = await PostAsync(admin, $"admin/accounts/{first}/email-trader", HttpStatusCode.OK);
        var invitation = factory.Emails.Sent.Last(e => e.To == "anna@test.example");
        using var trader = factory.CreatePortalClient();
        using var accepted = await trader.PostAsJsonAsync(
            Url("invites/accept"), new { token = FakeEmailSender.TokenIn(invitation), password = PropFactory.TraderPassword }, TestContext.Current.CancellationToken);
        var second = (await factory.StartActiveAccountAsync("anna@test.example", QuickTest)).GetProperty("id").GetGuid();
        var notified = await PostAsync(admin, $"admin/accounts/{second}/email-trader", HttpStatusCode.OK);
        var notice = factory.Emails.Sent.Last(e => e.To == "anna@test.example");
        factory.Emails.FailNext(1);
        var notSent = await PostAsync(admin, $"admin/accounts/{second}/email-trader", HttpStatusCode.ServiceUnavailable);

        Assert.Equal(("anna@test.example", "Invitation"), (invited.GetProperty("email").GetString(), invited.GetProperty("kind").GetString()));
        Assert.Equal(("Demo Firm", "Your Two-step 100K with Demo Firm has started"), (invitation.FromName, invitation.Subject));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("Notice", notified.GetProperty("kind").GetString());
        Assert.Contains($"http://localhost:3002/accounts/{second}", notice.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", notice.Body, StringComparison.Ordinal);
        Assert.Equal("The email could not be sent. Try again shortly.", notSent.GetProperty("title").GetString());
    }

    [Fact]
    public async Task TheAdminSeesTheEmailBeforeItIsSent()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync("anna@test.example", TwoStep)).GetProperty("id").GetGuid();
        using var admin = await factory.LogInAsAdminAsync();
        var sentBefore = factory.Emails.Sent.Count;

        var preview = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{id}/email-trader"), TestContext.Current.CancellationToken);

        Assert.Equal(("anna@test.example", "Invitation"), (preview.GetProperty("email").GetString(), preview.GetProperty("kind").GetString()));
        Assert.Equal("Your Two-step 100K with Demo Firm has started", preview.GetProperty("subject").GetString());
        Assert.Contains("http://localhost:3002/invite?token=...", preview.GetProperty("body").GetString(), StringComparison.Ordinal);
        Assert.Equal(sentBefore, factory.Emails.Sent.Count);
    }

    // The trader writes a name only once, so the firm is the one who corrects it.
    [Fact]
    public async Task TheFirmCorrectsTheTradersName()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync("anna@test.example", TwoStep)).GetProperty("id").GetGuid();
        using var trader = await factory.LogInAsTraderAsync(id);
        (await trader.PutAsJsonAsync(Url("me/name"), new { name = "Ana Berg" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var admin = await factory.LogInAsAdminAsync();

        using var empty = await admin.PutAsJsonAsync(Url($"admin/accounts/{id}/trader/name"), new { name = "" }, TestContext.Current.CancellationToken);
        using var unknown = await admin.PutAsJsonAsync(Url($"admin/accounts/{Guid.NewGuid()}/trader/name"), new { name = "Anna Berg" }, TestContext.Current.CancellationToken);
        using var corrected = await admin.PutAsJsonAsync(Url($"admin/accounts/{id}/trader/name"), new { name = " Anna Berg " }, TestContext.Current.CancellationToken);
        var card = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{id}/trader"), TestContext.Current.CancellationToken);
        var me = await trader.GetFromJsonAsync<JsonElement>(Url("me"), TestContext.Current.CancellationToken);
        using var byTrader = await trader.PutAsJsonAsync(Url($"admin/accounts/{id}/trader/name"), new { name = "Someone Else" }, TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.UnprocessableEntity, HttpStatusCode.NotFound, HttpStatusCode.NoContent), (empty.StatusCode, unknown.StatusCode, corrected.StatusCode));
        Assert.Equal(("Anna Berg", "Anna Berg"), (card.GetProperty("name").GetString(), me.GetProperty("name").GetString()));
        Assert.Equal(HttpStatusCode.Unauthorized, byTrader.StatusCode);
    }

    [Fact]
    public async Task TheFirmTicksItsChecksOfATraderAndSeesThemOnPayouts()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync("anna@test.example", TwoStep)).GetProperty("id").GetGuid();
        using var admin = await factory.LogInAsAdminAsync();

        var before = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{id}/trader"), TestContext.Current.CancellationToken);
        using var identity = await admin.PutAsJsonAsync(Url($"admin/accounts/{id}/trader/checks/identity"), new { @checked = true }, TestContext.Current.CancellationToken);
        using var address = await admin.PutAsJsonAsync(Url($"admin/accounts/{id}/trader/checks/address"), new { @checked = true }, TestContext.Current.CancellationToken);
        using var untick = await admin.PutAsJsonAsync(Url($"admin/accounts/{id}/trader/checks/address"), new { @checked = false }, TestContext.Current.CancellationToken);
        using var unknown = await admin.PutAsJsonAsync(Url($"admin/accounts/{id}/trader/checks/selfie"), new { @checked = true }, TestContext.Current.CancellationToken);
        var after = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{id}/trader"), TestContext.Current.CancellationToken);

        Assert.Equal(
            [("identity", "ID checked", false), ("address", "Address checked", false)],
            before.GetProperty("checks").EnumerateArray().Select(c => (c.GetProperty("item").GetString(), c.GetProperty("label").GetString(), c.GetProperty("checkedAt").ValueKind == JsonValueKind.String)));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.NotFound), (identity.StatusCode, address.StatusCode, untick.StatusCode, unknown.StatusCode));
        var checks = after.GetProperty("checks").EnumerateArray().ToList();
        Assert.Equal((PropFactory.AdminEmail, JsonValueKind.Null), (checks[0].GetProperty("checkedBy").GetString(), checks[1].GetProperty("checkedAt").ValueKind));
    }

    [Fact]
    public async Task TheAdminSeesTheTradingHistoryOfTheFirmsAccountsOnly()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithOtherFirm());
        var id = (await factory.StartActiveAccountAsync("anna@test.example", TwoStep)).GetProperty("id").GetGuid();
        factory.Trading.OpenPosition("demo-firm-1001-1");
        factory.Trading.ClosePosition("demo-firm-1001-1", 250m);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("balance").GetDecimal() == 100_250m);
        using var admin = await factory.LogInAsAdminAsync();
        using var otherAdmin = factory.CreatePortalClient(PropFactory.OtherFirmHost);
        (await otherAdmin.PostAsJsonAsync(Url("admin/login"), new { email = PropFactory.AdminEmail, password = "other-admin-password" }, TestContext.Current.CancellationToken)).Dispose();

        JsonElement trades = default;
        await Eventually.ThatAsync(
            async () =>
            {
                trades = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{id}/trades"));
                return trades.GetProperty("trades").GetArrayLength() == 1;
            },
            "the closed trade in the history");
        var performance = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{id}/performance"), TestContext.Current.CancellationToken);
        using var csv = await admin.GetAsync(Url($"admin/accounts/{id}/trades.csv"), TestContext.Current.CancellationToken);
        using var otherFirm = await otherAdmin.GetAsync(Url($"admin/accounts/{id}/performance"), TestContext.Current.CancellationToken);

        Assert.Equal(250m, trades.GetProperty("trades")[0].GetProperty("result").GetDecimal());
        Assert.Equal(("Phase 1", 1), (performance.GetProperty("stageName").GetString(), performance.GetProperty("statistics").GetProperty("trades").GetInt32()));
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, otherFirm.StatusCode);
    }

    // The firm sees the limits the trader set for themselves and when they locked, for 30 days, but cannot change them (ADR 0054).
    [Fact]
    public async Task TheAdminSeesTheTradersOwnLimitsAndTheDaysTheyLocked()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithOtherFirm());
        var id = (await factory.StartActiveAccountAsync("anna@test.example", TwoStep)).GetProperty("id").GetGuid();
        var midnight = new DateTimeOffset(2026, 10, 5, 22, 0, 0, TimeSpan.Zero);
        factory.Trading.SetOwnLimits(
            "demo-firm-1001-1",
            new TradingOwnLimits(new OwnLimitAmounts(1_500m, null, 6), new OwnLimitAmounts(2_000m, null, 6), 2, midnight, new OwnLock(midnight, OwnLockReason.DailyLoss)));
        factory.Trading.LockDay("demo-firm-1001-1", OwnLockReason.DailyLoss, midnight, 1_500m, -1_531.6m, 2);
        using var admin = await factory.LogInAsAdminAsync();
        using var otherAdmin = factory.CreatePortalClient(PropFactory.OtherFirmHost);
        (await otherAdmin.PostAsJsonAsync(Url("admin/login"), new { email = PropFactory.AdminEmail, password = "other-admin-password" }, TestContext.Current.CancellationToken)).Dispose();

        JsonElement limits = default;
        await Eventually.ThatAsync(
            async () =>
            {
                limits = await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{id}/own-limits"));
                return limits.GetProperty("locks").GetArrayLength() == 1;
            },
            "the locked day in the history");
        using var otherFirm = await otherAdmin.GetAsync(Url($"admin/accounts/{id}/own-limits"), TestContext.Current.CancellationToken);

        var now = limits.GetProperty("now");
        Assert.Equal((1_500m, 2_000m, 2), (now.GetProperty("limits").GetProperty("dailyLoss").GetDecimal(), now.GetProperty("pending").GetProperty("dailyLoss").GetDecimal(), now.GetProperty("tradesToday").GetInt32()));
        Assert.Equal(("DailyLoss", midnight), (now.GetProperty("lock").GetProperty("reason").GetString(), now.GetProperty("lock").GetProperty("until").GetDateTimeOffset()));
        var locked = limits.GetProperty("locks")[0];
        Assert.Equal(("DailyLoss", 1_500m, -1_531.6m, 2), (locked.GetProperty("reason").GetString(), locked.GetProperty("limit").GetDecimal(), locked.GetProperty("dayResult").GetDecimal(), locked.GetProperty("positionsClosed").GetInt32()));
        Assert.Equal(HttpStatusCode.NotFound, otherFirm.StatusCode);

        // The session has ended by then, so the admin logs in again.
        await factory.AdvanceAsync(TimeSpan.FromDays(31));
        using var adminLater = await factory.LogInAsAdminAsync();
        var later = await adminLater.GetFromJsonAsync<JsonElement>(Url($"admin/accounts/{id}/own-limits"), TestContext.Current.CancellationToken);
        Assert.Equal(0, later.GetProperty("locks").GetArrayLength());
    }

    [Fact]
    public async Task ThePayoutQueueHasTheOldestFirstWithWhatTheAccountWasPaidBefore()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var anna = await FundedAsync(factory, "anna@test.example", 1001, 8_000m);
        var bert = await FundedAsync(factory, "bert@test.example", 1002, 4_000m);
        await PayOutAsync(factory, anna, "wire-1");
        await factory.AdvanceAsync(TimeSpan.FromHours(1));
        factory.Trading.OpenPosition("demo-firm-1001-3");
        factory.Trading.ClosePosition("demo-firm-1001-3", 1_000m);
        await factory.WaitForAccountAsync(anna, a => a.GetProperty("balance").GetDecimal() == 101_000m);
        var annasSecond = await RequestPayoutAsync(factory, anna);
        await factory.AdvanceAsync(TimeSpan.FromHours(2));
        var bertsFirst = await RequestPayoutAsync(factory, bert);
        using var admin = await factory.LogInAsAdminAsync();

        var oldestFirst = await admin.GetFromJsonAsync<JsonElement>(Url("admin/payouts?status=Pending&oldestFirst=true"), TestContext.Current.CancellationToken);
        var newestFirst = await admin.GetFromJsonAsync<JsonElement>(Url("admin/payouts?status=Pending"), TestContext.Current.CancellationToken);
        var summary = await admin.GetFromJsonAsync<JsonElement>(Url("admin/payouts/summary"), TestContext.Current.CancellationToken);

        Assert.Equal([annasSecond, bertsFirst], oldestFirst.EnumerateArray().Select(p => p.GetProperty("payout").GetProperty("id").GetGuid()));
        Assert.Equal([bertsFirst, annasSecond], newestFirst.EnumerateArray().Select(p => p.GetProperty("payout").GetProperty("id").GetGuid()));
        var second = oldestFirst[0];
        Assert.Equal(("Quick test 100K", 1, 6_400m), (second.GetProperty("challengeName").GetString(), second.GetProperty("paidBefore").GetInt32(), second.GetProperty("paidBeforeAmount").GetDecimal()));
        Assert.Equal((0, 0m), (oldestFirst[1].GetProperty("paidBefore").GetInt32(), oldestFirst[1].GetProperty("paidBeforeAmount").GetDecimal()));
        var toApprove = summary.GetProperty("toApprove");
        Assert.Equal(2, toApprove.GetProperty("count").GetInt32());
        Assert.Equal([("USD", 800m + 3_200m)], Totals(toApprove.GetProperty("totals")));
        Assert.Equal(oldestFirst[0].GetProperty("payout").GetProperty("requestedAt").GetDateTimeOffset(), toApprove.GetProperty("oldest").GetDateTimeOffset());
        Assert.Equal(0, summary.GetProperty("toPay").GetProperty("count").GetInt32());
        Assert.Equal([("USD", 6_400m)], Totals(summary.GetProperty("paidLast30Days").GetProperty("totals")));
    }

    [Fact]
    public async Task EachChallengeHasItsOpenAccountsStartsAndPassRate()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        await FundedAsync(factory, "anna@test.example", 1001, 1_000m);
        var bert = (await factory.StartActiveAccountAsync("bert@test.example", TwoStep)).GetProperty("id").GetGuid();
        factory.Trading.Breach("demo-firm-1002-1", "daily", 94_900m);
        await factory.WaitForAccountAsync(bert, a => a.GetProperty("status").GetString() == "Failed");
        await factory.StartActiveAccountAsync("cora@test.example", TwoStep);
        var dora = (await factory.StartActiveAccountAsync("dora@test.example", QuickTest)).GetProperty("id").GetGuid();
        using var firm = factory.CreateFirmClient();
        await firm.PostJsonAsync($"accounts/{dora}/cancel", new { reason = "Refunded." });
        using var admin = await factory.LogInAsAdminAsync();

        var figures = (await admin.GetFromJsonAsync<JsonElement>(Url("admin/challenges/figures"), TestContext.Current.CancellationToken))
            .EnumerateArray()
            .ToDictionary(f => f.GetProperty("challengeId").GetString()!);

        // A cancelled challenge says nothing about how hard the challenge is, so it is not an ended evaluation.
        Assert.Equal((1, 2, 1, 1), Figures(figures[QuickTest]));
        Assert.Equal((1, 2, 0, 1), Figures(figures[TwoStep]));
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static (int All, int Evaluation, int AwaitingFunding, int Funded, int Ended) Counts(JsonElement counts) =>
        (counts.GetProperty("all").GetInt32(),
            counts.GetProperty("evaluation").GetInt32(),
            counts.GetProperty("awaitingFunding").GetInt32(),
            counts.GetProperty("funded").GetInt32(),
            counts.GetProperty("ended").GetInt32());

    private static List<(string Currency, decimal Amount)> Totals(JsonElement totals) =>
        [.. totals.EnumerateArray().Select(t => (t.GetProperty("currency").GetString()!, t.GetProperty("amount").GetDecimal()))];

    private static IEnumerable<long> Numbers(JsonElement page) => page.GetProperty("accounts").EnumerateArray().Select(a => a.GetProperty("number").GetInt64());

    private static (int Trading, int Started, int Passed, int Ended) Figures(JsonElement figures) =>
        (figures.GetProperty("trading").GetInt32(),
            figures.GetProperty("startedLast30Days").GetInt32(),
            figures.GetProperty("passRate").GetProperty("passed").GetInt32(),
            figures.GetProperty("passRate").GetProperty("ended").GetInt32());

    private static async Task<JsonElement> SearchAsync(HttpClient admin, string query) =>
        await admin.GetFromJsonAsync<JsonElement>(Url($"admin/accounts{query}"), TestContext.Current.CancellationToken);

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, HttpStatusCode expected)
    {
        using var response = await client.PostAsync(Url(path), null, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == expected, $"Expected {expected} but got {response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    /// <summary>The trader asks for a payout, and the firm approves it and marks it as paid.</summary>
    private static async Task PayOutAsync(PropFactory factory, Guid accountId, string? reference)
    {
        var payoutId = await RequestPayoutAsync(factory, accountId);
        using var firm = factory.CreateFirmClient();
        await firm.PostJsonAsync($"payouts/{payoutId}/approve", null);
        await firm.PostJsonAsync($"payouts/{payoutId}/mark-paid", new { reference });
    }

    /// <summary>Buys the quick challenge in the demo firm's shop and pays with a test payment. Returns the account it started.</summary>
    private static async Task<Guid> BuyAsync(PropFactory factory, string email)
    {
        using var buyer = factory.CreatePortalClient();
        using var response = await buyer.PostAsJsonAsync(Url("orders"), new { challengeId = QuickTest, email, acceptTerms = true, name = "Ann Buyer", country = "SE" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var token = created.GetProperty("checkoutUrl").GetString()!.Split("token=")[1].Split('&')[0];
        using var paid = await buyer.PostAsJsonAsync(Url($"orders/{created.GetProperty("orderId").GetGuid()}/test-payment"), new { token }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        return (await paid.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("accountId").GetGuid();
    }
}
