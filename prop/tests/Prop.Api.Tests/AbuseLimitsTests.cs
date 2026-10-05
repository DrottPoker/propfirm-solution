using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>
/// The limits that keep a free user from misusing the platform (ADR 0044 and 0045): webhooks only to the public internet,
/// a robot check and a few confirmation emails a day on sign-up, caps on support tickets, and calls per minute.
/// </summary>
public sealed class AbuseLimitsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8];

    [Theory]
    [InlineData("https://127.0.0.1/hooks")]
    [InlineData("https://10.1.2.3/hooks")]
    [InlineData("https://169.254.169.254/latest")]
    [InlineData("https://[::1]/hooks")]
    [InlineData("https://localhost/hooks")]
    [InlineData("https://prop-api.internal/hooks")]
    public async Task AWebhookCannotPointIntoOurNetwork(string url)
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.LogInAsAdminAsync();

        using var refused = await admin.PutAsJsonAsync(Url("admin/firm/webhook"), new { url }, TestContext.Current.CancellationToken);
        using var saved = await admin.PutAsJsonAsync(Url("admin/firm/webhook"), new { url = "https://hooks.firm.example/prop" }, TestContext.Current.CancellationToken);

        Assert.Equal((HttpStatusCode.UnprocessableEntity, "The webhook address must be on the public internet."), (refused.StatusCode, await TitleAsync(refused)));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    [Fact]
    public async Task TheRobotCheckComesBeforeTheSignUp()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), FakeRobotCheck.Settings());
        using var platform = factory.CreatePlatformClient();

        var settings = await platform.GetFromJsonAsync<JsonElement>(Url("platform"), TestContext.Current.CancellationToken);
        using var without = await SignUpAsync(platform, "acme", "owner@firm.test", null);
        using var wrong = await SignUpAsync(platform, "acme", "owner@firm.test", "a-made-up-answer");
        factory.Robots.Down = true;
        using var down = await SignUpAsync(platform, "acme", "owner@firm.test", FakeRobotCheck.GoodAnswer);
        factory.Robots.Down = false;
        using var passed = await SignUpAsync(platform, "acme", "owner@firm.test", FakeRobotCheck.GoodAnswer);

        Assert.Equal(FakeRobotCheck.SiteKey, settings.GetProperty("robotCheckSiteKey").GetString());
        foreach (var refused in new[] { without, wrong })
        {
            var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal((HttpStatusCode.UnprocessableEntity, "robotCheck"), (refused.StatusCode, problem.GetProperty("field").GetString()));
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
        Assert.Equal(HttpStatusCode.OK, passed.StatusCode);
    }

    [Fact]
    public async Task WithoutKeysThereIsNoRobotCheck()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var platform = factory.CreatePlatformClient();

        var settings = await platform.GetFromJsonAsync<JsonElement>(Url("platform"), TestContext.Current.CancellationToken);
        using var signedUp = await SignUpAsync(platform, "acme", "owner@firm.test", null);

        Assert.Equal(JsonValueKind.Null, settings.GetProperty("robotCheckSiteKey").ValueKind);
        Assert.Equal(HttpStatusCode.OK, signedUp.StatusCode);
    }

    [Fact]
    public async Task AnAddressGetsAFewConfirmationEmailsADayWithNothingTheSenderChose()
    {
        await using var factory = PropFactory.Create(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string> { ["Signup:RequireEmailVerification"] = "true", ["Login:AttemptsPerMinute"] = "0" });
        using var platform = factory.CreatePlatformClient();

        var answers = new List<HttpStatusCode>();
        foreach (var firmId in new[] { "acme", "beta", "gamma", "delta" })
        {
            using var response = await SignUpAsync(platform, firmId, "victim@mail.example", null, firmName: "Claim your prize at scam.example");
            answers.Add(response.StatusCode);
        }

        await factory.AdvanceAsync(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        using var nextDay = await SignUpAsync(platform, "epsilon", "victim@mail.example", null);

        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests], answers);
        Assert.Equal(HttpStatusCode.Accepted, nextDay.StatusCode);
        var emails = factory.Emails.Sent.Where(e => e.To == "victim@mail.example").ToList();
        Assert.Equal(4, emails.Count);
        Assert.All(emails, e => Assert.DoesNotContain("scam.example", e.Subject + e.Body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATicketHasAtMostTwoHundredMessages()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var accountId = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var trader = await factory.LogInAsTraderAsync(accountId);
        using var admin = await factory.LogInAsAdminAsync();
        using var opened = await PostFormAsync(trader, "support/tickets", new() { ["subject"] = "A question", ["body"] = "Hello" });
        var ticketId = (await opened.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();

        // The ticket gets 198 more messages straight in the database, so it has 199.
        await factory.ScalarAsync(
            $"""
            insert into support_messages (id, ticket_id, position, author, body, created_at)
            select gen_random_uuid(), '{ticketId}', p, 'Trader', 'More', now() from generate_series(2, 199) p
            """);
        await factory.ScalarAsync($"update support_tickets set messages = 199 where id = '{ticketId}'");
        using var last = await PostFormAsync(trader, $"support/tickets/{ticketId}/messages", new() { ["body"] = "The 200th" });
        using var traderRefused = await PostFormAsync(trader, $"support/tickets/{ticketId}/messages", new() { ["body"] = "One more" });
        using var firmRefused = await PostFormAsync(admin, $"admin/support/tickets/{ticketId}/messages", new() { ["body"] = "An answer" });

        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        Assert.True(last.IsSuccessStatusCode);
        Assert.Equal(
            (HttpStatusCode.Conflict, "This ticket has 200 messages, as many as a ticket can have. Open a new ticket to write more."),
            (traderRefused.StatusCode, await TitleAsync(traderRefused)));
        Assert.Equal(HttpStatusCode.Conflict, firmRefused.StatusCode);
    }

    [Fact]
    public async Task ATraderAddsTwentyFiveMegabytesOfFilesADay()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var accountId = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var trader = await factory.LogInAsTraderAsync(accountId);
        using var admin = await factory.LogInAsAdminAsync();
        using var opened = await PostFormAsync(trader, "support/tickets", new() { ["subject"] = "A question", ["body"] = "Hello" }, ("first.png", Png));
        var ticketId = (await opened.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();

        // The first file is counted as almost the whole day's allowance.
        await factory.ScalarAsync($"update support_attachments set size = {(25 * 1024 * 1024) - 4} where ticket_id = '{ticketId}'");
        using var refused = await PostFormAsync(trader, $"support/tickets/{ticketId}/messages", new() { ["body"] = "One more file" }, ("second.png", Png));
        using var withoutFiles = await PostFormAsync(trader, $"support/tickets/{ticketId}/messages", new() { ["body"] = "Without files" });
        using var firmsFile = await PostFormAsync(admin, $"admin/support/tickets/{ticketId}/messages", new() { ["body"] = "Here is ours" }, ("answer.png", Png));
        await factory.AdvanceAsync(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        using var traderTomorrow = await factory.LogInAsTraderAsync(accountId);
        using var tomorrow = await PostFormAsync(traderTomorrow, $"support/tickets/{ticketId}/messages", new() { ["body"] = "One more file" }, ("second.png", Png));

        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode.Conflict, "files"), (refused.StatusCode, problem.GetProperty("field").GetString()));
        Assert.Equal("At most 25 MB of files can be added in a day, and this would go past it. Write without files, or add them tomorrow.", problem.GetProperty("title").GetString());
        Assert.True(withoutFiles.IsSuccessStatusCode);
        Assert.True(firmsFile.IsSuccessStatusCode);
        Assert.True(tomorrow.IsSuccessStatusCode);
    }

    [Fact]
    public async Task SupportIsWrittenAFewTimesAMinuteAtMost()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), new Dictionary<string, string> { ["Limits:SupportWritesPerMinute"] = "2" });
        var accountId = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var trader = await factory.LogInAsTraderAsync(accountId);

        var answers = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await PostFormAsync(trader, "support/tickets", new() { ["subject"] = $"Question {i}", ["body"] = "Hello" });
            answers.Add(response.StatusCode);
            if (i == 2)
            {
                Assert.Equal("Too many tries in a short time. Wait a minute and try again.", await TitleAsync(response));
            }
        }

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], answers);
    }

    [Fact]
    public async Task AFirmCallsItsApiAFewHundredTimesAMinuteAtMost()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync(), new Dictionary<string, string> { ["Limits:FirmApiCallsPerMinute"] = "2" });
        using var firmApi = factory.CreateFirmClient();

        var answers = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await firmApi.GetAsync(new Uri("/api/firm/v1/challenges", UriKind.Relative), TestContext.Current.CancellationToken);
            answers.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], answers);
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static Task<HttpResponseMessage> SignUpAsync(HttpClient platform, string firmId, string email, string? robotCheck, string? firmName = null) =>
        platform.PostAsJsonAsync(
            Url("signup"),
            new { firmName = firmName ?? $"Firm {firmId}", firmId, email, password = PropFactory.SignupPassword, acceptTerms = true, robotCheck },
            TestContext.Current.CancellationToken);

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

    private static async Task<string?> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("title").GetString();
}
