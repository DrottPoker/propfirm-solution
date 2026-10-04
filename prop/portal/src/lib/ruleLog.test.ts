import { describe, expect, it } from "vitest";

import { describeInput, describeOutputs } from "./ruleLog";

const context = { currency: "USD", stageName: (stage: number) => ["Phase 1", "Phase 2", "Funded"][stage] ?? `Stage ${stage + 1}` };

describe("the rule log in plain sentences", () => {
  it("says what happened", () => {
    expect(describeInput({ kind: "FloorBreached", floorId: "daily", level: 95_000, equity: 94_900 }, context)).toBe(
      "Daily loss limit broken: equity 94,900.00 USD fell below 95,000.00",
    );
    expect(describeInput({ kind: "AccountUpdated", balance: 100_150, openPositions: 1 }, context)).toBe("Balance 100,150.00 USD, 1 open position");
    expect(describeInput({ kind: "RejectPayout", reason: "KYC is missing", returnProfit: true }, context)).toBe("You rejected the payout and put the profit back: KYC is missing");
  });

  it("says what the rules decided, with the loss limits' levels", () => {
    expect(
      describeOutputs(
        [
          { kind: "OpenAccountRequested", stage: 0, initialBalance: 100_000, currency: "USD" },
          { kind: "FloorRequested", floorId: "daily", floor: { kind: "StartOfDayFloor", distance: 5_000, reference: "Balance" } },
          { kind: "FloorRequested", floorId: "max-loss", floor: { kind: "FixedFloor", level: 90_000 } },
          { kind: "StagePassed", stage: 0, balance: 110_000, tradingDays: 4 },
        ],
        context,
      ),
    ).toEqual([
      "Open a trading account for Phase 1 with 100,000.00 USD",
      "Daily loss limit set 5,000.00 USD below where each day starts",
      "Max loss limit set at 90,000.00 USD",
      "Phase 1 passed with a balance of 110,000.00 USD in 4 trading days",
    ]);
  });

  it("keeps the rule engine's name for what it does not know", () => {
    expect(describeInput({ kind: "SomethingNew" }, context)).toBe("SomethingNew");
    expect(describeOutputs(null, context)).toEqual([]);
  });
});
