using Microsoft.Extensions.Logging.Abstractions;

using Trading.Engine;
using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Service.Engine;

namespace Trading.Service.Tests;

public sealed class EngineHostTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TimestampsNeverGoBackwardsEvenIfTheClockDoes()
    {
        var clock = new SettableTimeProvider(Start);
        await using var host = await StartedHostAsync(clock);

        await host.Value.SendAsync(t => new CreateAccount(t, "A", "standard", 1_000m), TestContext.Current.CancellationToken);
        clock.Now = Start.AddSeconds(-30);
        var events = await host.Value.SendAsync(t => new CreateAccount(t, "B", "standard", 1_000m), TestContext.Current.CancellationToken);

        var created = Assert.IsType<AccountCreated>(Assert.Single(events).Event);
        Assert.Equal(Start, created.Timestamp);
    }

    [Fact]
    public async Task EventsAreNumberedAndPublishedInOrder()
    {
        await using var host = await StartedHostAsync(new SettableTimeProvider(Start));

        var first = await host.Value.SendAsync(t => new CreateAccount(t, "A", "standard", 1_000m), TestContext.Current.CancellationToken);
        var second = await host.Value.SendAsync(t => new CreateAccount(t, "B", "standard", 1_000m), TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(first).Sequence);
        Assert.Equal(2, Assert.Single(second).Sequence);
        Assert.True(host.Events.Published.TryRead(out var published));
        Assert.Equal(1, published.Sequence);
    }

    [Fact]
    public async Task QueriesSeeEveryInputSentBeforeThem()
    {
        await using var host = await StartedHostAsync(new SettableTimeProvider(Start));
        host.Value.EnqueueQuote("EURUSD", 1.08000m, 1.08010m);

        var prices = await host.Value.QueryAsync(e => e.GetPrices("standard"), TestContext.Current.CancellationToken);

        Assert.Equal(1.08000m, Assert.Single(prices!).Bid);
        Assert.Equal(1, host.Value.QuotesApplied);
    }

    private static async Task<RunningHost> StartedHostAsync(TimeProvider clock)
    {
        var configuration = new EngineConfiguration(
            [new Instrument("EURUSD", "EUR", "USD", 100_000m, 5, 0.01m, 0.01m, 100m)],
            [new TradingGroup("standard", "USD", 50m, [new SymbolConditions("EURUSD", 100, 0, 0m)])],
            TimeSpan.FromSeconds(5));
        var events = new EventLog();
        var host = new EngineHost(configuration, events, clock, NullLogger<EngineHost>.Instance);
        await host.StartAsync(TestContext.Current.CancellationToken);
        return new RunningHost(host, events);
    }

    private sealed class RunningHost(EngineHost value, EventLog events) : IAsyncDisposable
    {
        public EngineHost Value { get; } = value;

        public EventLog Events { get; } = events;

        public async ValueTask DisposeAsync()
        {
            await Value.StopAsync(CancellationToken.None);
            Value.Dispose();
        }
    }

    private sealed class SettableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
