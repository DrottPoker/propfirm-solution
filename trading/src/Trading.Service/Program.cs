using System.Reflection;
using System.Threading.RateLimiting;

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

builder.Services.AddOptions<TradingOptions>().Bind(builder.Configuration.GetSection(TradingOptions.SectionName));
builder.Services.AddOptions<SyntheticFeedOptions>().Bind(builder.Configuration.GetSection(SyntheticFeedOptions.SectionName));
builder.Services.AddOptions<RealtimeOptions>().Bind(builder.Configuration.GetSection(RealtimeOptions.SectionName));
builder.Services.AddOptions<JournalOptions>().Bind(builder.Configuration.GetSection(JournalOptions.SectionName));
builder.Services.AddOptions<PriceFeedOptions>()
    .Bind(builder.Configuration.GetSection(PriceFeedOptions.SectionName))
    .Validate(
        o => o.Provider is PriceFeedOptions.SyntheticProvider or PriceFeedOptions.TiingoProvider,
        "PriceFeed:Provider must be Synthetic or Tiingo.")
    .Validate(
        o => o.Provider != PriceFeedOptions.TiingoProvider || o.Tiingo.ApiKey.Length > 0,
        "PriceFeed:Tiingo:ApiKey is required. Set it with dotnet user-secrets, never in a file.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<TradingOptions>>().Value.ToEngineConfiguration());
builder.Services.AddSingleton<MarketCatalog>();
builder.Services.AddSingleton<EventLog>();

// Resolved lazily, so tools that only load the app (like the OpenAPI generator) need no database.
builder.Services.AddSingleton(sp => NpgsqlDataSource.Create(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Trading")
    ?? throw new InvalidOperationException("The connection string Trading is missing.")));
builder.Services.AddSingleton<IEngineJournal, PostgresEngineJournal>();
builder.Services.AddSingleton<IUserStore, PostgresUserStore>();
builder.Services.AddSingleton<IXmlRepository, PostgresXmlRepository>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton(sp => new TenantCatalog(
    sp.GetRequiredService<IConfiguration>().GetSection(TenantOptions.SectionName).Get<List<TenantOptions>>() ?? [],
    sp.GetRequiredService<EngineConfiguration>()));

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
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
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
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthEndpoints.LoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddSingleton(_ => new CandleStore(CandleStore.DefaultCapacity));
if (builder.Configuration.GetValue<string>($"{PriceFeedOptions.SectionName}:Provider") == PriceFeedOptions.TiingoProvider)
{
    builder.Services.AddHttpClient(TiingoPriceFeed.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
    builder.Services.AddSingleton<IPriceFeed, TiingoPriceFeed>();
}
else
{
    builder.Services.AddSingleton<IPriceFeed, SyntheticPriceFeed>();
}
builder.Services.AddSingleton<SubscriptionRegistry>();

builder.Services.AddSingleton<EngineHost>();
if (!isOpenApiGeneration)
{
    // Start order matters: the engine loop must run before anything sends to it.
    builder.Services.AddHostedService(sp => sp.GetRequiredService<EngineHost>());
    builder.Services.AddHostedService<AccountSeeder>();
    builder.Services.AddHostedService<PriceFeedPump>();
    builder.Services.AddHostedService<RealtimePublisher>();
}

builder.Services.ConfigureHttpJsonOptions(o => EngineJson.Configure(o.SerializerOptions));
builder.Services.AddSignalR().AddJsonProtocol(o => EngineJson.Configure(o.PayloadSerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<EngineHealthCheck>("engine");

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
app.MapAdminApi();
app.MapHub<TradingHub>("/hubs/trading").RequireAuthorization();

app.Run();
