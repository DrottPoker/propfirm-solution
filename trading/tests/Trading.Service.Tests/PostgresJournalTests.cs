using System.Globalization;
using System.Text.Json;

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

        await journal.Value.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await journal.Value.GetLastInputSequenceAsync(TestContext.Current.CancellationToken));
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
            new JournalBatch(inputs.Select((input, i) => new JournaledInput(i + 1, input)).ToList(), events, snapshot),
            TestContext.Current.CancellationToken);

        var readInputs = await ToListAsync(journal.Value.ReadInputsAsync(0, TestContext.Current.CancellationToken));
        Assert.Equal(inputs, readInputs.Select(i => i.Input));
        Assert.Equal([1L, 2, 3, 4], readInputs.Select(i => i.Sequence));
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
        await journal.Value.AppendAsync(new JournalBatch([], events, null), TestContext.Current.CancellationToken);

        var first = await journal.Value.ReadEventsAsync("A", 0, 2, TestContext.Current.CancellationToken);
        var rest = await journal.Value.ReadEventsAsync("A", first[^1].Sequence, 10, TestContext.Current.CancellationToken);

        Assert.Equal([2L, 4], first.Select(e => e.Sequence));
        Assert.Equal([6L], rest.Select(e => e.Sequence));
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
            new JournaledInput(1, new Quote(T, "EURUSD", 1.08000m, 1.08010m)),
            new JournaledInput(2, new CreateAccount(T.AddMinutes(1), "A1", "standard", 1_000m)),
            new JournaledInput(3, new Quote(T.AddMinutes(2), "EURUSD", 1.08100m, 1.08110m)),
        };
        await journal.Value.AppendAsync(new JournalBatch(inputs, [], null), TestContext.Current.CancellationToken);

        var quotes = await ToListAsync(journal.Value.ReadQuotesAsync(T.AddMinutes(1), TestContext.Current.CancellationToken));

        Assert.Equal(1.08100m, Assert.Single(quotes).Bid);
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
        var journal = new PostgresEngineJournal(
            dataSource,
            Options.Create(new JournalOptions { SnapshotsToKeep = snapshotsToKeep }),
            NullLogger<PostgresEngineJournal>.Instance);
        await journal.InitializeAsync(TestContext.Current.CancellationToken);
        return new JournalHandle(journal, dataSource);
    }

    private static string ToJson(EventEnvelope envelope) => JsonSerializer.Serialize(envelope, Json);

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

        public ValueTask DisposeAsync() => DataSource.DisposeAsync();
    }
}
