using System.Reflection;

using Microsoft.Extensions.Options;

using Npgsql;

using Trading.Service.Api;
using Trading.Service.Candles;
using Trading.Service.Configuration;
using Trading.Service.Engine;
using Trading.Service.Feeds;
using Trading.Service.Json;
using Trading.Service.Persistence;
using Trading.Service.Realtime;

// The build-time OpenAPI generator loads the app only to read its endpoints.
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<TradingOptions>().Bind(builder.Configuration.GetSection(TradingOptions.SectionName));
builder.Services.AddOptions<SyntheticFeedOptions>().Bind(builder.Configuration.GetSection(SyntheticFeedOptions.SectionName));
builder.Services.AddOptions<RealtimeOptions>().Bind(builder.Configuration.GetSection(RealtimeOptions.SectionName));
builder.Services.AddOptions<JournalOptions>().Bind(builder.Configuration.GetSection(JournalOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<TradingOptions>>().Value.ToEngineConfiguration());
builder.Services.AddSingleton<MarketCatalog>();
builder.Services.AddSingleton<EventLog>();

// Resolved lazily, so tools that only load the app (like the OpenAPI generator) need no database.
builder.Services.AddSingleton(sp => NpgsqlDataSource.Create(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Trading")
    ?? throw new InvalidOperationException("The connection string Trading is missing.")));
builder.Services.AddSingleton<IEngineJournal, PostgresEngineJournal>();
builder.Services.AddSingleton(_ => new CandleStore(CandleStore.DefaultCapacity));
builder.Services.AddSingleton<IPriceFeed, SyntheticPriceFeed>();
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

// There is no authentication yet, so the API is open to anyone who can reach it.
// The OpenAPI generator never serves requests, so it is allowed.
if (!app.Environment.IsDevelopment() && !isOpenApiGeneration)
{
    throw new InvalidOperationException("Trading.Service has no authentication yet and may only run in the Development environment.");
}

app.UseCors();
app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapTradingApi();
app.MapAdminApi();
app.MapHub<TradingHub>("/hubs/trading");

app.Run();
