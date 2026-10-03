using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Prop.Rules.Tests.Support;

/// <summary>
/// A whole challenge from purchase through a payout to a breach on the funded account, with repeated and late facts.
/// Every input becomes one JSON line with what the rule engine decided.
/// </summary>
internal static class ReplayScenario
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { AddFloorKinds } },
    };

    public static List<string> Run()
    {
        var lines = new List<string>();
        var driver = new ChallengeDriver();
        lines.Add(Line(null, driver.Outputs));

        void Apply(ChallengeInput input) => lines.Add(Line(input, driver.Apply(input)));

        var day = ChallengeDriver.Monday;
        long sequence = 0;
        DateTimeOffset At(int dayOffset, int hour) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(dayOffset).AddHours(hour);

        // Phase 1: the target is reached on the third trading day, but the stage needs four.
        Apply(new AccountOpened(At(0, 7), "C1-0", ++sequence, day));
        for (var d = 0; d < 4; d++)
        {
            if (d > 0)
            {
                Apply(new TradingDayStarted(At(d, 0), day.AddDays(d)));
            }

            Apply(new PositionOpened(At(d, 9), "C1-0", ++sequence, day.AddDays(d)));
            Apply(new AccountUpdated(At(d, 9), "C1-0", ++sequence, 100_000m + (d * 4_000m), 1));
            Apply(new AccountUpdated(At(d, 15), "C1-0", ++sequence, 100_000m + ((d + 1) * 4_000m), 0));
        }

        // A repeated fact and a fact about the passed account arrive late.
        Apply(new AccountUpdated(At(3, 15), "C1-0", sequence, 116_000m, 0));
        Apply(new AccountDisabled(At(3, 16), "C1-0", ++sequence));

        // Phase 2 opens the next day and is passed after four more trading days.
        Apply(new TradingDayStarted(At(4, 0), day.AddDays(4)));
        Apply(new AccountOpened(At(4, 8), "C1-1", ++sequence, day.AddDays(4)));
        for (var d = 4; d < 8; d++)
        {
            if (d > 4)
            {
                Apply(new TradingDayStarted(At(d, 0), day.AddDays(d)));
            }

            Apply(new PositionOpened(At(d, 10), "C1-1", ++sequence, day.AddDays(d)));
        }

        Apply(new AccountUpdated(At(7, 16), "C1-1", ++sequence, 105_250.40m, 0));

        // The firm approves funding, and the funded trader trades on five days.
        Apply(new ApproveFunding(At(8, 9)));
        Apply(new AccountOpened(At(8, 10), "C1-2", ++sequence, day.AddDays(8)));
        for (var d = 8; d < 13; d++)
        {
            if (d > 8)
            {
                Apply(new TradingDayStarted(At(d, 0), day.AddDays(d)));
            }

            Apply(new PositionOpened(At(d, 11), "C1-2", ++sequence, day.AddDays(d)));
            Apply(new AccountUpdated(At(d, 15), "C1-2", ++sequence, 100_000m + ((d - 7) * 1_500.25m), d < 12 ? 0 : 1));
        }

        // A payout is refused while a position is open. Once it is closed, the profit is withdrawn and the firm pays.
        Apply(new RequestPayout(At(12, 16), "P1"));
        Apply(new AccountUpdated(At(12, 17), "C1-2", ++sequence, 107_501.25m, 0));
        Apply(new RequestPayout(At(12, 18), "P1"));
        Apply(new BalanceAdjusted(At(12, 18), "C1-2", ++sequence, "P1", -7_501.25m, 100_000m));
        Apply(new TradingDayStarted(At(13, 0), day.AddDays(13)));
        Apply(new ApprovePayout(At(13, 9), "P1"));
        Apply(new MarkPayoutPaid(At(13, 10), "P1", "wire-17"));
        Apply(new RequestPayout(At(13, 11), "P2"));

        // The funded account then breaches its daily floor.
        Apply(new PositionOpened(At(13, 12), "C1-2", ++sequence, day.AddDays(13)));
        Apply(new FloorBreached(At(13, 13), "C1-2", ++sequence, FloorIds.Daily, 95_000m, 94_987.15m));
        Apply(new CancelChallenge(At(13, 14), "Too late"));
        return lines;
    }

    private static string Line(ChallengeInput? input, IEnumerable<ChallengeOutput> outputs)
    {
        var line = new JsonObject
        {
            ["input"] = input is null ? "Start" : Typed(input),
            ["outputs"] = new JsonArray([.. outputs.Select(Typed)]),
        };
        return line.ToJsonString();
    }

    private static JsonObject Typed(object value)
    {
        var json = JsonSerializer.SerializeToNode(value, value.GetType(), Json)!.AsObject();
        json.Insert(0, "type", value.GetType().Name);
        return json;
    }

    private static void AddFloorKinds(JsonTypeInfo info)
    {
        if (info.Type != typeof(FloorSpec))
        {
            return;
        }

        info.PolymorphismOptions = new JsonPolymorphismOptions { TypeDiscriminatorPropertyName = "kind" };
        foreach (var kind in new[] { typeof(StartOfDayFloor), typeof(FixedFloor), typeof(TrailingFloor) })
        {
            info.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(kind, kind.Name));
        }
    }
}
