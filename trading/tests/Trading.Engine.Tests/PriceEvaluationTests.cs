using Trading.Engine.Events;
using Trading.Engine.Inputs;
using Trading.Engine.Tests.Support;

namespace Trading.Engine.Tests;

/// <summary>
/// A price evaluates only the accounts it can change, and those a command or a new trading day changed since their last
/// evaluation (ADR 0059). An engine read from its own state before every input evaluates every account on every price,
/// as the engine did before, and must give exactly the same events over many accounts, conversions, limits, commands
/// and days.
/// </summary>
public sealed class PriceEvaluationTests
{
    private const int Accounts = 30;

    [Theory]
    [InlineData(1, "USD")]
    [InlineData(2, "EUR")]
    [InlineData(3, "USD")]
    [InlineData(4, "EUR")]
    public void APriceGivesTheSameEventsAsWhenEveryAccountIsEvaluated(int seed, string currency)
    {
        var usd = TestMarket.Configuration(commission: 3.5m, markupPoints: 2);
        var configuration = usd with { Groups = [usd.Groups[0] with { Currency = currency }] };
        var engine = new TradingEngine(configuration);
        var reference = new TradingEngine(configuration);
        var kinds = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var input in Inputs(seed, currency))
        {
            reference = TradingEngine.FromState(configuration, reference.ExportState());
            var expected = reference.Apply(input).Select(EventJson.Serialize).ToList();
            var actual = engine.Apply(input);

            Assert.Equal(expected, actual.Select(EventJson.Serialize).ToList());
            foreach (var engineEvent in actual)
            {
                var kind = engineEvent is PositionClosed closed ? $"{nameof(PositionClosed)}:{closed.Reason}" : engineEvent.GetType().Name;
                kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
            }
        }

        // The run reached the paths a skipped evaluation could change.
        Assert.All(
            [
                nameof(EquityFloorBreached), nameof(StopOutTriggered), nameof(OwnLimitReached), nameof(TradingDayStarted), nameof(GroupSymbolsChanged),
                nameof(AccountReopened), "PositionClosed:StopLoss", "PositionClosed:TakeProfit",
            ],
            kind => Assert.True(kinds.ContainsKey(kind), $"No {kind} in the run: {string.Join(", ", kinds.Keys)}"));
    }

    private static IEnumerable<EngineInput> Inputs(int seed, string currency)
    {
        var random = new Random(seed);
        var prices = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["EURUSD"] = 1.08000m,
            ["GBPUSD"] = 1.27000m,
            ["EURGBP"] = 0.85000m,
            ["USDJPY"] = 150.000m,
            ["XAUUSD"] = 2650.00m,
        };
        var instruments = TestMarket.AllInstruments.ToDictionary(i => i.Symbol, StringComparer.Ordinal);
        var symbols = prices.Keys.ToList();
        var orders = new List<(string Account, string Id)>();
        var time = new DateTimeOffset(2026, 10, 5, 20, 0, 0, TimeSpan.Zero);
        var next = 0;

        Quote Move(string symbol, decimal step)
        {
            var point = instruments[symbol].Point;
            prices[symbol] = Math.Max(point * 100, prices[symbol] + (point * step));
            return new Quote(time, symbol, prices[symbol], prices[symbol] + (point * random.Next(1, 4)));
        }

        foreach (var symbol in symbols)
        {
            yield return Move(symbol, 0);
        }

        // A group made by input, whose conditions change while its accounts trade.
        IReadOnlyList<SymbolConditions> Conditions() => [.. symbols.Select(s => new SymbolConditions(s, random.Next(10, 200), random.Next(0, 6), random.Next(0, 8)))];
        yield return new CreateGroup(time, new TradingGroup("made", currency, 50m, Conditions()));

        for (var a = 0; a < Accounts; a++)
        {
            var account = $"A{a}";
            var balance = a % 5 == 4 ? 5_000m : random.Next(10, 100) * 1_000m;
            yield return new CreateAccount(time, account, a % 3 == 2 ? "made" : "standard", balance);
            // Every fifth account is small and has no limits at all, so only a stop out stops its losses.
            switch (a % 5)
            {
                case 0:
                    yield return new SetEquityFloor(time, account, "max-loss", new FixedFloor(balance * 0.9m));
                    break;
                case 1:
                    yield return new SetEquityFloor(time, account, "max-loss", new TrailingFloor(balance * 0.08m, balance));
                    break;
                case 2:
                    yield return new SetEquityFloor(time, account, "daily", new AnchoredFloor(balance * 0.05m, FloorAnchor.HigherOfBalanceAndEquity));
                    break;
                case 3:
                    yield return new SetOwnLimits(time, account, new OwnLimits(balance * 0.02m, balance * 0.03m, 20));
                    break;
            }
        }

        for (var i = 0; i < 4_000; i++)
        {
            time = time.AddSeconds(random.Next(1, 90));
            var account = $"A{random.Next(Accounts)}";
            var symbol = symbols[random.Next(symbols.Count)];
            var roll = random.Next(100);
            if (roll < 60)
            {
                // Most inputs are prices, now and then a jump that breaks limits and triggers stops.
                yield return Move(symbol, random.Next(100) < 4 ? random.Next(-1_500, 1_500) : random.Next(-30, 31));
                continue;
            }

            // Orders need a fresh price.
            yield return Move(symbol, random.Next(-5, 6));
            var point = instruments[symbol].Point;
            var id = $"O{next++}";
            var side = random.Next(2) == 0 ? Side.Buy : Side.Sell;
            var sign = side == Side.Buy ? 1 : -1;
            switch (roll)
            {
                case < 78:
                    var volume = instruments[symbol].VolumeMin * random.Next(1, 600);
                    var stops = random.Next(3) == 0;
                    yield return new PlaceOrder(
                        time,
                        account,
                        id,
                        symbol,
                        side,
                        OrderType.Market,
                        volume,
                        StopLoss: stops ? prices[symbol] - (sign * point * random.Next(50, 400)) : null,
                        TakeProfit: stops ? prices[symbol] + (sign * point * random.Next(50, 400)) : null,
                        TrailingStop: stops && random.Next(4) == 0);
                    orders.Add((account, id));
                    break;
                case < 84:
                    var type = random.Next(2) == 0 ? OrderType.Limit : OrderType.Stop;
                    var away = (type == OrderType.Limit ? -sign : sign) * point * random.Next(5, 200);
                    yield return new PlaceOrder(time, account, id, symbol, side, type, instruments[symbol].VolumeMin * random.Next(1, 300), prices[symbol] + away);
                    orders.Add((account, id));
                    break;
                case < 87 when orders.Count > 0:
                    var (owner, position) = orders[random.Next(orders.Count)];
                    yield return new ClosePosition(time, owner, position, random.Next(3) == 0 ? 0.01m : null);
                    break;
                case < 89 when orders.Count > 0:
                    var (holder, changed) = orders[random.Next(orders.Count)];
                    yield return new ModifyPosition(time, holder, changed, prices[symbol] - (point * 200), null, random.Next(2) == 0);
                    break;
                case < 90 when orders.Count > 0:
                    var (placer, pending) = orders[random.Next(orders.Count)];
                    yield return random.Next(2) == 0
                        ? new CancelOrder(time, placer, pending)
                        : new ModifyOrder(time, placer, pending, prices[symbol] - (sign * point * random.Next(5, 100)), null, null);
                    break;
                case < 92:
                    yield return new AdjustBalance(time, account, id, random.Next(-5, 6) * 1_000m);
                    break;
                case < 93:
                    yield return new SetOwnLimits(time, account, new OwnLimits(random.Next(1, 20) * 100m, random.Next(1, 40) * 100m, random.Next(5, 50)));
                    break;
                case < 94:
                    yield return new LockTrading(time, account, random.Next(2) == 0);
                    break;
                case < 95:
                    yield return random.Next(2) == 0 ? new SuspendAccount(time, account) : new ResumeAccount(time, account);
                    break;
                case < 96:
                    yield return new SetTradingDay(time, account, new TradingDay("Europe/Stockholm", new TimeOnly(random.Next(0, 24), 0)));
                    break;
                case < 97:
                    yield return new CloseAllPositions(time, account, random.Next(2) == 0 ? symbol : null);
                    break;
                case < 98:
                    yield return new ChangeGroupSymbols(time, "made", Conditions());
                    break;
                case < 99:
                    yield return new CloseAccount(time, account);
                    break;
                default:
                    yield return new ReopenAccount(time, account, random.Next(10, 100) * 1_000m);
                    break;
            }
        }
    }
}
