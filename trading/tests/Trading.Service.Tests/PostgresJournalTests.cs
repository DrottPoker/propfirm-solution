using System.Globalization;
using System.Text.Json;

using Common.Postgres;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Npgsql;

using Trading.Engine;
using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Service.Engine;
using Trading.Service.Json;
using Trading.Service.Persistence;
using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

public sealed class PostgresJournalTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // Whole microseconds, which is what Postgres stores.
    private static readonly DateTimeOffset T = new(2026, 10, 5, 10, 0, 0, 123, 456, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Json = EngineJson.CreateOptions();

    [Fact]
    public async Task MigrationsCanRunOnEveryStart()
    {
        await using var journal = await CreateJournalAsync();
        var restarted = CreateJournal(journal.DataSource);

        await restarted.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await restarted.GetLastInputSequenceAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InputsEventsAndSnapshotsComeBackExactly()
    {
        await using var journal = await CreateJournalAsync();
        var engine = new TradingEngine(HostHarness.Configuration);
        var inputs = new EngineInput[]
        {
            new CreateAccount(T, "A1", "standard", 100_000.00m),
            new Quote(T, "EURUSD", 1.08010m, 1.08020m),
            new SetEquityFloor(T, "A1", "max-loss", new TrailingFloor(5_000.00m, LockLevel: 100_000m)),
            new PlaceOrder(T, "A1", "O1", "EURUSD", Side.Buy, OrderType.Market, 1.50m, null, 1.07000m, null),
        };
        var events = inputs.SelectMany(engine.Apply).Select((e, i) => new EventEnvelope(i + 1, e)).ToList();
        var snapshot = new JournalSnapshot(4, events.Count, "fingerprint", engine.ExportState());

        await journal.Value.AppendAsync(
            new JournalBatch(
                inputs.Select((input, i) => new JournaledInput(i + 1, input) { Feed = input is Quote ? "Tiingo" : null }).ToList(),
                [.. events.Select(e => new JournaledEvent(e, "standard"))],
                snapshot),
            TestContext.Current.CancellationToken);

        var readInputs = await ToListAsync(journal.Value.ReadInputsAsync(0, TestContext.Current.CancellationToken));
        Assert.Equal(inputs, readInputs.Select(i => i.Input));
        Assert.Equal([1L, 2, 3, 4], readInputs.Select(i => i.Sequence));
        Assert.Equal([null, "Tiingo", null, null], readInputs.Select(i => i.Feed));
        var quote = Assert.IsType<Quote>(readInputs[1].Input);
        Assert.Equal("1.08010", quote.Bid.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(T, quote.Timestamp);

        var readEvents = await journal.Value.ReadEventsAsync("A1", 0, 100, TestContext.Current.CancellationToken);
        Assert.Equal(events.Select(ToJson), readEvents.Select(ToJson));

        var readSnapshot = await journal.Value.LoadLatestSnapshotAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(readSnapshot);
        Assert.Equal((4L, (long)events.Count, "fingerprint"), (readSnapshot.InputSequence, readSnapshot.EventSequence, readSnapshot.ConfigurationFingerprint));
        Assert.Equal(JsonSerializer.Serialize(snapshot.State, Json), JsonSerializer.Serialize(readSnapshot.State, Json));
        Assert.Equal(events.Count, await journal.Value.GetLastEventSequenceAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EventsAreReadPerAccountInOrderAndInPages()
    {
        await using var journal = await CreateJournalAsync();
        var events = Enumerable.Range(1, 6)
            .Select(i => new EventEnvelope(i, new EquityFloorRemoved(T, i % 2 == 0 ? "A" : "B", $"floor-{i}")))
            .ToList();
        await journal.Value.AppendAsync(new JournalBatch([], [.. events.Select(e => new JournaledEvent(e, null))], null), TestContext.Current.CancellationToken);

        var first = await journal.Value.ReadEventsAsync("A", 0, 2, TestContext.Current.CancellationToken);
        var rest = await journal.Value.ReadEventsAsync("A", first[^1].Sequence, 10, TestContext.Current.CancellationToken);

        Assert.Equal([2L, 4], first.Select(e => e.Sequence));
        Assert.Equal([6L], rest.Select(e => e.Sequence));
    }

    [Fact]
    public async Task LatestEventsAreReadPerAccountInOrderAndInPagesBackwards()
    {
        await using var journal = await CreateJournalAsync();
        var events = Enumerable.Range(1, 6)
            .Select(i => new EventEnvelope(i, new EquityFloorRemoved(T, i % 2 == 0 ? "A" : "B", $"floor-{i}")))
            .ToList();
        await journal.Value.AppendAsync(new JournalBatch([], [.. events.Select(e => new JournaledEvent(e, null))], null), TestContext.Current.CancellationToken);

        var latest = await journal.Value.ReadEventsBeforeAsync("A", long.MaxValue, 2, TestContext.Current.CancellationToken);
        var earlier = await journal.Value.ReadEventsBeforeAsync("A", latest[0].Sequence, 10, TestContext.Current.CancellationToken);

        Assert.Equal([4L, 6], latest.Select(e => e.Sequence));
        Assert.Equal(["floor-4", "floor-6"], latest.Select(e => Assert.IsType<EquityFloorRemoved>(e.Event).FloorId));
        Assert.Equal([2L], earlier.Select(e => e.Sequence));
    }

    [Fact]
    public async Task EventsAreReadPerGroupInOrderAndInPages()
    {
        await using var journal = await CreateJournalAsync();
        string?[] groups = ["g1", "g2", null, "g1", "g3", "g1"];
        var events = groups.Select((group, i) => new JournaledEvent(new EventEnvelope(i + 1, new EquityFloorRemoved(T, $"A{i}", "daily")), group)).ToList();
        await journal.Value.AppendAsync(new JournalBatch([], events, null), TestContext.Current.CancellationToken);

        var first = await journal.Value.ReadGroupEventsAsync(["g1"], 0, 2, TestContext.Current.CancellationToken);
        var rest = await journal.Value.ReadGroupEventsAsync(["g1"], first[^1].Sequence, 10, TestContext.Current.CancellationToken);
        var twoGroups = await journal.Value.ReadGroupEventsAsync(["g2", "g3"], 0, 10, TestContext.Current.CancellationToken);

        Assert.Equal([1L, 4], first.Select(e => e.Sequence));
        Assert.Equal([6L], rest.Select(e => e.Sequence));
        Assert.Equal([2L, 5], twoGroups.Select(e => e.Sequence));
    }

    // Events stored before groups were recorded get the group of their account when the database is upgraded.
    [Fact]
    public async Task UpgradeFillsInTheGroupOfEarlierEvents()
    {
        await using var journal = await CreateJournalAsync();
        await using (var downgrade = journal.DataSource.CreateBatch())
        {
            foreach (var sql in new[]
            {
                "drop table login_links",
                "alter table engine_events drop column group_id",
                "delete from schema_migrations where version = '0003_integration.sql'",
                "insert into engine_events (sequence, account_id, kind, occurred_at, payload) values (1, 'A1', 'AccountCreated', now(), '{\"groupId\": \"standard\"}')",
                "insert into engine_events (sequence, account_id, kind, occurred_at, payload) values (2, 'A1', 'EquityFloorRemoved', now(), '{\"kind\": \"EquityFloorRemoved\", \"timestamp\": \"2026-10-05T10:00:00+00:00\", \"accountId\": \"A1\", \"floorId\": \"daily\"}')",
            })
            {
                downgrade.BatchCommands.Add(new NpgsqlBatchCommand(sql));
            }

            await downgrade.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await CreateJournal(journal.DataSource).InitializeAsync(TestContext.Current.CancellationToken);

        var events = await journal.Value.ReadGroupEventsAsync(["standard"], 1, 10, TestContext.Current.CancellationToken);
        Assert.Equal([2L], events.Select(e => e.Sequence));
    }

    // The price behind a fill is the last one before the order's input, also when a later one has the same time (ADR 0053).
    [Fact]
    public async Task PositionEventsAndPricesAreFoundByInputAndTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var journal = await CreateJournalAsync();
        var engine = new TradingEngine(HostHarness.Configuration);
        var inputs = new EngineInput[]
        {
            new CreateAccount(T, "A1", "standard", 100_000.00m),
            new Quote(T, "EURUSD", 1.08010m, 1.08020m),
            new PlaceOrder(T.AddSeconds(1), "A1", "O1", "EURUSD", Side.Buy, OrderType.Market, 1.00m, null, null, null),
            new Quote(T.AddSeconds(1), "EURUSD", 1.09000m, 1.09010m),
            new Quote(T.AddSeconds(2), "GBPUSD", 1.25000m, 1.25010m),
        };
        var events = new List<JournaledEvent>();
        for (var i = 0; i < inputs.Length; i++)
        {
            // Only an account's events have its group, like in the service.
            events.AddRange(engine.Apply(inputs[i]).Select(e => new JournaledEvent(new EventEnvelope(events.Count + 1, e), EventLog.AccountIdOf(e) is null ? null : "standard") { InputSequence = i + 1 }));
        }

        await journal.Value.AppendAsync(
            new JournalBatch([.. inputs.Select((input, i) => new JournaledInput(i + 1, input) { Feed = input is Quote ? "Tiingo" : null })], events, null),
            cancellationToken);

        var position = await journal.Value.ReadPositionEventsAsync("A1", "O1", cancellationToken);
        var behind = await journal.Value.FindQuoteAsync("EURUSD", T.AddSeconds(1), position[0].InputSequence, cancellationToken);
        var latest = await journal.Value.FindQuoteAsync("EURUSD", T.AddSeconds(1), null, cancellationToken);
        var between = await ToListAsync(journal.Value.ReadQuotesBetweenAsync(["EURUSD"], T, T.AddSeconds(5), cancellationToken));
        var since = await journal.Value.ReadGroupEventsSinceAsync(["standard"], T, 0, 10, cancellationToken);

        Assert.Equal(["PositionOpened"], position.Select(e => e.Envelope.Event.GetType().Name));
        Assert.Equal(3L, position[0].InputSequence);
        Assert.Equal((2L, 1.08020m, "Tiingo"), (behind!.Sequence, behind.Quote.Ask, behind.Feed));
        Assert.Equal(4L, latest!.Sequence);
        Assert.Equal([2L, 4L], between.Select(q => q.Sequence));
        Assert.Equal(["PositionOpened"], since.Select(e => e.Envelope.Event.GetType().Name));
    }

    [Fact]
    public async Task OnlyTheLatestSnapshotsAreKept()
    {
        await using var journal = await CreateJournalAsync(snapshotsToKeep: 2);
        var state = new TradingEngine(HostHarness.Configuration).ExportState();

        foreach (var sequence in new long[] { 10, 20, 30 })
        {
            await journal.Value.AppendAsync(new JournalBatch([], [], new JournalSnapshot(sequence, 0, "f", state)), TestContext.Current.CancellationToken);
        }

        await using var command = journal.DataSource.CreateCommand("select input_sequence from engine_snapshots order by input_sequence");
        var kept = new List<long>();
        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                kept.Add(reader.GetInt64(0));
            }
        }

        Assert.Equal([20L, 30], kept);
    }

    [Fact]
    public async Task RecordedPricesAreReadFromAGivenTime()
    {
        await using var journal = await CreateJournalAsync();
        var inputs = new[]
        {
            new JournaledInput(1, new Quote(T, "EURUSD", 1.08000m, 1.08010m)) { Feed = "Tiingo" },
            new JournaledInput(2, new CreateAccount(T.AddMinutes(1), "A1", "standard", 1_000m)),
            new JournaledInput(3, new Quote(T.AddMinutes(2), "EURUSD", 1.08100m, 1.08110m)) { Feed = "Tiingo" },
        };
        await journal.Value.AppendAsync(new JournalBatch(inputs, [], null), TestContext.Current.CancellationToken);

        var quotes = await ToListAsync(journal.Value.ReadQuotesAsync(T.AddMinutes(1), "Tiingo", TestContext.Current.CancellationToken));

        Assert.Equal(1.08100m, Assert.Single(quotes).Bid);
    }

    [Fact]
    public async Task RecordedPricesAreReadFromOneFeed()
    {
        await using var journal = await CreateJournalAsync();
        var inputs = new[]
        {
            // Recorded before feeds were.
            new JournaledInput(1, new Quote(T, "EURUSD", 1.08000m, 1.08010m)),
            new JournaledInput(2, new Quote(T, "EURUSD", 1.08100m, 1.08110m)) { Feed = "Synthetic" },
            new JournaledInput(3, new Quote(T, "EURUSD", 1.08200m, 1.08210m)) { Feed = "Tiingo" },
        };
        await journal.Value.AppendAsync(new JournalBatch(inputs, [], null), TestContext.Current.CancellationToken);

        var quotes = await ToListAsync(journal.Value.ReadQuotesAsync(T, "Tiingo", TestContext.Current.CancellationToken));

        Assert.Equal(1.08200m, Assert.Single(quotes).Bid);
    }

    // It tells whether the service has switched feeds since it last ran.
    [Fact]
    public async Task FeedOfTheLastPriceIsKnown()
    {
        await using var journal = await CreateJournalAsync();
        var none = await journal.Value.GetLastQuoteFeedAsync(TestContext.Current.CancellationToken);
        await journal.Value.AppendAsync(
            new JournalBatch(
                [
                    new JournaledInput(1, new Quote(T, "EURUSD", 1.08000m, 1.08010m)) { Feed = "Synthetic" },
                    new JournaledInput(2, new Quote(T, "EURUSD", 1.08100m, 1.08110m)) { Feed = "Tiingo" },
                    new JournaledInput(3, new CreateAccount(T, "A1", "standard", 1_000m)),
                ],
                [],
                null),
            TestContext.Current.CancellationToken);
        var tiingo = await journal.Value.GetLastQuoteFeedAsync(TestContext.Current.CancellationToken);
        await journal.Value.AppendAsync(
            new JournalBatch([new JournaledInput(4, new Quote(T, "EURUSD", 1.08200m, 1.08210m))], [], null),
            TestContext.Current.CancellationToken);
        var unknown = await journal.Value.GetLastQuoteFeedAsync(TestContext.Current.CancellationToken);

        Assert.Equal<string?>([null, "Tiingo", null], [none, tiingo, unknown]);
    }

    [Fact]
    public async Task OnlyPricesHaveAFeed()
    {
        await using var journal = await CreateJournalAsync();
        await using var insert = journal.DataSource.CreateCommand(
            "insert into engine_inputs (sequence, recorded_at, kind, payload, feed) values (1, now(), 'CreateAccount', '{}', 'Tiingo')");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        Assert.Equal("engine_inputs_feed_only_on_quotes", exception.ConstraintName);
    }

    // One service writes to a journal. A second does not start, and a journal without the lock writes nothing.
    [Fact]
    public async Task OnlyOneServiceWritesToAJournal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var journal = await CreateJournalAsync();
        await using var second = CreateJournal(journal.DataSource);
        await journal.Value.TakeWriterLockAsync(TimeSpan.Zero, cancellationToken);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => second.TakeWriterLockAsync(TimeSpan.FromMilliseconds(300), cancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.AppendAsync(QuoteBatch(1), cancellationToken));
        await journal.Value.AppendAsync(QuoteBatch(1), cancellationToken);

        // Once the first lets go, the second takes over.
        await journal.Value.ReleaseWriterLockAsync();
        await second.TakeWriterLockAsync(TimeSpan.Zero, cancellationToken);
        await second.AppendAsync(QuoteBatch(2), cancellationToken);

        Assert.Contains("Only one may run against a database at a time", refused.Message, StringComparison.Ordinal);
        Assert.Equal(2, await second.GetLastInputSequenceAsync(cancellationToken));
    }

    [Fact]
    public async Task AStartingServiceWaitsForTheOneBeforeItToStop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var journal = await CreateJournalAsync();
        await using var next = CreateJournal(journal.DataSource);
        await journal.Value.TakeWriterLockAsync(TimeSpan.Zero, cancellationToken);

        var waiting = next.TakeWriterLockAsync(TimeSpan.FromSeconds(20), cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        Assert.False(waiting.IsCompleted);
        await journal.Value.ReleaseWriterLockAsync();

        await waiting.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        await next.AppendAsync(QuoteBatch(1), cancellationToken);
    }

    // The lock lives with the writer's connection. If it breaks, the writer takes the lock again before the next batch,
    // and writes nothing if another service took it in between.
    [Fact]
    public async Task AWriterWhoseConnectionBreaksWritesOnlyIfNoOtherTookTheJournal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var journal = await CreateJournalAsync();
        await using var other = CreateJournal(journal.DataSource);
        await journal.Value.AppendAsync(QuoteBatch(1), cancellationToken);

        await TerminateLockHolderAsync(journal.DataSource);
        await Assert.ThrowsAnyAsync<Exception>(() => journal.Value.AppendAsync(QuoteBatch(2), cancellationToken));
        await journal.Value.AppendAsync(QuoteBatch(2), cancellationToken);

        await TerminateLockHolderAsync(journal.DataSource);
        await Assert.ThrowsAnyAsync<Exception>(() => journal.Value.AppendAsync(QuoteBatch(3), cancellationToken));
        await other.TakeWriterLockAsync(TimeSpan.Zero, cancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.Value.AppendAsync(QuoteBatch(3), cancellationToken));

        Assert.Equal(2, await other.GetLastInputSequenceAsync(cancellationToken));
    }

    [Fact]
    public async Task ServiceRecoversFromPostgres()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        using (var first = new ServiceFactory(postgresConnectionString: connectionString))
        {
            using var client = await first.CreateTraderClientAsync("T1");
            await first.PushQuoteAsync("EURUSD", 1.08000m, 1.08010m);
            await client.PostJsonAsync("/api/accounts/T1/orders", new { orderId = "O1", symbol = "EURUSD", side = "Buy", type = "Market", volume = 1.00m });
        }

        using var second = new ServiceFactory(postgresConnectionString: connectionString);
        using var restarted = await second.LoginAsync(ServiceFactory.EmailOf("T1"), ServiceFactory.TraderPassword);

        var account = await restarted.GetJsonAsync("/api/accounts/T1");
        Assert.Equal("O1", account.GetProperty("positions")[0].GetProperty("positionId").GetString());
        Assert.Equal(["AccountCreated", "PositionOpened"], (await restarted.GetJsonAsync("/api/accounts/T1/events")).EventKinds());
    }

    private async Task<JournalHandle> CreateJournalAsync(int snapshotsToKeep = 3)
    {
        var dataSource = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        var journal = CreateJournal(dataSource, snapshotsToKeep);
        await journal.InitializeAsync(TestContext.Current.CancellationToken);
        return new JournalHandle(journal, dataSource);
    }

    /// <summary>A journal as a new start of the service creates it.</summary>
    private static PostgresEngineJournal CreateJournal(NpgsqlDataSource dataSource, int snapshotsToKeep = 3) =>
        new(
            dataSource,
            new DatabaseSchema(dataSource, TradingMigrations.All, NullLogger<DatabaseSchema>.Instance),
            Options.Create(new JournalOptions { SnapshotsToKeep = snapshotsToKeep }));

    private static string ToJson(EventEnvelope envelope) => JsonSerializer.Serialize(envelope, Json);

    private static JournalBatch QuoteBatch(long sequence) => new([new JournaledInput(sequence, new Quote(T, "EURUSD", 1.08000m, 1.08010m))], [], null);

    // Ends the session that holds the journal's lock, as a network failure or a restarted database would.
    private static async Task TerminateLockHolderAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("select pg_terminate_backend(pid) from pg_locks where locktype = 'advisory' and granted");
        Assert.Equal(true, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> items)
    {
        var list = new List<T>();
        await foreach (var item in items)
        {
            list.Add(item);
        }

        return list;
    }

    private sealed class JournalHandle(PostgresEngineJournal value, NpgsqlDataSource dataSource) : IAsyncDisposable
    {
        public PostgresEngineJournal Value { get; } = value;

        public NpgsqlDataSource DataSource { get; } = dataSource;

        public async ValueTask DisposeAsync()
        {
            await Value.DisposeAsync();
            await DataSource.DisposeAsync();
        }
    }
}
