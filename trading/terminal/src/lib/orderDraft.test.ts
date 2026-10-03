import { describe, expect, it } from "vitest";

import { ghostLines, sideOfStops } from "./orderDraft";

describe("sideOfStops", () => {
  it("finds the side that typed stops fit", () => {
    // A market order closes a buy at the bid 1.0890 and a sell at the ask 1.0892.
    expect(sideOfStops(1.088, 1.09, 1.089, 1.0892)).toBe("Buy");
    expect(sideOfStops(1.09, 1.088, 1.089, 1.0892)).toBe("Sell");
    expect(sideOfStops(1.088, null, 1.089, 1.0892)).toBe("Buy");
    expect(sideOfStops(null, 1.088, 1.089, 1.0892)).toBe("Sell");
  });

  it("has no side when the stops fit both or neither", () => {
    expect(sideOfStops(null, null, 1.089, 1.0892)).toBeNull();
    // Between the bid and the ask, a stop loss fits neither side.
    expect(sideOfStops(1.0891, null, 1.089, 1.0892)).toBeNull();
    expect(sideOfStops(1.088, 1.087, 1.089, 1.0892)).toBeNull();
  });
});

describe("ghostLines", () => {
  const base = { side: "Buy" as const, lots: 1, digits: 5, pointValuePerLot: 1 };

  it("shows the stops of a market order with the estimated result", () => {
    const lines = ghostLines({ ...base, type: "Market", entry: 1.08849, stops: { ok: true, stopLoss: 1.08749, takeProfit: 1.09049 } });

    expect(lines).toEqual([
      { kind: "stopLoss", price: 1.08749, title: "Buy SL -100.00" },
      { kind: "takeProfit", price: 1.09049, title: "Buy TP +200.00" },
    ]);
  });

  it("shows the order price of a pending order, even without stops", () => {
    const lines = ghostLines({ ...base, side: "Sell", type: "Limit", entry: 1.09, stops: { ok: true, stopLoss: null, takeProfit: null } });

    expect(lines).toEqual([{ kind: "entry", price: 1.09, title: "Sell limit" }]);
  });

  it("leaves out the result without a point value, and the stops when they cannot be read", () => {
    expect(
      ghostLines({ ...base, type: "Market", entry: 1.08849, pointValuePerLot: undefined, stops: { ok: true, stopLoss: 1.08749, takeProfit: null } }),
    ).toEqual([{ kind: "stopLoss", price: 1.08749, title: "Buy SL" }]);
    expect(ghostLines({ ...base, type: "Market", entry: 1.08849, stops: { ok: false, error: "Invalid" } })).toEqual([]);
  });
});
