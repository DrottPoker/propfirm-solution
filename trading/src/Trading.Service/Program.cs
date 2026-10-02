using Microsoft.Extensions.Options;

using Trading.Service.Api;
using Trading.Service.Candles;
using Trading.Service.Configuration;
using Trading.Service.Engine;
using Trading.Service.Feeds;
using Trading.Service.Json;
using Trading.Service.Realtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<TradingOptions>().Bind(builder.Configuration.GetSection(TradingOptions.SectionName));
builder.Services.AddOptions<SyntheticFeedOptions>().Bind(builder.Configuration.GetSection(SyntheticFeedOptions.SectionName));
builder.Services.AddOptions<RealtimeOptions>().Bind(builder.Configuration.GetSection(RealtimeOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<TradingOptions>>().Value.ToEngineConfiguration());
builder.Services.AddSingleton<MarketCatalog>();
builder.Services.AddSingleton<EventLog>();
builder.Services.AddSingleton(_ => new CandleStore(CandleStore.DefaultCapacity));
builder.Services.AddSingleton<IPriceFeed, SyntheticPriceFeed>();
builder.Services.AddSingleton<SubscriptionRegistry>();

// Start order matters: the engine loop must run before anything sends to it.
builder.Services.AddSingleton<EngineHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EngineHost>());
builder.Services.AddHostedService<AccountSeeder>();
builder.Services.AddHostedService<PriceFeedPump>();
builder.Services.AddHostedService<RealtimePublisher>();

builder.Services.ConfigureHttpJsonOptions(o => EngineJson.Configure(o.SerializerOptions));
builder.Services.AddSignalR().AddJsonProtocol(o => EngineJson.Configure(o.PayloadSerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// There is no authentication yet, so the API is open to anyone who can reach it.
if (!app.Environment.IsDevelopment())
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
