using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Prop.Api.Challenges;
using Prop.Api.Email;
using Prop.Api.Firms;
using Prop.Api.Payments;
using Prop.Api.Trading;

namespace Prop.Api.Tests.Support;

/// <summary>
/// The real prop service against its own Postgres database, with a fake trading platform, a clock that
/// moves only when told and a receiver for webhooks. Pass the same database, platform and clock to a new
/// factory to restart the service.
/// </summary>
internal sealed class PropFactory : WebApplicationFactory<Program>
{
    /// <summary>The development firm's key for the firm API, from appsettings.Development.json.</summary>
    public const string FirmApiKey = "dev-prop-key";

    public const string WebhookSecret = "a-webhook-secret-of-at-least-32-characters";

    public const string OtherFirmKey = "other-prop-key";

    /// <summary>Where the second firm's portal is reached.</summary>
    public const string OtherFirmHost = "portal.other.test";

    /// <summary>The development firm's administrator, from appsettings.Development.json.</summary>
    public const string AdminEmail = "admin@test.com";

    public const string AdminPassword = "admin";

    /// <summary>The password traders choose when they accept an invitation in the tests.</summary>
    public const string TraderPassword = "a-good-password";

    /// <summary>Where firms sign up, from appsettings.Development.json.</summary>
    public const string PlatformHost = "app.localhost";

    /// <summary>The password an administrator chooses when signing up in the tests.</summary>
    public const string SignupPassword = "a-signup-password";

    /// <summary>Our admin view, from appsettings.Development.json.</summary>
    public const string OpsHost = "ops.localhost";

    /// <summary>Our staff member, from appsettings.Development.json.</summary>
    public const string StaffEmail = "ops@test.com";

    public const string StaffPassword = "ops";

    /// <summary>A Monday, 10:00 in Stockholm.</summary>
    public static readonly DateTimeOffset Start = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly IReadOnlyDictionary<string, string> _settings;

    private PropFactory(string connectionString, FakeTradingPlatform trading, FakeTimeProvider time, IReadOnlyDictionary<string, string>? settings)
    {
        _connectionString = connectionString;
        _settings = settings ?? new Dictionary<string, string>();
        Trading = trading;
        Time = time;
    }

    public FakeTimeProvider Time { get; }

    /// <summary>The service's database, for a second service on it.</summary>
    public string ConnectionString => _connectionString;

    public FakeTradingPlatform Trading { get; }

    public WebhookReceiver Webhooks { get; } = new();

    public FakeEmailSender Emails { get; private init; } = new();

    public FakeStripe Stripe { get; } = new();

    /// <summary>Settings that give the development firm a webhook to <see cref="Webhooks"/>.</summary>
    public static Dictionary<string, string> WithWebhook() => new()
    {
        ["Firms:0:Webhook:Url"] = "https://firm.test/webhooks",
        ["Firms:0:Webhook:Secret"] = WebhookSecret,
    };

    /// <summary>Settings that add a second firm with its own key, portal and server on the trading platform.</summary>
    public static Dictionary<string, string> WithOtherFirm() => new()
    {
        ["Firms:1:Id"] = "other-firm",
        ["Firms:1:Name"] = "Other Firm",
        ["Firms:1:ApiKeySha256"] = FirmCatalog.HashApiKey(OtherFirmKey),
        ["Firms:1:Trading:Server"] = "other-firm",
        ["Firms:1:Trading:ApiKey"] = "other-admin-key",
        ["Firms:1:Trading:Group"] = "other",
        ["Firms:1:Portal:Url"] = $"https://{OtherFirmHost}/",
        ["Firms:1:Portal:Hosts:0"] = OtherFirmHost,
        ["Firms:1:SeedAdmins:0:Email"] = AdminEmail,
        ["Firms:1:SeedAdmins:0:Password"] = "other-admin-password",
    };

    public static PropFactory Create(string connectionString, IReadOnlyDictionary<string, string>? settings = null)
    {
        var time = new FakeTimeProvider(Start);
        return new PropFactory(connectionString, new FakeTradingPlatform(time), time, settings);
    }

    /// <summary>A new service on the same database, trading platform, clock and mail, as after a restart.</summary>
    public PropFactory Restart() => new(_connectionString, Trading, Time, _settings) { Emails = Emails };

    /// <summary>The portal host of a firm that signed up, from the address template in appsettings.Development.json.</summary>
    public static string HostOf(string firmId) => $"{firmId}.localhost";

    /// <summary>A browser on the platform's own address, where firms sign up.</summary>
    public HttpClient CreatePlatformClient() => CreatePortalClient(PlatformHost);

    /// <summary>A complete application for our review, as a firm sends it.</summary>
    public static object Application(string companyName = "Acme Trading Ltd") => new
    {
        companyName,
        registrationNumber = "559000-1234",
        country = "SE",
        address = "Storgatan 1, 111 22 Stockholm",
        website = "https://acme.test",
        contactName = "Anna Andersson",
        contactPhone = "+46 70 123 45 67",
        owners = new[] { new { name = "Anna Andersson", sharePercent = 60m }, new { name = "Bert Berg", sharePercent = 40m } },
        termsUrl = "https://acme.test/terms",
        links = new[] { "https://x.com/acme" },
        description = "We sell two-step challenges and pay out every two weeks.",
    };

    /// <summary>Our staff member logs in to our admin view. Returns the staff member's browser.</summary>
    public async Task<HttpClient> LogInAsStaffAsync()
    {
        var ops = CreatePortalClient(OpsHost);
        using var login = await ops.PostAsJsonAsync(new Uri("/api/portal/ops/login", UriKind.Relative), new { email = StaffEmail, password = StaffPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return ops;
    }

    /// <summary>
    /// The firm sends a complete application, without a deposit as in most tests, and our staff approve it, so the
    /// firm can go live by paying.
    /// </summary>
    public async Task ApproveAsync(HttpClient admin, string firmId)
    {
        using var saved = await admin.PutAsJsonAsync(new Uri("/api/portal/admin/verification/application", UriKind.Relative), Application());
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var submitted = await admin.PostAsync(new Uri("/api/portal/admin/verification/submit", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await submitted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("checkoutUrl").ValueKind);
        using var ops = await LogInAsStaffAsync();
        using var approved = await ops.PostAsJsonAsync(new Uri($"/api/portal/ops/firms/{firmId}/approve", UriKind.Relative), new { message = (string?)null });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
    }

    /// <summary>
    /// Signs a firm up without email confirmation, as in development, and follows the link to its admin panel.
    /// Returns the administrator's browser on the firm's portal.
    /// </summary>
    public async Task<HttpClient> SignUpAsync(string firmId, string email = "owner@firm.test")
    {
        using var platform = CreatePlatformClient();
        using var response = await platform.PostAsJsonAsync(
            new Uri("/api/portal/signup", UriKind.Relative),
            new { firmName = $"Firm {firmId}", firmId, email, password = SignupPassword, acceptTerms = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var adminUrl = new Uri((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("adminUrl").GetString()!);
        return await WelcomeAsync(adminUrl);
    }

    /// <summary>Opens the link that logs a new firm's administrator in. Returns the administrator's browser on the firm's portal.</summary>
    public async Task<HttpClient> WelcomeAsync(Uri adminUrl)
    {
        var admin = CreatePortalClient(adminUrl.Host);
        var token = System.Web.HttpUtility.ParseQueryString(adminUrl.Query)["token"];
        using var welcome = await admin.PostAsJsonAsync(new Uri("/api/portal/admin/welcome", UriKind.Relative), new { token });
        Assert.Equal(HttpStatusCode.OK, welcome.StatusCode);
        return admin;
    }

    /// <summary>
    /// Moves the clock a second at a time until the firm is in the sandbox, for when creating its server must be
    /// tried again. The clock is moved again and again, since the retry may not be waiting yet the first time.
    /// </summary>
    public async Task AdvanceUntilProvisionedAsync(HttpClient admin)
    {
        await Eventually.ThatAsync(
            async () =>
            {
                await AdvanceAsync(TimeSpan.FromSeconds(1));
                return (await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/portal/admin/firm", UriKind.Relative))).GetProperty("status").GetString() == "Sandbox";
            },
            "the firm to get its trading server");
    }

    /// <summary>Waits until the firm's server on the trading platform exists and the firm is in the sandbox.</summary>
    public static async Task WaitUntilProvisionedAsync(HttpClient admin)
    {
        await Eventually.ThatAsync(
            async () => (await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/portal/admin/firm", UriKind.Relative))).GetProperty("status").GetString() == "Sandbox",
            "the firm to get its trading server");
    }

    public HttpClient CreateFirmClient(string apiKey = FirmApiKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(FirmApiKeyFilter.HeaderName, apiKey);
        return client;
    }

    /// <summary>A browser on a firm's portal: it keeps the session cookie, and is on the development firm's host unless told otherwise.</summary>
    public HttpClient CreatePortalClient(string? host = null)
    {
        var client = CreateClient();
        if (host is not null)
        {
            client.DefaultRequestHeaders.Add("X-Forwarded-Host", host);
        }

        return client;
    }

    /// <summary>The firm invites the account's trader to its portal. Returns the invitation's token.</summary>
    public async Task<string> InviteAsync(Guid accountId, string apiKey = FirmApiKey)
    {
        using var firm = CreateFirmClient(apiKey);
        using var response = await firm.PostAsync(new Uri($"/api/firm/v1/accounts/{accountId}/invite", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var url = new Uri((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!);
        return System.Web.HttpUtility.ParseQueryString(url.Query)["token"]!;
    }

    /// <summary>The account's trader accepts an invitation to the portal. Returns the trader's browser.</summary>
    public async Task<HttpClient> LogInAsTraderAsync(Guid accountId)
    {
        var token = await InviteAsync(accountId);
        var portal = CreatePortalClient();
        using var accepted = await portal.PostAsJsonAsync(new Uri("/api/portal/invites/accept", UriKind.Relative), new { token, password = TraderPassword });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        return portal;
    }

    /// <summary>The development firm's administrator logs in to the portal. Returns the administrator's browser.</summary>
    public async Task<HttpClient> LogInAsAdminAsync()
    {
        var portal = CreatePortalClient();
        using var login = await portal.PostAsJsonAsync(new Uri("/api/portal/admin/login", UriKind.Relative), new { email = AdminEmail, password = AdminPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return portal;
    }

    /// <summary>Starts the challenge for the email and waits until its first trading account is open.</summary>
    public async Task<JsonElement> StartActiveAccountAsync(string email = "anna@test.example", string challengeId = "two-step-100k")
    {
        using var firm = CreateFirmClient();
        using var response = await firm.PostAsJsonAsync(new Uri("/api/firm/v1/accounts", UriKind.Relative), new { email, challengeId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return await WaitForAccountAsync(id, a => a.GetProperty("status").GetString() == "Active" && a.GetProperty("dailyFloor").ValueKind == JsonValueKind.Number);
    }

    public async Task<JsonElement> GetAccountAsync(Guid id)
    {
        using var firm = CreateFirmClient();
        return await firm.GetFromJsonAsync<JsonElement>(new Uri($"/api/firm/v1/accounts/{id}", UriKind.Relative));
    }

    /// <summary>Waits until the account is as the condition wants. The service works in the background.</summary>
    public async Task<JsonElement> WaitForAccountAsync(Guid id, Func<JsonElement, bool> condition)
    {
        JsonElement account = default;
        await Eventually.ThatAsync(
            async () =>
            {
                account = await GetAccountAsync(id);
                return condition(account);
            },
            $"account {id} to change");
        return account;
    }

    /// <summary>Moves the clock and gives the background work a moment to notice.</summary>
    public async Task AdvanceAsync(TimeSpan by)
    {
        Time.Advance(by);
        await Task.Delay(50);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Prop", _connectionString);
        builder.UseSetting("TradingPlatform:Url", "https://trading.test/");

        // Development turns the login rules off. The tests check the rules that hold everywhere else.
        builder.UseSetting("Login:MinimumPasswordLength", "10");
        builder.UseSetting("Login:AttemptsPerMinute", "10");
        builder.UseSetting("Login:SessionLifetime", "12:00:00");

        // Most tests are about other things than the deposit for our review. The review tests pay it.
        builder.UseSetting("Billing:ReviewDeposit", "0");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter, LoopbackConnection>();
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<ITradingPlatform>(Trading);
            services.AddSingleton<ITradingPartner>(Trading);
            services.AddSingleton<IEmailSender>(Emails);
            services.AddHttpClient(WebhookWorker.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Webhooks);
            services.AddHttpClient(StripeClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Stripe);
        });
    }
}
