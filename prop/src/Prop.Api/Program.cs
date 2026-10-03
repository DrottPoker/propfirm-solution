using System.Reflection;
using System.Threading.RateLimiting;

using Common.Postgres;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Npgsql;

using Prop.Api.Api;
using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Firms;
using Prop.Api.Json;
using Prop.Api.Persistence;
using Prop.Api.Portal;
using Prop.Api.Trading;

// The build-time OpenAPI generator loads the app only to read its endpoints.
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

var builder = WebApplication.CreateBuilder(args);

var tradingPlatform = builder.Services.AddOptions<TradingPlatformOptions>()
    .Bind(builder.Configuration.GetSection(TradingPlatformOptions.SectionName))
    .Validate(
        o => o.Url is { IsAbsoluteUri: true } url && url.AbsolutePath.EndsWith('/'),
        "TradingPlatform:Url must be the trading service's absolute address, ending with /.")
    .Validate(o => o.EventWaitSeconds is >= 0 and <= 30 && o.EventsPerRequest is >= 1 and <= 1_000, "TradingPlatform event settings are out of range.");
if (!isOpenApiGeneration)
{
    tradingPlatform.ValidateOnStart();
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new FirmCatalog(
    sp.GetRequiredService<IConfiguration>().GetSection(FirmOptions.SectionName).Get<List<FirmOptions>>() ?? []));

// Resolved lazily, so tools that only load the app (like the OpenAPI generator) need no database.
builder.Services.AddSingleton(sp => NpgsqlDataSource.Create(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Prop")
    ?? throw new InvalidOperationException("The connection string Prop is missing.")));
builder.Services.AddSingleton(PropMigrations.All);
builder.Services.AddSingleton<DatabaseSchema>();
builder.Services.AddSingleton<WorkSignals>();
builder.Services.AddSingleton<ChallengeCatalog>();
builder.Services.AddSingleton<ChallengeService>();
builder.Services.AddSingleton<ChallengeQueries>();
builder.Services.AddSingleton<PayoutQueries>();
builder.Services.AddSingleton<PortalUsers>();
builder.Services.AddSingleton<IPasswordHasher<PortalUser>, PasswordHasher<PortalUser>>();

// Portal sessions survive restarts and work across instances, since the keys that protect them are in the database.
builder.Services.AddSingleton<IXmlRepository, PostgresXmlRepository>();
builder.Services.AddDataProtection().SetApplicationName("prop-api");
if (!isOpenApiGeneration)
{
    builder.Services.AddOptions<KeyManagementOptions>().Configure<IXmlRepository>((options, repository) => options.XmlRepository = repository);
}

builder.Services.AddOptions<LoginOptions>()
    .Bind(builder.Configuration.GetSection(LoginOptions.SectionName))
    .Validate(
        o => o.MinimumPasswordLength >= 1 && o.AttemptsPerMinute >= 0 && o.SessionLifetime > TimeSpan.Zero,
        "Login needs a password length of at least 1, attempts per minute of 0 or more and a positive session lifetime.")
    .ValidateOnStart();
builder.Services.AddPortalAuth();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(PortalAuth.LoginRateLimit, context =>
    {
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var attempts = context.RequestServices.GetRequiredService<IOptions<LoginOptions>>().Value.AttemptsPerMinute;
        return attempts == 0
            ? RateLimitPartition.GetNoLimiter(address)
            : RateLimitPartition.GetFixedWindowLimiter(address, _ => new FixedWindowRateLimiterOptions { PermitLimit = attempts, Window = TimeSpan.FromMinutes(1) });
    });
});

// Long enough for the trading platform's longest wait for new events.
builder.Services.AddHttpClient(TradingPlatformClient.HttpClientName, (sp, client) =>
{
    client.BaseAddress = sp.GetRequiredService<IOptions<TradingPlatformOptions>>().Value.Url;
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddSingleton<ITradingPlatform, TradingPlatformClient>();
builder.Services.AddHttpClient(WebhookWorker.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));

if (!isOpenApiGeneration)
{
    // The seeder runs first, so the firms' challenges exist before anything else starts.
    builder.Services.AddHostedService<ChallengeSeeder>();
    builder.Services.AddHostedService<PortalSeeder>();
    builder.Services.AddHostedService<TradingEventConsumer>();
    builder.Services.AddHostedService<TradingCommandWorker>();
    builder.Services.AddHostedService<TradingDayScheduler>();
    builder.Services.AddHostedService<WebhookWorker>();
}

builder.Services.ConfigureHttpJsonOptions(o => PropJson.Configure(o.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Not yet hardened for production: HTTPS, secrets management and firm sign-up are missing.
// The OpenAPI generator never serves requests, so it is allowed.
if (!app.Environment.IsDevelopment() && !isOpenApiGeneration)
{
    throw new InvalidOperationException("Prop.Api is not yet ready for production and may only run in the Development environment.");
}

// Browsers reach the portal API through the portal, and in production through a proxy in front of it that
// adds their address and scheme. The login limit counts each browser, and the session cookie is secure behind
// HTTPS. Only loopback proxies are trusted until production networks are configured.
app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapFirmApi();
app.MapPortalApi();

app.Run();
