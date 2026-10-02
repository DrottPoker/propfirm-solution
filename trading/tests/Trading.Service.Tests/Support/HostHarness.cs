using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Trading.Engine;
using Trading.Service.Engine;
using Trading.Service.Persistence;

namespace Trading.Service.Tests.Support;

/// <summary>Runs the engine loop directly against a journal in memory, without the web host.</summary>
internal sealed class HostHarness : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    private HostHarness(EngineHost host, InMemoryJournal journal, EventLog events, FakeLifetime lifetime)
    {
        Host = host;
        Journal = journal;
        Events = events;
        Lifetime = lifetime;
    }

    public EngineHost Host { get; }

    public InMemoryJournal Journal { get; }

    public EventLog Events { get; }

    public FakeLifetime Lifetime { get; }

    public static EngineConfiguration Configuration { get; } = new(
        [new Instrument("EURUSD", "EUR", "USD", 100_000m, 5, 0.01m, 0.01m, 100m)],
        [new TradingGroup("standard", "USD", 50m, [new SymbolConditions("EURUSD", 100, 0, 0m)])],
        TimeSpan.FromSeconds(5));

    public static async Task<HostHarness> StartAsync(InMemoryJournal? journal = null, TimeProvider? clock = null, int snapshotInterval = 10_000)
    {
        var events = new EventLog();
        var lifetime = new FakeLifetime();
        journal ??= new InMemoryJournal();
        var host = new EngineHost(
            Configuration,
            journal,
            events,
            clock ?? new SettableTimeProvider(Start),
            Options.Create(new JournalOptions { SnapshotInterval = snapshotInterval }),
            lifetime,
            NullLogger<EngineHost>.Instance);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.Ready.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        return new HostHarness(host, journal, events, lifetime);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.StopAsync(CancellationToken.None);
        Host.Dispose();
    }
}

internal sealed class FakeLifetime : IHostApplicationLifetime
{
    private int _stopRequested;

    public bool StopRequested => Volatile.Read(ref _stopRequested) == 1;

    public CancellationToken ApplicationStarted => CancellationToken.None;

    public CancellationToken ApplicationStopping => CancellationToken.None;

    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void StopApplication() => Interlocked.Exchange(ref _stopRequested, 1);
}

internal sealed class SettableTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
