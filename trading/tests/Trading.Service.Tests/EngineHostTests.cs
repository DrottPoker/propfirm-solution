using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

public sealed class EngineHostTests
{
    [Fact]
    public async Task TimestampsNeverGoBackwardsEvenIfTheClockDoes()
    {
        var clock = new SettableTimeProvider(HostHarness.Start);
        await using var harness = await HostHarness.StartAsync(clock: clock);

        await harness.Host.SendAsync(t => new CreateAccount(t, "A", "standard", 1_000m), TestContext.Current.CancellationToken);
        clock.Now = HostHarness.Start.AddSeconds(-30);
        var events = await harness.Host.SendAsync(t => new CreateAccount(t, "B", "standard", 1_000m), TestContext.Current.CancellationToken);

        var created = Assert.IsType<AccountCreated>(Assert.Single(events).Event);
        Assert.Equal(HostHarness.Start, created.Timestamp);
    }

    // The journal stores microseconds, so the engine must never see finer times than a replay would.
    [Fact]
    public async Task TimestampsAreCutToWholeMicroseconds()
    {
        var clock = new SettableTimeProvider(HostHarness.Start.AddTicks(1_234_567));
        await using var harness = await HostHarness.StartAsync(clock: clock);

        var events = await harness.Host.SendAsync(t => new CreateAccount(t, "A", "standard", 1_000m), TestContext.Current.CancellationToken);

        Assert.Equal(HostHarness.Start.AddTicks(1_234_560), Assert.Single(events).Event.Timestamp);
    }

    [Fact]
    public async Task EventsAreNumberedAndPublishedInOrder()
    {
        await using var harness = await HostHarness.StartAsync();

        var first = await harness.Host.SendAsync(t => new CreateAccount(t, "A", "standard", 1_000m), TestContext.Current.CancellationToken);
        var second = await harness.Host.SendAsync(t => new CreateAccount(t, "B", "standard", 1_000m), TestContext.Current.CancellationToken);

        Assert.Equal(1, Assert.Single(first).Sequence);
        Assert.Equal(2, Assert.Single(second).Sequence);
        Assert.True(harness.Events.Published.TryRead(out var published));
        Assert.Equal(1, published.Sequence);
    }

    [Fact]
    public async Task QueriesSeeEveryInputSentBeforeThem()
    {
        await using var harness = await HostHarness.StartAsync();
        harness.Host.EnqueueQuote("EURUSD", 1.08000m, 1.08010m);

        var prices = await harness.Host.QueryAsync(e => e.GetPrices("standard"), TestContext.Current.CancellationToken);

        Assert.Equal(1.08000m, Assert.Single(prices!).Bid);
        Assert.Equal(1, harness.Host.QuotesApplied);
    }
}
