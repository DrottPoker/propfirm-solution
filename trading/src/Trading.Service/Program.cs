using System.Reflection;
using System.Threading.RateLimiting;

using Common.Postgres;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Npgsql;

using Trading.Engine;
using Trading.Service.Api;
using Trading.Service.Candles;
using Trading.Service.Configuration;
using Trading.Service.Engine;
using Trading.Service.Feeds;
using Trading.Service.Identity;
using Trading.Service.Json;
using Trading.Service.Persistence;
using Trading.Service.Realtime;
using Trading.Service.Tenancy;

// The build-time OpenAPI generator loads the app only to read its endpoints.
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<TradingOptions>()
    .Bind(builder.Configuration.GetSection(TradingOptions.SectionName))
    .Validate(o => o.Instruments.All(i => i.Category is not null), "Every instrument in Trading:Instruments needs a Category: Forex, Metals, Indices, Commodities or Crypto.")
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<TradingOptions>, TradingHoursValidation>();
builder.Services.AddOptions<SyntheticFeedOptions>().Bind(builder.Configuration.GetSection(SyntheticFeedOptions.SectionName));
builder.Services.AddOptions<RealtimeOptions>().Bind(builder.Configuration.GetSection(RealtimeOptions.SectionName));
builder.Services.AddOptions<JournalOptions>().Bind(builder.Configuration.GetSection(JournalOptions.SectionName));
builder.Services.AddOptions<ChartOptions>()
    .Bind(builder.Configuration.GetSection(ChartOptions.SectionName))
    .Validate(
        o => o.IsValid(),
        "Charts:History, Charts:QuarterHourHistory and Charts:MinuteHistory must be whole days, each no longer than the one before.")
    .ValidateOnStart();
var terminalOptions = builder.Services.AddOptions<TerminalOptions>()
    .Bind(builder.Configuration.GetSection(TerminalOptions.SectionName))
    .Validate(o => o.Url is { IsAbsoluteUri: true }, "Terminal:Url must be the absolute address of the terminal, for login links.");
if (!isOpenApiGeneration)
{
    terminalOptions.ValidateOnStart();
}

builder.Services.AddOptions<LoginOptions>()
    .Bind(builder.Configuration.GetSection(LoginOptions.SectionName))
    .Validate(
        o => o.MinimumPasswordLength >= 1 && o.AttemptsPerMinute >= 0 && o.SessionLifetime > TimeSpan.Zero,
        "Login needs a password length of at least 1, attempts per minute of 0 or more and a positive session lifetime.")
    .ValidateOnStart();

builder.Services.AddOptions<PriceFeedOptions>()
    .Bind(builder.Configuration.GetSection(PriceFeedOptions.SectionName))
    .Validate(
        o => o.Provider is PriceFeedOptions.SyntheticProvider or PriceFeedOptions.TiingoProvider or PriceFeedOptions.CapitalComProvider,
        "PriceFeed:Provider must be Synthetic, Tiingo or CapitalCom.")
    .Validate(
        o => o.Provider != PriceFeedOptions.TiingoProvider || o.Tiingo.ApiKey.Length > 0,
        "PriceFeed:Tiingo:ApiKey is required. Set it with dotnet user-secrets, never in a file.")
    .Validate(
        o => o.Provider != PriceFeedOptions.CapitalComProvider
            || (o.CapitalCom.ApiKey.Length > 0 && o.CapitalCom.Identifier.Length > 0 && o.CapitalCom.Password.Length > 0),
        "PriceFeed:CapitalCom:ApiKey, Identifier and Password are required. Set them with dotnet user-secrets, never in a file.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
// The hours the feed quotes in. Made-up prices run around the clock, so with them every market is always open (ADR 0050).
builder.Services.AddSingleton(sp =>
{
    var feed = sp.GetRequiredService<IPriceFeed>();
    return sp.GetRequiredService<IOptions<TradingOptions>>().Value.ToEngineConfiguration(feed.FollowsTradingHours ? feed.Name : null);
});
builder.Services.AddSingleton<MarketCatalog>();
builder.Services.AddSingleton<EventLog>();

// Resolved lazily, so tools that only load the app (like the OpenAPI generator) need no database.
builder.Services.AddSingleton(sp => NpgsqlDataSource.Create(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Trading")
    ?? throw new InvalidOperationException("The connection string Trading is missing.")));
builder.Services.AddSingleton(TradingMigrations.All);
builder.Services.AddSingleton<DatabaseSchema>();
builder.Services.AddSingleton<IEngineJournal, PostgresEngineJournal>();
builder.Services.AddSingleton<IChartStore, PostgresChartStore>();
builder.Services.AddSingleton<IUserStore, PostgresUserStore>();
builder.Services.AddSingleton<IXmlRepository, PostgresXmlRepository>();
builder.Services.AddSingleton<ILoginLinkStore, PostgresLoginLinkStore>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<ITenantStore, PostgresTenantStore>();
builder.Services.AddSingleton<TenantCatalog>();
builder.Services.AddSingleton<TenantProvisioner>();
builder.Services.AddOptions<TenancyOptions>().Bind(builder.Configuration.GetSection(TenancyOptions.SectionName));
builder.Services.AddSingleton(sp => new PartnerCatalog(
    sp.GetRequiredService<IConfiguration>().GetSection(PartnerOptions.SectionName).Get<List<PartnerOptions>>() ?? []));

// Login cookies are protected with keys kept in the database, so sessions survive restarts.
builder.Services.AddDataProtection().SetApplicationName("trading-service");
if (!isOpenApiGeneration)
{
    builder.Services.AddOptions<KeyManagementOptions>().Configure<IXmlRepository>((options, repository) => options.XmlRepository = repository);
}
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "trading_session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.SlidingExpiration = true;

    // An API answers with status codes instead of redirecting to a login page.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<IOptions<LoginOptions>>((options, login) => options.ExpireTimeSpan = login.Value.SessionLifetime);
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthEndpoints.LoginRateLimit, context =>
    {
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var attempts = context.RequestServices.GetRequiredService<IOptions<LoginOptions>>().Value.AttemptsPerMinute;
        return attempts == 0
            ? RateLimitPartition.GetNoLimiter(address)
            : RateLimitPartition.GetFixedWindowLimiter(address, _ => new FixedWindowRateLimiterOptions { PermitLimit = attempts, Window = TimeSpan.FromMinutes(1) });
    });
});
builder.Services.AddSingleton(_ => new CandleStore(CandleStore.DefaultCapacity));
builder.Services.AddSingleton<ChartHistory>();
switch (builder.Configuration.GetValue<string>($"{PriceFeedOptions.SectionName}:Provider"))
{
    case PriceFeedOptions.TiingoProvider:
        builder.Services.AddHttpClient(TiingoPriceFeed.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        builder.Services.AddSingleton<IPriceFeed, TiingoPriceFeed>();
        break;
    case PriceFeedOptions.CapitalComProvider:
        builder.Services.AddHttpClient(CapitalComPriceFeed.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
        builder.Services.AddSingleton<IPriceFeed, CapitalComPriceFeed>();
        break;
    default:
        builder.Services.AddSingleton<IPriceFeed, SyntheticPriceFeed>();
        break;
}
builder.Services.AddSingleton<SubscriptionRegistry>();

builder.Services.AddSingleton<EngineHost>();
if (!isOpenApiGeneration)
{
    // Start order matters: the firms are loaded first, and the engine loop must run before anything sends to it.
    builder.Services.AddHostedService<TenantSeeder>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<EngineHost>());
    builder.Services.AddHostedService<AccountSeeder>();
    builder.Services.AddHostedService<PriceFeedPump>();
    builder.Services.AddHostedService<ChartRecorder>();
    builder.Services.AddHostedService<RealtimePublisher>();
}

builder.Services.ConfigureHttpJsonOptions(o => EngineJson.Configure(o.SerializerOptions));
builder.Services.AddSignalR().AddJsonProtocol(o => EngineJson.Configure(o.PayloadSerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<EngineHealthCheck>("engine").AddCheck<TenantsHealthCheck>("tenants");

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// Not yet hardened for production: HTTPS, secrets management and a production feed are missing.
// The OpenAPI generator never serves requests, so it is allowed.
if (!app.Environment.IsDevelopment() && !isOpenApiGeneration)
{
    throw new InvalidOperationException("Trading.Service is not yet ready for production and may only run in the Development environment.");
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapAuthApi();
app.MapTradingApi();
app.MapSettingsApi();
app.MapAdminApi();
app.MapPartnerApi();
app.MapHub<TradingHub>("/hubs/trading").RequireAuthorization();

app.Run();
