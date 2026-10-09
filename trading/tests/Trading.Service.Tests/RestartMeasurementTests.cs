using System.Globalization;
using System.Text.Json;

using Common.Postgres;

using Microsoft.Extensions.DependencyInjection;
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

/// <summary>
/// How long a restart takes with many accounts (ADR 0059). After a crash the service reads the latest snapshot and
/// replays the inputs after it, at most one snapshot interval, before it takes orders again. The journal here has the
/// service's own instruments, accounts with two open positions each and the worst case: a crash one input before the
/// next snapshot. Set TRADING_RESTART_ACCOUNTS to a list such as "1000,5000,20000" to measure other sizes, and
/// TRADING_RESTART_REPORT to a file to keep the figures.
/// </summary>
public sealed class RestartMeasurementTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Group = "standard";

    // A weekday in the middle of the trading day, in whole microseconds as Postgres stores them.
    private static readonly DateTimeOffset T = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Json = EngineJson.CreateOptions();

    [Fact]
    public async Task ARestartAfterACrashReplaysASnapshotIntervalInSeconds()
    {
        var sizes = (Environment.GetEnvironmentVariable("TRADING_RESTART_ACCOUNTS") ?? "1000")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.Parse(s, CultureInfo.InvariantCulture));
        foreach (var accounts in sizes)
        {
            var measured = await MeasureAsync(accounts);
            Report(measured);

            // The budget is generous, so a slow test machine never fails it, but a restart that took minutes would.
            if (accounts <= 1_000)
            {
                Assert.True(measured.Restart < TimeSpan.FromSeconds(15), $"The restart took {measured.Restart}.");
            }
        }
    }

    private async Task<Measurement> MeasureAsync(int accounts)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var configuration = await ServiceConfigurationAsync();
        var interval = new JournalOptions().SnapshotInterval;
        await using var dataSource = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());

        // The journal as the service leaves it: accounts and positions, a snapshot, then all but one input of an interval.
        var writer = new JournalWriter(configuration, CreateJournal(dataSource));
        await writer.Journal.InitializeAsync(cancellationToken);
        var prices = configuration.Groups.Single(g => g.Id == Group).Symbols.Select(s => s.Symbol).ToDictionary(s => s, StartPrice);
        await writer.ApplyAsync(Quotes(prices, configuration, prices.Count), cancellationToken);
        await writer.ApplyAsync(Accounts(accounts, prices.Keys.ToList(), configuration), cancellationToken);
        var export = System.Diagnostics.Stopwatch.StartNew();
        var state = writer.Engine.ExportState();
        export.Stop();
        var snapshotBytes = JsonSerializer.SerializeToUtf8Bytes(state, Json).Length;
        await writer.Journal.AppendAsync(new JournalBatch([], [], new JournalSnapshot(writer.InputSequence, writer.EventSequence, ConfigurationFingerprint.Of(configuration), state)), cancellationToken);
        var live = System.Diagnostics.Stopwatch.StartNew();
        await writer.ApplyAsync(Quotes(prices, configuration, interval - 1), cancellationToken);
        live.Stop();
        var openPositions = writer.Engine.ExportState().Accounts.Sum(a => a.Positions.Count);
        await writer.Journal.DisposeAsync();

        // The restart, as the service makes it.
        await using var journal = CreateJournal(dataSource);
        var host = new EngineHost(
            configuration,
            journal,
            new EventLog(),
            TimeProvider.System,
            Options.Create(new JournalOptions()),
            new FakeLifetime(),
            new EngineMetrics(TimeProvider.System),
            NullLogger<EngineHost>.Instance);
        var restart = System.Diagnostics.Stopwatch.StartNew();
        await host.StartAsync(cancellationToken);
        await host.Ready.WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
        restart.Stop();
        await host.StopAsync(CancellationToken.None);
        host.Dispose();

        return new Measurement(accounts, openPositions, interval - 1, snapshotBytes, export.Elapsed, live.Elapsed / (interval - 1), restart.Elapsed);
    }

    // The service's own instruments and conditions, open around the clock so the weekday does not matter.
    private static async Task<EngineConfiguration> ServiceConfigurationAsync()
    {
        using var service = new ServiceFactory();
        await service.Engine.Ready;
        var configuration = service.Services.GetRequiredService<EngineConfiguration>();
        return configuration with { Instruments = [.. configuration.Instruments.Select(i => i with { TradingHours = null })] };
    }

    private static PostgresEngineJournal CreateJournal(NpgsqlDataSource dataSource) =>
        new(dataSource, new DatabaseSchema(dataSource, TradingMigrations.All, NullLogger<DatabaseSchema>.Instance), Options.Create(new JournalOptions()));

    // Every account has the usual loss limits and two small positions, spread over the symbols.
    private static IEnumerable<Func<DateTimeOffset, EngineInput>> Accounts(int count, List<string> symbols, EngineConfiguration configuration)
    {
        for (var i = 0; i < count; i++)
        {
            var accountId = $"A{i}";
            yield return t => new CreateAccount(t, accountId, Group, 100_000m);
            yield return t => new SetEquityFloor(t, accountId, "max-loss", new FixedFloor(50_000m));
            yield return t => new SetEquityFloor(t, accountId, "daily", new FixedFloor(90_000m));
            for (var p = 0; p < 2; p++)
            {
                var symbol = symbols[((i * 2) + p) % symbols.Count];
                var volume = configuration.Instruments.Single(x => x.Symbol == symbol).VolumeMin;
                var orderId = $"O{i}-{p}";
                var side = p == 0 ? Side.Buy : Side.Sell;
                yield return t => new PlaceOrder(t, accountId, orderId, symbol, side, OrderType.Market, volume);
            }
        }
    }

    // Prices a point or two apart, the symbols in turn, as a feed sends them.
    private static IEnumerable<Func<DateTimeOffset, EngineInput>> Quotes(Dictionary<string, decimal> prices, EngineConfiguration configuration, int count)
    {
        var symbols = prices.Keys.ToList();
        var random = new Random(7);
        for (var i = 0; i < count; i++)
        {
            var symbol = symbols[i % symbols.Count];
            var point = configuration.Instruments.Single(x => x.Symbol == symbol).Point;
            prices[symbol] += point * random.Next(-2, 3);
            var bid = prices[symbol];
            yield return t => new Quote(t, symbol, bid, bid + (point * 2));
        }
    }

    // Roughly where each market trades, so conversions between currencies are realistic.
    private static decimal StartPrice(string symbol) => symbol switch
    {
        "EURUSD" => 1.08000m,
        "GBPUSD" => 1.27000m,
        "AUDUSD" => 0.66000m,
        "NZDUSD" => 0.60000m,
        "USDJPY" => 150.000m,
        "USDCHF" => 0.88000m,
        "USDCAD" => 1.36000m,
        "EURGBP" => 0.85000m,
        "EURJPY" => 162.000m,
        "GBPJPY" => 190.000m,
        "XAUUSD" => 2650.00m,
        "XAGUSD" => 31.000m,
        _ => 1000.0m,
    };

    private static void Report(Measurement m)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{m.Accounts} accounts, {m.OpenPositions} open positions: snapshot {m.SnapshotBytes / 1_048_576.0:0.0} MB exported in {m.Export.TotalMilliseconds:0} ms, " +
            $"a price applied live in {m.PerQuote.TotalMicroseconds:0} µs, restart replaying {m.Replayed} inputs in {m.Restart.TotalMilliseconds:0} ms");
        TestContext.Current.TestOutputHelper?.WriteLine(line);
        if (Environment.GetEnvironmentVariable("TRADING_RESTART_REPORT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private sealed record Measurement(int Accounts, int OpenPositions, int Replayed, int SnapshotBytes, TimeSpan Export, TimeSpan PerQuote, TimeSpan Restart);

    /// <summary>Applies inputs to an engine and journals them the way the service does, in batches.</summary>
    private sealed class JournalWriter(EngineConfiguration configuration, PostgresEngineJournal journal)
    {
        private const int BatchSize = 1_000;

        private DateTimeOffset _clock = T;

        public TradingEngine Engine { get; } = new(configuration);

        public PostgresEngineJournal Journal { get; } = journal;

        public long InputSequence { get; private set; }

        public long EventSequence { get; private set; }

        public async Task ApplyAsync(IEnumerable<Func<DateTimeOffset, EngineInput>> inputs, CancellationToken cancellationToken)
        {
            var batchInputs = new List<JournaledInput>(BatchSize);
            var batchEvents = new List<JournaledEvent>();
            foreach (var create in inputs)
            {
                _clock = _clock.AddTicks(TimeSpan.TicksPerMicrosecond);
                var input = create(_clock);
                var events = Engine.Apply(input);
                if (events.OfType<InputRejected>().FirstOrDefault() is { } rejected)
                {
                    throw new InvalidOperationException($"The engine rejected {input.GetType().Name}: {rejected.Reason}.");
                }

                batchInputs.Add(new JournaledInput(++InputSequence, input) { Feed = input is Quote ? "Measurement" : null });
                foreach (var engineEvent in events)
                {
                    batchEvents.Add(new JournaledEvent(new EventEnvelope(++EventSequence, engineEvent), EventLog.AccountIdOf(engineEvent) is null ? null : Group)
                    {
                        InputSequence = InputSequence,
                    });
                }

                if (batchInputs.Count == BatchSize)
                {
                    await Journal.AppendAsync(new JournalBatch([.. batchInputs], [.. batchEvents], null), cancellationToken);
                    batchInputs.Clear();
                    batchEvents.Clear();
                }
            }

            if (batchInputs.Count > 0)
            {
                await Journal.AppendAsync(new JournalBatch(batchInputs, batchEvents, null), cancellationToken);
            }
        }
    }
}
