using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Prop.Api.Challenges;
using Prop.Api.Firms;
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

    public FakeTradingPlatform Trading { get; }

    public WebhookReceiver Webhooks { get; } = new();

    /// <summary>Settings that give the development firm a webhook to <see cref="Webhooks"/>.</summary>
    public static Dictionary<string, string> WithWebhook() => new()
    {
        ["Firms:0:Webhook:Url"] = "https://firm.test/webhooks",
        ["Firms:0:Webhook:Secret"] = WebhookSecret,
    };

    public static PropFactory Create(string connectionString, IReadOnlyDictionary<string, string>? settings = null)
    {
        var time = new FakeTimeProvider(Start);
        return new PropFactory(connectionString, new FakeTradingPlatform(time), time, settings);
    }

    /// <summary>A new service on the same database, trading platform and clock, as after a restart.</summary>
    public PropFactory Restart() => new(_connectionString, Trading, Time, _settings);

    public HttpClient CreateFirmClient(string apiKey = FirmApiKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(FirmApiKeyFilter.HeaderName, apiKey);
        return client;
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
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<ITradingPlatform>(Trading);
            services.AddHttpClient(WebhookWorker.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Webhooks);
        });
    }
}
