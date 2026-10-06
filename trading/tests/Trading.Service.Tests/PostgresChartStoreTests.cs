using System.Globalization;

using Common.Postgres;

using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Trading.Service.Candles;
using Trading.Service.Persistence;
using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

public sealed class PostgresChartStoreTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset T = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HistoryReplacesOnlyTheFeedsEarlierBarsAndIsMarkedAsLoaded()
    {
        await using var store = await CreateStoreAsync();
        await store.Value.SaveAsync("Tiingo", [Minute(T, 1.08000m), Minute(T.AddMinutes(1), 1.08100m)], TestContext.Current.CancellationToken);
        await store.Value.SaveAsync("Synthetic", [Minute(T, 1.07000m)], TestContext.Current.CancellationToken);
        var before = await store.Value.GetHistoryReachAsync("Tiingo", TestContext.Current.CancellationToken);

        var history = new ChartBar("EURUSD", Timeframe.M15, new Candle(T.AddMinutes(-15), 1.07900m, 1.08050m, 1.07850m, 1.08000m, 0));
        await store.Value.ReplaceHistoryAsync("Tiingo", T.AddDays(-180), T.AddMinutes(1), [history], TestContext.Current.CancellationToken);

        Assert.Null(before);
        Assert.Equal(T.AddDays(-180), await store.Value.GetHistoryReachAsync("Tiingo", TestContext.Current.CancellationToken));
        Assert.Null(await store.Value.GetHistoryReachAsync("Synthetic", TestContext.Current.CancellationToken));
        Assert.Equal([history, Minute(T.AddMinutes(1), 1.08100m)], await ReadAsync(store.Value, "Tiingo", T.AddHours(-1)));
        Assert.Equal([Minute(T, 1.07000m)], await ReadAsync(store.Value, "Synthetic", T.AddHours(-1)));

        await store.Value.ForgetHistoryAsync("Tiingo", TestContext.Current.CancellationToken);
        Assert.Null(await store.Value.GetHistoryReachAsync("Tiingo", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SavedBarsReplaceEarlierOnesAndComeBackExactly()
    {
        await using var store = await CreateStoreAsync();
        await store.Value.SaveAsync("Tiingo", [Minute(T, 1.08000m)], TestContext.Current.CancellationToken);
        await store.Value.SaveAsync("Tiingo", [Minute(T, 1.08010m), Minute(T, 1.08000m) with { Symbol = "GBPUSD" }], TestContext.Current.CancellationToken);

        var bars = await ReadAsync(store.Value, "Tiingo", T);

        Assert.Equal([Minute(T, 1.08010m), Minute(T, 1.08000m) with { Symbol = "GBPUSD" }], bars);
        Assert.Equal("1.08000", bars[1].Candle.Close.ToString(CultureInfo.InvariantCulture));
        Assert.Empty(await ReadAsync(store.Value, "Tiingo", T.AddMinutes(1)));
    }

    [Fact]
    public async Task OldBarsOfEveryFeedArePruned()
    {
        await using var store = await CreateStoreAsync();
        foreach (var feed in new[] { "Tiingo", "Synthetic" })
        {
            await store.Value.SaveAsync(feed, [Minute(T.AddDays(-2), 1.07000m), Minute(T, 1.08000m)], TestContext.Current.CancellationToken);
        }

        await store.Value.PruneAsync(T.AddDays(-1), TestContext.Current.CancellationToken);

        Assert.Equal([Minute(T, 1.08000m)], await ReadAsync(store.Value, "Tiingo", T.AddDays(-3)));
        Assert.Equal([Minute(T, 1.08000m)], await ReadAsync(store.Value, "Synthetic", T.AddDays(-3)));
    }

    private static ChartBar Minute(DateTimeOffset time, decimal close) =>
        new("EURUSD", Timeframe.M1, new Candle(time, close - 0.00010m, close + 0.00020m, close - 0.00020m, close, 12));

    private static async Task<List<ChartBar>> ReadAsync(PostgresChartStore store, string feed, DateTimeOffset since)
    {
        var bars = new List<ChartBar>();
        await foreach (var bar in store.ReadAsync(feed, since, TestContext.Current.CancellationToken))
        {
            bars.Add(bar);
        }

        return bars;
    }

    private async Task<StoreHandle> CreateStoreAsync()
    {
        var dataSource = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
        return new StoreHandle(new PostgresChartStore(dataSource, new DatabaseSchema(dataSource, TradingMigrations.All, NullLogger<DatabaseSchema>.Instance)), dataSource);
    }

    private sealed class StoreHandle(PostgresChartStore value, NpgsqlDataSource dataSource) : IAsyncDisposable
    {
        public PostgresChartStore Value { get; } = value;

        public ValueTask DisposeAsync() => dataSource.DisposeAsync();
    }
}
