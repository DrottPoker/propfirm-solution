using Trading.Engine.Events;
using Trading.Engine.Inputs;

namespace Trading.Engine.Tests.Support;

/// <summary>
/// Replays 3 000 synthetic EURUSD prices with orders, floors and closes in between.
/// Returns every event and the final account snapshots as JSON lines.
/// The engine can be restarted from its exported state at given ticks, which must not change the result.
/// </summary>
internal static class ReplayScenario
{
    private const int TickCount = 3_000;

    public static List<string> Run(params int[] restartAtTicks)
    {
        var configuration = new EngineConfiguration(
            [TestMarket.EurUsd],
            [
                new TradingGroup("standard", "USD", 50m, [new SymbolConditions("EURUSD", 100, 2, 3.50m)]),
                new TradingGroup("high-leverage", "USD", 100m, [new SymbolConditions("EURUSD", 500, 2, 3.50m)]),
            ],
            TimeSpan.FromSeconds(5));
        var engine = new TradingEngine(configuration);
        var events = new List<EngineEvent>();
        var point = TestMarket.EurUsd.Point;
        var now = TestMarket.Start;

        void Send(EngineInput input) => events.AddRange(engine.Apply(input));

        Send(new CreateAccount(now, "A", "standard", 100_000m));
        Send(new CreateAccount(now, "B", "standard", 5_000m));
        Send(new CreateAccount(now, "C", "high-leverage", 1_000m));
        Send(new SetEquityFloor(now, "A", "daily", new FixedFloor(95_000m)));
        Send(new SetEquityFloor(now, "A", "max-loss", new TrailingFloor(10_000m, LockLevel: 100_000m)));
        Send(new SetEquityFloor(now, "B", "max-loss", new FixedFloor(4_700m)));

        var tick = 0;
        foreach (var (bid, ask) in SyntheticTicks.EurUsd(TickCount))
        {
            if (restartAtTicks.Contains(tick))
            {
                engine = TradingEngine.FromState(configuration, engine.ExportState());
            }

            now = now.AddMilliseconds(250);
            Send(new Quote(now, "EURUSD", bid, ask));

            switch (tick)
            {
                case 10:
                    Send(new PlaceOrder(now, "A", "A-1", "EURUSD", Side.Buy, OrderType.Market, 1.00m, null, bid - (30 * point), bid + (30 * point)));
                    break;
                case 40:
                    Send(new PlaceOrder(now, "A", "A-2", "EURUSD", Side.Sell, OrderType.Limit, 0.50m, ask + (20 * point), ask + (60 * point), ask - (20 * point)));
                    break;
                case 80:
                    Send(new PlaceOrder(now, "A", "A-3", "EURUSD", Side.Buy, OrderType.Stop, 0.50m, ask + (25 * point), ask - (5 * point), ask + (55 * point)));
                    break;
                case 150:
                    Send(new PlaceOrder(now, "B", "B-1", "EURUSD", Side.Buy, OrderType.Market, 3.00m));
                    break;
                case 300:
                    // Nearly all free margin used, so a small move triggers stop out
                    Send(new PlaceOrder(now, "C", "C-1", "EURUSD", Side.Buy, OrderType.Market, 4.00m));
                    break;
                case 700:
                    Send(new PlaceOrder(now, "A", "A-4", "EURUSD", Side.Sell, OrderType.Market, 2.00m));
                    break;
                case 1_200:
                    // New trading day: the daily floor moves to 5 000 below current equity
                    var equity = engine.GetAccount("A")!.Equity;
                    Send(new SetEquityFloor(now, "A", "daily", new FixedFloor(equity - 5_000m)));
                    break;
                case 2_000:
                    foreach (var position in engine.GetAccount("A")!.Positions)
                    {
                        Send(new ClosePosition(now, "A", position.PositionId));
                    }

                    break;
                case 2_500:
                    Send(new CloseAccount(now, "A"));
                    break;
            }

            tick++;
        }

        var lines = events.Select(EventJson.Serialize).ToList();
        lines.Add(EventJson.Serialize(engine.GetAccount("A")!));
        lines.Add(EventJson.Serialize(engine.GetAccount("B")!));
        lines.Add(EventJson.Serialize(engine.GetAccount("C")!));
        return lines;
    }
}
