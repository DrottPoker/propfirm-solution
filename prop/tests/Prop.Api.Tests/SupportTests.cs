using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>
/// Support tickets between a firm's traders and the firm (ADR 0041): opening, answering, closing and opening again, the
/// files, the emails, what is refused, and that a ticket is only reached by its trader and its firm.
/// </summary>
public sealed class SupportTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Admin = PropFactory.AdminEmail;

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8];

    private static readonly byte[] Pdf = [.. "%PDF-1.7 a document"u8];

    [Fact]
    public async Task TheTraderAsksAndTheFirmAnswersInTheTicket()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var accountId = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var trader = await factory.LogInAsTraderAsync(accountId);
        using var admin = await factory.LogInAsAdminAsync();

        using var opened = await PostFormAsync(
            trader,
            "support/tickets",
            new() { ["subject"] = "  Where is\nmy payout?  ", ["body"] = "Hi,\r\nI asked for it two days ago.\r\n", ["accountId"] = accountId.ToString() },
            ("screen.png", Png));
        var ticket = await JsonAsync(opened, HttpStatusCode.Created);
        var ticketId = ticket.GetProperty("id").GetGuid();

        Assert.Equal(new Uri($"/api/portal/support/tickets/{ticketId}", UriKind.Relative), opened.Headers.Location);
        Assert.Equal((1L, "Where is my payout?", "Open"), (ticket.GetProperty("number").GetInt64(), ticket.GetProperty("subject").GetString(), ticket.GetProperty("status").GetString()));
        Assert.Equal((accountId, 1001L, "Two-step 100K"), (ticket.GetProperty("account").GetProperty("id").GetGuid(), ticket.GetProperty("account").GetProperty("number").GetInt64(), ticket.GetProperty("account").GetProperty("challengeName").GetString()));
        var first = Assert.Single(ticket.GetProperty("messages").EnumerateArray());
        Assert.Equal(("Trader", "Hi,\nI asked for it two days ago."), (first.GetProperty("author").GetString(), first.GetProperty("body").GetString()));
        var attachment = Assert.Single(first.GetProperty("attachments").EnumerateArray());
        Assert.Equal(("screen.png", "image/png", Png.Length), (attachment.GetProperty("fileName").GetString(), attachment.GetProperty("contentType").GetString(), attachment.GetProperty("size").GetInt32()));

        // The administrators get the message, and the file is kept encrypted.
        var toAdmin = await factory.Emails.WaitForAsync(Admin, "New support ticket #1 from anna@test.example");
        Assert.Contains("opened support ticket #1 about account #1001, \"Where is my payout?\":", toAdmin.Body, StringComparison.Ordinal);
        Assert.Contains("> Hi,\n> I asked for it two days ago.\n\n1 file is attached in the ticket.", toAdmin.Body, StringComparison.Ordinal);
        Assert.Contains($"/admin/support/{ticketId}", toAdmin.Body, StringComparison.Ordinal);
        var stored = (byte[])(await factory.ScalarAsync("select content from support_attachments"))!;
        Assert.Equal(Png.Length + 28, stored.Length);
        Assert.False(stored.AsSpan().IndexOf(Png.AsSpan(0, 8)) >= 0);

        var queue = await admin.GetFromJsonAsync<JsonElement>(Url("admin/support/tickets"), TestContext.Current.CancellationToken);
        var item = Assert.Single(queue.GetProperty("tickets").EnumerateArray());
        Assert.Equal(("Hi, I asked for it two days ago.", "Trader", 1), (item.GetProperty("preview").GetString(), item.GetProperty("lastAuthor").GetString(), item.GetProperty("messages").GetInt32()));
        Assert.Equal((1, 0, 0, 1), Counts(queue));
        var summary = await admin.GetFromJsonAsync<JsonElement>(Url("admin/support/summary"), TestContext.Current.CancellationToken);
        Assert.Equal((1, PropFactory.Start), (summary.GetProperty("open").GetInt32(), summary.GetProperty("oldestWaiting").GetDateTimeOffset()));

        using var download = await admin.GetAsync(Url($"admin/support/attachments/{attachment.GetProperty("id").GetGuid()}"), TestContext.Current.CancellationToken);
        Assert.Equal(Png, await download.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(("image/png", "attachment", "nosniff"), (download.Content.Headers.ContentType?.MediaType, download.Content.Headers.ContentDisposition?.DispositionType, download.Headers.GetValues("X-Content-Type-Options").Single()));
        Assert.Contains("sandbox", download.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);

        // The firm answers, and the trader gets the answer by email and sees it unread.
        await factory.AdvanceAsync(TimeSpan.FromHours(1));
        using var answer = await PostFormAsync(admin, $"admin/support/tickets/{ticketId}/messages", new() { ["body"] = "It was paid today.\nThe reference is wire-17." });
        var answered = await JsonAsync(answer, HttpStatusCode.OK);
        Assert.Equal("Answered", answered.GetProperty("status").GetString());
        Assert.Equal(("Firm", Admin), (answered.GetProperty("messages")[1].GetProperty("author").GetString(), answered.GetProperty("messages")[1].GetProperty("adminEmail").GetString()));
        var toTrader = await factory.Emails.WaitForAsync("anna@test.example", "Demo Firm answered your ticket #1");
        Assert.Contains("It was paid today.\nThe reference is wire-17.", toTrader.Body, StringComparison.Ordinal);
        Assert.Contains("It was paid today.<br>The reference is wire-17.", toTrader.Html, StringComparison.Ordinal);
        Assert.Contains($"/support/{ticketId}", toTrader.Body, StringComparison.Ordinal);
        Assert.Equal((1, 1), await TraderSummaryAsync(trader));

        var seen = await trader.GetFromJsonAsync<JsonElement>(Url($"support/tickets/{ticketId}"), TestContext.Current.CancellationToken);
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("messages")[1].GetProperty("adminEmail").ValueKind);
        Assert.True(seen.GetProperty("unread").GetBoolean());
        var mine = await trader.GetFromJsonAsync<JsonElement>(Url("support/tickets"), TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(mine.GetProperty("tickets").EnumerateArray()).GetProperty("unread").GetBoolean());
        using var read = await trader.PostAsync(Url($"support/tickets/{ticketId}/read"), null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        Assert.Equal((1, 0), await TraderSummaryAsync(trader));
        Assert.False((await trader.GetFromJsonAsync<JsonElement>(Url($"support/tickets/{ticketId}"), TestContext.Current.CancellationToken)).GetProperty("unread").GetBoolean());

        // The trader writes again, so it waits for the firm, and the administrators get one email however often the trader writes.
        await factory.AdvanceAsync(TimeSpan.FromHours(1));
        var again = await JsonAsync(await PostFormAsync(trader, $"support/tickets/{ticketId}/messages", new() { ["body"] = "Thanks, but I cannot see it." }), HttpStatusCode.OK);
        await factory.AdvanceAsync(TimeSpan.FromMinutes(5));
        var more = await JsonAsync(await PostFormAsync(trader, $"support/tickets/{ticketId}/messages", new() { ["body"] = "My bank is Nordea." }, ("statement.pdf", Pdf)), HttpStatusCode.OK);
        Assert.Equal(("Open", "Open"), (again.GetProperty("status").GetString(), more.GetProperty("status").GetString()));
        Assert.Equal(PropFactory.Start.AddHours(2), more.GetProperty("waitingSince").GetDateTimeOffset());
        Assert.Equal(4, more.GetProperty("messages").GetArrayLength());
        await factory.Emails.WaitForAsync(Admin, "anna@test.example wrote in support ticket #1");
        Assert.Equal(2L, await factory.ScalarAsync("select count(*) from email_outbox where kind = 'firmSupport'"));

        // Closed by the trader, opened again by a new message, and answered and closed by the firm at once.
        var closed = await JsonAsync(await trader.PostAsync(Url($"support/tickets/{ticketId}/close"), null, TestContext.Current.CancellationToken), HttpStatusCode.OK);
        Assert.Equal(("Closed", "Trader"), (closed.GetProperty("status").GetString(), closed.GetProperty("closedBy").GetString()));
        Assert.Equal((0, 0), await TraderSummaryAsync(trader));
        var reopened = await JsonAsync(await PostFormAsync(trader, $"support/tickets/{ticketId}/messages", new() { ["body"] = "Still nothing." }), HttpStatusCode.OK);
        Assert.Equal(("Open", JsonValueKind.Null), (reopened.GetProperty("status").GetString(), reopened.GetProperty("closedAt").ValueKind));
        Assert.Equal(3L, await factory.ScalarAsync("select count(*) from email_outbox where kind = 'firmSupport'"));
        var done = await JsonAsync(await PostFormAsync(admin, $"admin/support/tickets/{ticketId}/messages", new() { ["body"] = "Sent again.", ["close"] = "true" }), HttpStatusCode.OK);
        Assert.Equal(("Closed", "Firm", Admin), (done.GetProperty("status").GetString(), done.GetProperty("closedBy").GetString(), done.GetProperty("closedByAdmin").GetString()));
        var asTrader = await trader.GetFromJsonAsync<JsonElement>(Url($"support/tickets/{ticketId}"), TestContext.Current.CancellationToken);
        Assert.Equal(("Firm", JsonValueKind.Null), (asTrader.GetProperty("closedBy").GetString(), asTrader.GetProperty("closedByAdmin").ValueKind));
        await Eventually.ThatAsync(
            () => factory.Emails.Sent.Any(e => e.To == "anna@test.example" && e.Body.Contains("Demo Firm has closed the ticket.", StringComparison.Ordinal)),
            "the answer that closed the ticket");
    }

    [Fact]
    public async Task WhatATicketNeedsIsCheckedBeforeItIsSaved()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var accountId = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        var othersAccount = (await factory.StartActiveAccountAsync("bert@test.example")).GetProperty("id").GetGuid();
        using var trader = await factory.LogInAsTraderAsync(accountId);

        Assert.Equal((422, "subject"), await RefusedAsync(trader, new() { ["subject"] = " \n ", ["body"] = "Hello" }));
        Assert.Equal((422, "subject"), await RefusedAsync(trader, new() { ["subject"] = new string('a', 121), ["body"] = "Hello" }));
        Assert.Equal((422, "body"), await RefusedAsync(trader, new() { ["subject"] = "Help", ["body"] = " \r\n " }));
        Assert.Equal((422, "body"), await RefusedAsync(trader, new() { ["subject"] = "Help", ["body"] = new string('a', 5_001) }));
        Assert.Equal((422, "accountId"), await RefusedAsync(trader, new() { ["subject"] = "Help", ["body"] = "Hello", ["accountId"] = othersAccount.ToString() }));
        Assert.Equal((422, "accountId"), await RefusedAsync(trader, new() { ["subject"] = "Help", ["body"] = "Hello", ["accountId"] = Guid.NewGuid().ToString() }));
        Assert.Equal((422, "files"), await RefusedAsync(trader, Hello(), ("1.png", Png), ("2.png", Png), ("3.png", Png), ("4.png", Png)));
        Assert.Equal((413, "files"), await RefusedAsync(trader, Hello(), ("big.png", [.. Png, .. new byte[5 * 1024 * 1024]])));
        Assert.Equal((415, "files"), await RefusedAsync(trader, Hello(), ("notes.txt", "just text"u8.ToArray())));
        Assert.Equal(0L, await factory.ScalarAsync("select count(*) from support_tickets"));

        // A trader has at most ten tickets that are not closed at once.
        var ids = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            ids.Add((await JsonAsync(await PostFormAsync(trader, "support/tickets", Hello()), HttpStatusCode.Created)).GetProperty("id").GetGuid());
        }

        Assert.Equal((409, (string?)null), await RefusedAsync(trader, Hello()));
        await JsonAsync(await trader.PostAsync(Url($"support/tickets/{ids[0]}/close"), null, TestContext.Current.CancellationToken), HttpStatusCode.OK);
        var eleventh = await JsonAsync(await PostFormAsync(trader, "support/tickets", Hello()), HttpStatusCode.Created);
        Assert.Equal(11L, eleventh.GetProperty("number").GetInt64());
    }

    [Fact]
    public async Task ATicketIsOnlyReachedByItsTraderAndItsFirm()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), PropFactory.WithOtherFirm());
        using var anna = await factory.LogInAsTraderAsync((await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid());
        using var bert = await factory.LogInAsTraderAsync((await factory.StartActiveAccountAsync("bert@test.example")).GetProperty("id").GetGuid());
        var ticket = await JsonAsync(await PostFormAsync(anna, "support/tickets", Hello(), ("screen.png", Png)), HttpStatusCode.Created);
        var ticketId = ticket.GetProperty("id").GetGuid();
        var attachmentId = ticket.GetProperty("messages")[0].GetProperty("attachments")[0].GetProperty("id").GetGuid();
        using var otherAdmin = factory.CreatePortalClient(PropFactory.OtherFirmHost);
        using var login = await otherAdmin.PostAsJsonAsync(Url("admin/login"), new { email = Admin, password = "other-admin-password" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var bertReads = await bert.GetAsync(Url($"support/tickets/{ticketId}"), TestContext.Current.CancellationToken);
        using var bertWrites = await PostFormAsync(bert, $"support/tickets/{ticketId}/messages", new() { ["body"] = "Me too." });
        using var bertCloses = await bert.PostAsync(Url($"support/tickets/{ticketId}/close"), null, TestContext.Current.CancellationToken);
        using var bertMarks = await bert.PostAsync(Url($"support/tickets/{ticketId}/read"), null, TestContext.Current.CancellationToken);
        using var bertDownloads = await bert.GetAsync(Url($"support/attachments/{attachmentId}"), TestContext.Current.CancellationToken);
        var bertsList = await bert.GetFromJsonAsync<JsonElement>(Url("support/tickets"), TestContext.Current.CancellationToken);
        using var traderAsAdmin = await anna.GetAsync(Url($"admin/support/tickets/{ticketId}"), TestContext.Current.CancellationToken);
        using var otherFirmReads = await otherAdmin.GetAsync(Url($"admin/support/tickets/{ticketId}"), TestContext.Current.CancellationToken);
        using var otherFirmAnswers = await PostFormAsync(otherAdmin, $"admin/support/tickets/{ticketId}/messages", new() { ["body"] = "Hello from us." });
        using var otherFirmDownloads = await otherAdmin.GetAsync(Url($"admin/support/attachments/{attachmentId}"), TestContext.Current.CancellationToken);
        var otherFirmsList = await otherAdmin.GetFromJsonAsync<JsonElement>(Url("admin/support/tickets?group=All"), TestContext.Current.CancellationToken);

        Assert.All(new[] { bertReads, bertWrites, bertCloses, bertMarks, bertDownloads }, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Empty(bertsList.GetProperty("tickets").EnumerateArray());
        Assert.Equal(HttpStatusCode.Unauthorized, traderAsAdmin.StatusCode);
        Assert.All(new[] { otherFirmReads, otherFirmAnswers, otherFirmDownloads }, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Empty(otherFirmsList.GetProperty("tickets").EnumerateArray());
        Assert.Equal("Open", (await anna.GetFromJsonAsync<JsonElement>(Url($"support/tickets/{ticketId}"), TestContext.Current.CancellationToken)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task TheFirmFindsTicketsByGroupAndSearchPageByPage()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var anna = await factory.LogInAsTraderAsync((await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid());
        using var bert = await factory.LogInAsTraderAsync((await factory.StartActiveAccountAsync("bert@test.example")).GetProperty("id").GetGuid());
        using var admin = await factory.LogInAsAdminAsync();
        var tickets = new List<Guid>();
        foreach (var (trader, subject) in new[] { (anna, "Payout question"), (bert, "Cannot log in"), (anna, "Rules for news trading"), (bert, "Payout method") })
        {
            tickets.Add((await JsonAsync(await PostFormAsync(trader, "support/tickets", new() { ["subject"] = subject, ["body"] = "Hello" }), HttpStatusCode.Created)).GetProperty("id").GetGuid());
            await factory.AdvanceAsync(TimeSpan.FromMinutes(10));
        }

        await JsonAsync(await PostFormAsync(admin, $"admin/support/tickets/{tickets[1]}/messages", new() { ["body"] = "Try a new password." }), HttpStatusCode.OK);
        await JsonAsync(await admin.PostAsync(Url($"admin/support/tickets/{tickets[2]}/close"), null, TestContext.Current.CancellationToken), HttpStatusCode.OK);

        // Open tickets come the longest waiting first, the others the latest written in first.
        var open = await ListAsync(admin, "");
        var all = await ListAsync(admin, "?group=All");
        Assert.Equal([1L, 4L], Numbers(open));
        Assert.Equal((2, 1, 1, 4), Counts(open));
        Assert.Equal([3L, 2L, 4L, 1L], Numbers(all));
        Assert.Equal([4L, 1L], Numbers(await ListAsync(admin, "?group=All&search=PAYOUT")));
        Assert.Equal([2L], Numbers(await ListAsync(admin, "?group=All&search=%232")));
        Assert.Equal([3L, 1L], Numbers(await ListAsync(admin, "?group=All&search=anna@")));
        Assert.Equal((1, 0, 1, 2), Counts(await ListAsync(admin, "?search=anna@")));
        Assert.Equal([2L], Numbers(await ListAsync(admin, "?group=Answered")));
        Assert.Equal([3L], Numbers(await ListAsync(admin, "?group=Closed")));

        var firstPage = await ListAsync(admin, "?group=All&limit=3");
        var secondPage = await ListAsync(admin, $"?group=All&limit=3&cursor={firstPage.GetProperty("next").GetString()}");
        var firstOpen = await ListAsync(admin, "?limit=1");
        var secondOpen = await ListAsync(admin, $"?limit=1&cursor={firstOpen.GetProperty("next").GetString()}");
        Assert.Equal([3L, 2L, 4L], Numbers(firstPage));
        Assert.Equal([1L], Numbers(secondPage));
        Assert.Equal(JsonValueKind.Null, secondPage.GetProperty("next").ValueKind);
        Assert.Equal([1L], Numbers(firstOpen));
        Assert.Equal([4L], Numbers(secondOpen));
        using var badCursor = await admin.GetAsync(Url("admin/support/tickets?cursor=nonsense"), TestContext.Current.CancellationToken);
        using var tooMany = await admin.GetAsync(Url("admin/support/tickets?limit=101"), TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity), (badCursor.StatusCode, tooMany.StatusCode));

        // A trader sees only their own, the latest first.
        var annas = await anna.GetFromJsonAsync<JsonElement>(Url("support/tickets?limit=1"), TestContext.Current.CancellationToken);
        var annasRest = await anna.GetFromJsonAsync<JsonElement>(Url($"support/tickets?limit=1&cursor={annas.GetProperty("next").GetString()}"), TestContext.Current.CancellationToken);
        Assert.Equal([3L], Numbers(annas));
        Assert.Equal([1L], Numbers(annasRest));
    }

    [Fact]
    public async Task TheFirmCanTurnTheSupportEmailsOff()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var trader = await factory.LogInAsTraderAsync((await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid());
        using var admin = await factory.LogInAsAdminAsync();
        using var saved = await admin.PutAsJsonAsync(
            Url("admin/firm/email-settings"),
            new { settings = new { firmSupport = false, traderSupportAnswers = false } },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var ticketId = (await JsonAsync(await PostFormAsync(trader, "support/tickets", Hello()), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await factory.AdvanceAsync(TimeSpan.FromMinutes(1));
        await JsonAsync(await PostFormAsync(admin, $"admin/support/tickets/{ticketId}/messages", new() { ["body"] = "Hello to you." }), HttpStatusCode.OK);

        Assert.Equal(0L, await factory.ScalarAsync("select count(*) from email_outbox where kind in ('firmSupport', 'traderSupportAnswers')"));
        Assert.Equal((1, 1), await TraderSummaryAsync(trader));
    }

    private static Dictionary<string, string> Hello() => new() { ["subject"] = "A question", ["body"] = "Hello" };

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, Dictionary<string, string> fields, params (string Name, byte[] Content)[] files)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (name, value) in fields)
        {
            form.Add(new StringContent(value, Encoding.UTF8), name);
        }

        foreach (var (name, content) in files)
        {
            form.Add(new ByteArrayContent(content), "files", name);
        }

        return await client.PostAsync(Url(path), form, TestContext.Current.CancellationToken);
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

    // The status and the field the problem is about, when a new ticket is refused.
    private static async Task<(int Status, string? Field)> RefusedAsync(HttpClient trader, Dictionary<string, string> fields, params (string Name, byte[] Content)[] files)
    {
        using var response = await PostFormAsync(trader, "support/tickets", fields, files);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return ((int)response.StatusCode, problem.TryGetProperty("field", out var field) ? field.GetString() : null);
    }

    private static async Task<(int Active, int Unread)> TraderSummaryAsync(HttpClient trader)
    {
        var summary = await trader.GetFromJsonAsync<JsonElement>(Url("support/summary"), TestContext.Current.CancellationToken);
        return (summary.GetProperty("active").GetInt32(), summary.GetProperty("unread").GetInt32());
    }

    private static Task<JsonElement> ListAsync(HttpClient admin, string query) =>
        admin.GetFromJsonAsync<JsonElement>(Url($"admin/support/tickets{query}"), TestContext.Current.CancellationToken);

    private static List<long> Numbers(JsonElement page) => [.. page.GetProperty("tickets").EnumerateArray().Select(t => t.GetProperty("number").GetInt64())];

    private static (int Open, int Answered, int Closed, int All) Counts(JsonElement page)
    {
        var counts = page.GetProperty("counts");
        return (counts.GetProperty("open").GetInt32(), counts.GetProperty("answered").GetInt32(), counts.GetProperty("closed").GetInt32(), counts.GetProperty("all").GetInt32());
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);
}
