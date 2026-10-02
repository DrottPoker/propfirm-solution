using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Trading.Service.Engine;
using Trading.Service.Feeds;
using Trading.Service.Persistence;

namespace Trading.Service.Tests.Support;

/// <summary>
/// The real service in memory, with prices pushed by the test, a clock that only moves when told and a
/// journal in memory. Pass the same journal to a second factory to restart the service.
/// With a Postgres connection string, the real journal in that database is used instead.
/// </summary>
internal sealed class ServiceFactory(
    InMemoryJournal? journal = null,
    IReadOnlyDictionary<string, string>? settings = null,
    string? postgresConnectionString = null)
    : WebApplicationFactory<Program>
{
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));

    public ManualPriceFeed Feed { get; } = new();

    public InMemoryJournal Journal { get; } = journal ?? new InMemoryJournal();

    public EngineHost Engine => Services.GetRequiredService<EngineHost>();

    /// <summary>Pushes a raw price and waits until the engine has applied it.</summary>
    public async Task PushQuoteAsync(string symbol, decimal bid, decimal ask)
    {
        var target = Engine.QuotesApplied + 1;
        Feed.Push(symbol, bid, ask);
        await Eventually.ThatAsync(() => Engine.QuotesApplied >= target, "the engine to apply the price");
    }

    public HubConnection CreateHubConnection() =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "/hubs/trading"), options =>
            {
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<IPriceFeed>(Feed);
            if (postgresConnectionString is null)
            {
                services.AddSingleton<IEngineJournal>(Journal);
            }
        });
    }
}
