using Common.Postgres;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Npgsql;

using Trading.Engine;
using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Service.Candles;
using Trading.Service.Engine;
using Trading.Service.Identity;
using Trading.Service.Persistence;
using Trading.Service.Staff;
using Trading.Service.Tenancy;
using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

/// <summary>What the staff panel keeps and reads in Postgres (ADR 0057). Every test starts from an empty database.</summary>
public sealed class PostgresStaffPanelTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // Whole microseconds, which is what Postgres stores.
    private static readonly DateTimeOffset T = new(2026, 10, 5, 10, 0, 0, 123, 456, TimeSpan.Zero);

    [Fact]
    public async Task StaffAreFoundByEmailWithoutCaseAndGetANewPassword()
    {
        await using var dataSource = await CreateDatabaseAsync();
        var staff = new PostgresStaffStore(dataSource, Schema(dataSource));

        await staff.SaveAsync(" Ops@Test.Com ", "hash-1", T, TestContext.Current.CancellationToken);
        var saved = await staff.FindByEmailAsync("ops@test.com", TestContext.Current.CancellationToken);
        await staff.SaveAsync("OPS@TEST.COM", "hash-2", T, TestContext.Current.CancellationToken);

        Assert.NotNull(saved);
        Assert.Equal(("Ops@Test.Com", "hash-1"), (saved.Email, saved.PasswordHash));
        Assert.Equal(saved with { PasswordHash = "hash-2" }, await staff.FindByIdAsync(saved.Id, TestContext.Current.CancellationToken));
        Assert.Null(await staff.FindByEmailAsync("other@test.com", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ThePlatformsLogComesBackNewestFirstForTheWholePlatformOrOneServer()
    {
        await using var dataSource = await CreateDatabaseAsync();
        var log = new PostgresPlatformLog(dataSource, Schema(dataSource));

        await log.AddAsync(T, PlatformEventKind.ServiceStarted, null, null, new Dictionary<string, string> { ["replayed"] = "12" }, TestContext.Current.CancellationToken);
        await log.AddAsync(T.AddMinutes(1), PlatformEventKind.ServerCreated, "acme", null, new Dictionary<string, string> { ["partner"] = "Kronant Prop" }, TestContext.Current.CancellationToken);
        await log.AddAsync(T.AddMinutes(2), PlatformEventKind.AdminKeyReplaced, "acme", "ops@test.com", new Dictionary<string, string> { ["reason"] = "Leaked" }, TestContext.Current.CancellationToken);

        // A kind from a later version is left out rather than failing the log.
        await using (var future = dataSource.CreateCommand("insert into platform_log (at, kind, detail) values ($1, 'SomethingNew', '{}')"))
        {
            future.Parameters.AddWithValue(T.AddMinutes(3));
            await future.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var all = await log.LatestAsync(null, 10, TestContext.Current.CancellationToken);
        Assert.Equal([PlatformEventKind.AdminKeyReplaced, PlatformEventKind.ServerCreated, PlatformEventKind.ServiceStarted], all.Select(e => e.Kind));
        Assert.Equal((T.AddMinutes(2), "acme", "ops@test.com", "Leaked"), (all[0].At, all[0].ServerId, all[0].StaffEmail, all[0].Detail["reason"]));
        Assert.Equal("12", all[2].Detail["replayed"]);
        Assert.Equal([PlatformEventKind.AdminKeyReplaced], (await log.LatestAsync("acme", 1, TestContext.Current.CancellationToken)).Select(e => e.Kind));
        Assert.Equal(2, (await log.LatestAsync("acme", 10, TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task AServerStaffMadeKeepsWhoMadeItAndIsNeverTakenOverByTheConfiguration()
    {
        await using var dataSource = await CreateDatabaseAsync();
        var tenants = new PostgresTenantStore(dataSource, Schema(dataSource));
        var helix = new Tenant("helix", "Helix Markets", ["helix-standard"], [1, 2, 3], null, Listed: false, CreatedBy: "ops@test.com");

        Assert.True(await tenants.CreateAsync(helix, T, TestContext.Current.CancellationToken));
        var saved = Assert.Single(await tenants.ListAsync(TestContext.Current.CancellationToken));

        Assert.Equal((T, "ops@test.com", false), (saved.CreatedAt, saved.CreatedBy, saved.IsConfigured));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenants.SaveConfiguredAsync(helix with { CreatedBy = null, Name = "Taken" }, T, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChartGapsAreSavedAgainAsTheyAreFilledAndTheHistoryKnowsWhenItWasLoaded()
    {
        await using var dataSource = await CreateDatabaseAsync();
        var charts = new PostgresChartStore(dataSource, Schema(dataSource));
        var first = new ChartGap(Guid.CreateVersion7(T), "Real", T.AddMinutes(-10), T.AddMinutes(-5), T, ChartGapState.Filling, 1, null, 0, null);
        var second = first with { Id = Guid.CreateVersion7(T.AddMinutes(1)), FoundAt = T.AddMinutes(1) };

        await charts.SaveGapAsync(first, TestContext.Current.CancellationToken);
        await charts.SaveGapAsync(second, TestContext.Current.CancellationToken);
        await charts.SaveGapAsync(first with { State = ChartGapState.NotFilled, Tries = 4, FinishedAt = T.AddSeconds(42), Problem = "Too many requests" }, TestContext.Current.CancellationToken);
        await charts.SaveGapAsync(second with { Feed = "Other" }, TestContext.Current.CancellationToken);

        var gaps = await charts.ListGapsAsync("Real", 10, TestContext.Current.CancellationToken);
        Assert.Equal([second.Id, first.Id], gaps.Select(g => g.Id));
        Assert.Equal(first with { State = ChartGapState.NotFilled, Tries = 4, FinishedAt = T.AddSeconds(42), Problem = "Too many requests" }, gaps[1]);
        Assert.Empty(await charts.ListGapsAsync("Other", 10, TestContext.Current.CancellationToken));

        Assert.Null(await charts.GetHistoryInfoAsync("Real", TestContext.Current.CancellationToken));
        await charts.ReplaceHistoryAsync("Real", T.AddDays(-180), T, [], TestContext.Current.CancellationToken);
        Assert.Equal(new ChartHistoryInfo(T, T.AddDays(-180)), await charts.GetHistoryInfoAsync("Real", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheJournalGivesAGroupsLatestEventsItsActivityAndItsSize()
    {
        await using var dataSource = await CreateDatabaseAsync();
        var journal = new PostgresEngineJournal(dataSource, Schema(dataSource), Options.Create(new JournalOptions()));
        await journal.InitializeAsync(TestContext.Current.CancellationToken);
        var order = new PlaceOrder(T, "A2", "O1", "EURUSD", Side.Buy, OrderType.Market, 1m, null, null, null);
        JournaledEvent[] events =
        [
            new(new EventEnvelope(1, new PositionOpened(T.AddDays(-1), "A1", "P1", "EURUSD", Side.Buy, 1m, 1.08m, null, null, 0m, 100_000m)), "g1"),
            new(new EventEnvelope(2, new PositionOpened(T, "A1", "P2", "EURUSD", Side.Buy, 1m, 1.08m, null, null, 0m, 100_000m)), "g1"),
            new(new EventEnvelope(3, new InputRejected(T, order, RejectReason.StalePrice)), "g1"),
            new(new EventEnvelope(4, new InputRejected(T, order, RejectReason.InsufficientMargin)), "g1"),
            new(new EventEnvelope(5, new EquityFloorRemoved(T, "A3", "daily")), "g2"),
        ];
        await journal.AppendAsync(new JournalBatch([], events, new JournalSnapshot(0, 5, "fingerprint", new TradingEngine(HostHarness.Configuration).ExportState())), TestContext.Current.CancellationToken);

        var latest = await journal.ReadLatestGroupEventsAsync(["g1"], null, 3, TestContext.Current.CancellationToken);
        Assert.Equal([4L, 3, 2], latest.Select(e => e.Sequence));
        Assert.Equal([2L, 1], (await journal.ReadLatestGroupEventsAsync(["g1", "g2"], "A1", 10, TestContext.Current.CancellationToken)).Select(e => e.Sequence));

        var activity = await journal.CountGroupActivityAsync(T.AddHours(-1), TestContext.Current.CancellationToken);
        Assert.Equal(new GroupActivity(1, 2, 1), activity["g1"]);
        Assert.False(activity.ContainsKey("g2"));

        var stats = await journal.GetStatsAsync(TestContext.Current.CancellationToken);
        Assert.True(stats.SizeBytes > 0);
        var snapshot = Assert.Single(stats.Snapshots);
        Assert.Equal(0, snapshot.InputSequence);
        Assert.NotNull(snapshot.CreatedAt);
    }

    [Fact]
    public async Task TradersAreCountedPerFirmWithTheNewOnes()
    {
        await using var dataSource = await CreateDatabaseAsync();
        var users = new PostgresUserStore(dataSource, Schema(dataSource));
        await users.CreateAsync("firm-a", "a@test.example", "hash", TestContext.Current.CancellationToken);
        await users.CreateAsync("firm-a", "b@test.example", "hash", TestContext.Current.CancellationToken);
        await users.CreateAsync("firm-b", "c@test.example", "hash", TestContext.Current.CancellationToken);
        await using (var old = dataSource.CreateCommand("update users set created_at = now() - interval '30 days' where normalized_email = 'A@TEST.EXAMPLE'"))
        {
            await old.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var counts = await users.CountTradersAsync(DateTimeOffset.UtcNow.AddDays(-7), TestContext.Current.CancellationToken);

        Assert.Equal(new TraderCount(2, 1), counts["firm-a"]);
        Assert.Equal(new TraderCount(1, 1), counts["firm-b"]);
    }

    private static DatabaseSchema Schema(NpgsqlDataSource dataSource) => new(dataSource, TradingMigrations.All, NullLogger<DatabaseSchema>.Instance);

    private async Task<NpgsqlDataSource> CreateDatabaseAsync() => NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
}
