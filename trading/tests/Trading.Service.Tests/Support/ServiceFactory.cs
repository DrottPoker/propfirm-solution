using System.Net.Http.Json;

using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Trading.Service.Candles;
using Trading.Service.Engine;
using Trading.Service.Feeds;
using Trading.Service.Identity;
using Trading.Service.Persistence;
using Trading.Service.Tenancy;

namespace Trading.Service.Tests.Support;

/// <summary>
/// The real service in memory, with prices pushed by the test, a clock that only moves when told and
/// storage in memory. Pass the same backend to a second factory to restart the service. With a Postgres
/// connection string, the real stores in that database are used instead. Pass a feed to restart with another one.
/// </summary>
internal sealed class ServiceFactory(
    InMemoryBackend? backend = null,
    IReadOnlyDictionary<string, string>? settings = null,
    string? postgresConnectionString = null,
    ManualPriceFeed? feed = null)
    : WebApplicationFactory<Program>
{
    /// <summary>The development firm's admin key, from appsettings.Development.json.</summary>
    public const string AdminApiKey = "dev-admin-key";

    /// <summary>The development firm's server, which traders log in to.</summary>
    public const string DemoServer = "demo-firm";

    public const string TraderPassword = "test-password";

    /// <summary>The prop platform's partner key, from appsettings.Development.json.</summary>
    public const string PartnerApiKey = "dev-partner-key";

    private readonly StartupFailureLog _startupFailures = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));

    public ManualPriceFeed Feed { get; } = feed ?? new();

    public InMemoryBackend Backend { get; } = backend ?? new InMemoryBackend();

    public InMemoryJournal Journal => Backend.Journal;

    public EngineHost Engine => Services.GetRequiredService<EngineHost>();

    public static string EmailOf(string accountId) => $"{accountId.ToLowerInvariant()}@test.example";

    /// <summary>Pushes a raw price and waits until the engine has applied it.</summary>
    public async Task PushQuoteAsync(string symbol, decimal bid, decimal ask)
    {
        var target = Engine.QuotesApplied + 1;
        Feed.Push(symbol, bid, ask);
        await Eventually.ThatAsync(() => Engine.QuotesApplied >= target, "the engine to apply the price");
    }

    public HttpClient CreatePartnerClient(string apiKey = PartnerApiKey) => CreateAdminClient(apiKey);

    public HttpClient CreateAdminClient(string apiKey = AdminApiKey)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        return client;
    }

    /// <summary>
    /// Creates a trader who owns a new account and returns a client logged in as that trader.
    /// The client also carries the admin key, so tests can call both APIs.
    /// </summary>
    public async Task<HttpClient> CreateTraderClientAsync(string accountId, decimal balance = 100_000m)
    {
        using var admin = CreateAdminClient();
        var user = await admin.PostJsonAsync("/api/admin/v1/users", new { email = EmailOf(accountId), password = TraderPassword });
        await admin.PostJsonAsync(
            "/api/admin/v1/accounts",
            new { accountId, groupId = "standard", initialBalance = balance, ownerUserId = user.GetProperty("userId").GetGuid() });
        return await LoginAsync(EmailOf(accountId), TraderPassword);
    }

    /// <summary>A client logged in as the trader, which also carries the admin key.</summary>
    public async Task<HttpClient> LoginAsync(string email, string password, string server = DemoServer)
    {
        var client = CreateAdminClient();
        client.DefaultRequestHeaders.Add("Cookie", await LoginCookieAsync(email, password, server));
        return client;
    }

    public async Task<string> LoginCookieAsync(string email, string password, string server = DemoServer)
    {
        using var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var response = await client.PostAsJsonAsync(new Uri("/api/auth/login", UriKind.Relative), new { server, email, password });
        response.EnsureSuccessStatusCode();
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("trading_session=", StringComparison.Ordinal));
        return setCookie.Split(';')[0];
    }

    public HubConnection CreateHubConnection(string? cookie = null) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "/hubs/trading"), options =>
            {
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (cookie is not null)
                {
                    options.Headers["Cookie"] = cookie;
                }
            })
            .Build();

    /// <summary>
    /// Starts the service. When it fails at once, the host can be disposed before the factory waits for it, which
    /// hides the reason behind a disposed object. The reason is then taken from the host's log.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        try
        {
            return base.CreateHost(builder);
        }
        catch (ObjectDisposedException disposed) when (_startupFailures.Failure is { } failure)
        {
            throw new InvalidOperationException("The service failed to start.", new AggregateException(failure, disposed));
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Tests must not depend on the developer's user secrets, such as a real price feed.
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            foreach (var secrets in configuration.Sources.OfType<JsonConfigurationSource>().Where(s => s.Path == "secrets.json").ToList())
            {
                configuration.Sources.Remove(secrets);
            }
        });

        // Development turns the login rules off. The tests check the rules that hold everywhere else.
        builder.UseSetting("Login:MinimumPasswordLength", "10");
        builder.UseSetting("Login:AttemptsPerMinute", "10");
        builder.UseSetting("Login:SessionLifetime", "12:00:00");

        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        if (postgresConnectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:Trading", postgresConnectionString);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(_startupFailures);
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<IPriceFeed>(Feed);
            if (postgresConnectionString is null)
            {
                services.AddSingleton<IEngineJournal>(Backend.Journal);
                services.AddSingleton<IUserStore>(Backend.Users);
                services.AddSingleton<IXmlRepository>(Backend.Keys);
                services.AddSingleton<ILoginLinkStore>(Backend.LoginLinks);
                services.AddSingleton<ITenantStore>(Backend.Tenants);
                services.AddSingleton<IChartStore>(Backend.Charts);
            }
        });
    }
}
