using Trading.Engine.Inputs;
using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>Nothing that depends on an input is released before the input is stored.</summary>
public sealed class DurabilityTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task CommandResultAndEventsWaitUntilStored()
    {
        await using var harness = await HostHarness.StartAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Journal.Gate = gate;

        var command = harness.Host.SendAsync(t => new CreateAccount(t, "A", "standard", 1_000m), TestContext.Current.CancellationToken);
        await Task.Delay(Settle, TestContext.Current.CancellationToken);

        Assert.False(command.IsCompleted);
        Assert.False(harness.Events.Published.TryRead(out _));
        Assert.Equal(0, harness.Journal.InputCount);

        gate.SetResult();
        await Eventually.Within(command);

        Assert.Equal(1, harness.Journal.InputCount);
        Assert.True(harness.Events.Published.TryRead(out _));
    }

    [Fact]
    public async Task QueriesWaitForTheInputsTheySaw()
    {
        await using var harness = await HostHarness.StartAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Journal.Gate = gate;

        var command = harness.Host.SendAsync(t => new CreateAccount(t, "A", "standard", 1_000m), TestContext.Current.CancellationToken);
        var query = harness.Host.QueryAsync(e => e.GetAccount("A"), TestContext.Current.CancellationToken);
        await Task.Delay(Settle, TestContext.Current.CancellationToken);

        Assert.False(query.IsCompleted);

        gate.SetResult();
        Assert.NotNull(await Eventually.Within(query));
        await command;
    }

    [Fact]
    public async Task InputsArriveInBatchesWhileTheJournalIsBusy()
    {
        await using var harness = await HostHarness.StartAsync();
        var appendsBefore = harness.Journal.Appends;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Journal.Gate = gate;

        var commands = Enumerable.Range(0, 50)
            .Select(i => harness.Host.SendAsync(t => new CreateAccount(t, $"A{i}", "standard", 1_000m), TestContext.Current.CancellationToken))
            .ToList();
        await Task.Delay(Settle, TestContext.Current.CancellationToken);
        gate.SetResult();
        await Eventually.Within(Task.WhenAll(commands).ContinueWith(_ => 0, TaskScheduler.Default));

        Assert.Equal(50, harness.Journal.InputCount);
        Assert.True(harness.Journal.Appends - appendsBefore < 50, "Inputs that arrive during a write should share the next write.");
    }

    [Fact]
    public async Task JournalFailureFailsTheCommandAndStopsTheService()
    {
        await using var harness = await HostHarness.StartAsync();
        harness.Journal.Failure = new InvalidOperationException("Disk full");

        var command = harness.Host.SendAsync(t => new CreateAccount(t, "A", "standard", 1_000m), TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() => Eventually.Within(command));
        Assert.True(harness.Host.JournalFailed);
        Assert.True(harness.Lifetime.StopRequested);
        await Assert.ThrowsAnyAsync<Exception>(
            () => Eventually.Within(harness.Host.SendAsync(t => new CreateAccount(t, "B", "standard", 1_000m), TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task SnapshotsAreTakenAtTheIntervalAndOnStop()
    {
        var journal = new InMemoryJournal();
        await using (var harness = await HostHarness.StartAsync(journal, snapshotInterval: 3))
        {
            for (var i = 0; i < 4; i++)
            {
                await harness.Host.SendAsync(t => new CreateAccount(t, $"A{i}", "standard", 1_000m), TestContext.Current.CancellationToken);
            }

            Assert.Equal(3, journal.LastSnapshotInputSequence);
        }

        Assert.Equal(4, journal.LastSnapshotInputSequence);
    }
}
