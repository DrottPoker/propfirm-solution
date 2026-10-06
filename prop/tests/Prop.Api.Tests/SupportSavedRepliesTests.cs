using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Prop.Api.Json;
using Prop.Api.Tests.Support;
using Prop.Rules;

namespace Prop.Api.Tests;

/// <summary>
/// What a firm's administrators answer tickets with (ADR 0041): saved replies they add, change and remove, which only
/// their firm reaches, and where the account a ticket is about is now.
/// </summary>
public sealed class SupportSavedRepliesTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task TheFirmSavesChangesAndRemovesReplies()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.LogInAsAdminAsync();
        Assert.Empty(await ListAsync(admin));

        // The title is on one line and the text keeps its line breaks, with the placeholders as they were written.
        using var added = await admin.PostAsJsonAsync(Url("saved-replies"), new { title = "  Payout\ntimes ", body = "Hi {trader},\r\npayouts are paid within 2 days.\r\n{firm}\r\n" }, TestContext.Current.CancellationToken);
        var payout = await JsonAsync(added, HttpStatusCode.Created);
        var payoutId = payout.GetProperty("id").GetGuid();
        Assert.Equal(new Uri($"/api/portal/admin/support/saved-replies/{payoutId}", UriKind.Relative), added.Headers.Location);
        Assert.Equal(("Payout times", "Hi {trader},\npayouts are paid within 2 days.\n{firm}"), (payout.GetProperty("title").GetString(), payout.GetProperty("body").GetString()));
        Assert.Equal(PropFactory.Start, payout.GetProperty("updatedAt").GetDateTimeOffset());
        await JsonAsync(await admin.PostAsJsonAsync(Url("saved-replies"), new { title = "account rules", body = "The rules are on the account's page." }, TestContext.Current.CancellationToken), HttpStatusCode.Created);
        await JsonAsync(await admin.PostAsJsonAsync(Url("saved-replies"), new { title = "Terminal prices", body = "Choose the server {firm}." }, TestContext.Current.CancellationToken), HttpStatusCode.Created);

        // Listed by title in any case.
        Assert.Equal(["account rules", "Payout times", "Terminal prices"], Titles(await ListAsync(admin)));

        // A change keeps the reply's id. Another reply's title, in any case, is refused.
        await factory.AdvanceAsync(TimeSpan.FromHours(1));
        using var changed = await admin.PutAsJsonAsync(Url($"saved-replies/{payoutId}"), new { title = "Payout days", body = "Hi {trader}, payouts are paid within 3 days." }, TestContext.Current.CancellationToken);
        var change = await JsonAsync(changed, HttpStatusCode.OK);
        Assert.Equal((payoutId, "Payout days", "Hi {trader}, payouts are paid within 3 days."), (change.GetProperty("id").GetGuid(), change.GetProperty("title").GetString(), change.GetProperty("body").GetString()));
        Assert.Equal(PropFactory.Start.AddHours(1), change.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Equal((409, "title"), await RefusedAsync(admin.PutAsJsonAsync(Url($"saved-replies/{payoutId}"), new { title = "TERMINAL PRICES", body = "Hello" }, TestContext.Current.CancellationToken)));
        Assert.Equal((409, "title"), await RefusedAsync(admin.PostAsJsonAsync(Url("saved-replies"), new { title = "Account Rules", body = "Hello" }, TestContext.Current.CancellationToken)));
        var replies = await ListAsync(admin);
        Assert.Equal(["account rules", "Payout days", "Terminal prices"], Titles(replies));
        Assert.Equal("Hi {trader}, payouts are paid within 3 days.", replies[1].GetProperty("body").GetString());

        // A reply can keep its own title in another case.
        await JsonAsync(await admin.PutAsJsonAsync(Url($"saved-replies/{payoutId}"), new { title = "PAYOUT DAYS", body = "Hello" }, TestContext.Current.CancellationToken), HttpStatusCode.OK);

        // Removed once. Then it is unknown.
        using var removed = await admin.DeleteAsync(Url($"saved-replies/{payoutId}"), TestContext.Current.CancellationToken);
        using var removedAgain = await admin.DeleteAsync(Url($"saved-replies/{payoutId}"), TestContext.Current.CancellationToken);
        using var changedAfter = await admin.PutAsJsonAsync(Url($"saved-replies/{payoutId}"), new { title = "Payout days", body = "Hello" }, TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.NotFound), (removed.StatusCode, removedAgain.StatusCode, changedAfter.StatusCode));
        Assert.Equal(["account rules", "Terminal prices"], Titles(await ListAsync(admin)));
    }

    [Fact]
    public async Task WhatASavedReplyNeedsIsCheckedBeforeItIsSaved()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.LogInAsAdminAsync();

        Assert.Equal((422, "title"), await AddRefusedAsync(admin, " \n ", "Hello"));
        Assert.Equal((422, "title"), await AddRefusedAsync(admin, null, "Hello"));
        Assert.Equal((422, "title"), await AddRefusedAsync(admin, new string('a', 81), "Hello"));
        Assert.Equal((422, "body"), await AddRefusedAsync(admin, "Greeting", " \r\n "));
        Assert.Equal((422, "body"), await AddRefusedAsync(admin, "Greeting", null));
        Assert.Equal((422, "body"), await AddRefusedAsync(admin, "Greeting", new string('a', 4_001)));
        Assert.Empty(await ListAsync(admin));

        // The longest title and text are saved, and a change is checked as a new reply is.
        var longest = await JsonAsync(
            await admin.PostAsJsonAsync(Url("saved-replies"), new { title = new string('t', 80), body = new string('b', 4_000) }, TestContext.Current.CancellationToken),
            HttpStatusCode.Created);
        var longestId = longest.GetProperty("id").GetGuid();
        Assert.Equal((422, "body"), await RefusedAsync(admin.PutAsJsonAsync(Url($"saved-replies/{longestId}"), new { title = "Greeting", body = "" }, TestContext.Current.CancellationToken)));
        Assert.Equal((422, "title"), await RefusedAsync(admin.PutAsJsonAsync(Url($"saved-replies/{longestId}"), new { title = "", body = "Hello" }, TestContext.Current.CancellationToken)));

        // A firm has at most 100 saved replies.
        for (var i = 1; i < 100; i++)
        {
            await JsonAsync(await admin.PostAsJsonAsync(Url("saved-replies"), new { title = $"Reply {i}", body = "Hello" }, TestContext.Current.CancellationToken), HttpStatusCode.Created);
        }

        Assert.Equal((409, (string?)null), await AddRefusedAsync(admin, "One more", "Hello"));
        Assert.Equal(100, (await ListAsync(admin)).Count);
        using var removed = await admin.DeleteAsync(Url($"saved-replies/{longestId}"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        await JsonAsync(await admin.PostAsJsonAsync(Url("saved-replies"), new { title = "One more", body = "Hello" }, TestContext.Current.CancellationToken), HttpStatusCode.Created);
    }

    [Fact]
    public async Task ASavedReplyIsOnlyReachedByItsFirm()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithOtherFirm());
        using var admin = await factory.LogInAsAdminAsync();
        using var trader = await factory.LogInAsTraderAsync((await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid());
        using var otherAdmin = factory.CreatePortalClient(PropFactory.OtherFirmHost);
        using var login = await otherAdmin.PostAsJsonAsync(new Uri("/api/portal/admin/login", UriKind.Relative), new { email = PropFactory.AdminEmail, password = "other-admin-password" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var reply = await JsonAsync(await admin.PostAsJsonAsync(Url("saved-replies"), new { title = "Greeting", body = "Hi {trader}" }, TestContext.Current.CancellationToken), HttpStatusCode.Created);
        var replyId = reply.GetProperty("id").GetGuid();

        // The other firm sees none of them, and cannot change or remove one. The same title is its own to use.
        Assert.Empty(await ListAsync(otherAdmin));
        using var otherChanges = await otherAdmin.PutAsJsonAsync(Url($"saved-replies/{replyId}"), new { title = "Mine now", body = "Hello" }, TestContext.Current.CancellationToken);
        using var otherRemoves = await otherAdmin.DeleteAsync(Url($"saved-replies/{replyId}"), TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (otherChanges.StatusCode, otherRemoves.StatusCode));
        await JsonAsync(await otherAdmin.PostAsJsonAsync(Url("saved-replies"), new { title = "Greeting", body = "Hello from us" }, TestContext.Current.CancellationToken), HttpStatusCode.Created);
        Assert.Equal("Hello from us", Assert.Single(await ListAsync(otherAdmin)).GetProperty("body").GetString());

        // Traders and visitors reach none of it.
        using var traderLists = await trader.GetAsync(Url("saved-replies"), TestContext.Current.CancellationToken);
        using var traderAdds = await trader.PostAsJsonAsync(Url("saved-replies"), new { title = "Mine", body = "Hello" }, TestContext.Current.CancellationToken);
        using var visitor = factory.CreatePortalClient();
        using var visitorLists = await visitor.GetAsync(Url("saved-replies"), TestContext.Current.CancellationToken);
        Assert.All(new[] { traderLists, traderAdds, visitorLists }, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));

        var mine = Assert.Single(await ListAsync(admin));
        Assert.Equal((replyId, "Greeting", "Hi {trader}"), (mine.GetProperty("id").GetGuid(), mine.GetProperty("title").GetString(), mine.GetProperty("body").GetString()));
    }

    [Fact]
    public async Task ATicketShowsWhereItsAccountIsNow()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var firm = factory.CreateFirmClient();
        using var saved = await firm.PutAsJsonAsync(
            new Uri("/api/firm/v1/challenges/instant-funded-100k", UriKind.Relative),
            ChallengeTemplates.InstantFunded("instant-funded-100k", 100_000m),
            PropJson.Options,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var accountId = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        var fundedId = (await factory.StartActiveAccountAsync(challengeId: "instant-funded-100k")).GetProperty("id").GetGuid();
        using var trader = await factory.LogInAsTraderAsync(accountId);
        using var admin = await factory.LogInAsAdminAsync();

        var ticketId = (await JsonAsync(await PostFormAsync(trader, "support/tickets", new() { ["subject"] = "My account", ["body"] = "Hello", ["accountId"] = accountId.ToString() }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var fundedTicketId = (await JsonAsync(await PostFormAsync(trader, "support/tickets", new() { ["subject"] = "My funded account", ["body"] = "Hello", ["accountId"] = fundedId.ToString() }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var noAccount = await JsonAsync(await PostFormAsync(trader, "support/tickets", new() { ["subject"] = "A question", ["body"] = "Hello" }), HttpStatusCode.Created);
        Assert.Equal(JsonValueKind.Null, noAccount.GetProperty("account").ValueKind);

        Assert.Equal(("Active", "Phase 1", false, false), AccountOf(await TicketAsync(admin, ticketId)));
        Assert.Equal(("Active", "Funded", true, false), AccountOf(await TicketAsync(admin, fundedTicketId)));
        var listed = (await admin.GetFromJsonAsync<JsonElement>(Url("tickets?group=All"), TestContext.Current.CancellationToken)).GetProperty("tickets").EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == ticketId);
        Assert.Equal(("Active", "Phase 1", false, false), AccountOf(listed));

        // Paused while the firm's month is unpaid, and cancelled by the firm.
        await factory.ScalarAsync($"update challenge_accounts set paused = true where id = '{fundedId}'");
        Assert.Equal(("Active", "Funded", true, true), AccountOf(await TicketAsync(admin, fundedTicketId)));
        using var cancelled = await admin.PostAsJsonAsync(new Uri($"/api/portal/admin/accounts/{accountId}/cancel", UriKind.Relative), new { reason = "Refunded." }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(("Cancelled", "Phase 1", false, false), AccountOf(await TicketAsync(admin, ticketId)));

        // The trader sees where their own account is too.
        var asTrader = await trader.GetFromJsonAsync<JsonElement>(new Uri($"/api/portal/support/tickets/{ticketId}", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(("Cancelled", "Phase 1", false, false), AccountOf(asTrader));
    }

    private static Task<JsonElement> TicketAsync(HttpClient admin, Guid ticketId) =>
        admin.GetFromJsonAsync<JsonElement>(Url($"tickets/{ticketId}"), TestContext.Current.CancellationToken);

    private static (string? Status, string? StageName, bool Funded, bool Paused) AccountOf(JsonElement ticket)
    {
        var account = ticket.GetProperty("account");
        return (account.GetProperty("status").GetString(), account.GetProperty("stageName").GetString(), account.GetProperty("funded").GetBoolean(), account.GetProperty("paused").GetBoolean());
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient admin) =>
        [.. (await admin.GetFromJsonAsync<JsonElement>(Url("saved-replies"), TestContext.Current.CancellationToken)).EnumerateArray()];

    private static List<string?> Titles(List<JsonElement> replies) => [.. replies.Select(r => r.GetProperty("title").GetString())];

    private static Task<(int Status, string? Field)> AddRefusedAsync(HttpClient admin, string? title, string? body) =>
        RefusedAsync(admin.PostAsJsonAsync(Url("saved-replies"), new { title, body }, TestContext.Current.CancellationToken));

    // The status and the field the problem is about, when a reply is refused.
    private static async Task<(int Status, string? Field)> RefusedAsync(Task<HttpResponseMessage> sending)
    {
        using var response = await sending;
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return ((int)response.StatusCode, problem.TryGetProperty("field", out var field) ? field.GetString() : null);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (name, value) in fields)
        {
            form.Add(new StringContent(value, Encoding.UTF8), name);
        }

        return await client.PostAsync(new Uri($"/api/portal/{path}", UriKind.Relative), form, TestContext.Current.CancellationToken);
    }

    // The response's JSON, after checking its status. Disposes the response.
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.StatusCode == expected, $"Expected {expected} but got {response.StatusCode}: {content}");
            using var document = JsonDocument.Parse(content);
            return document.RootElement.Clone();
        }
    }

    private static Uri Url(string path) => new($"/api/portal/admin/support/{path}", UriKind.Relative);
}
