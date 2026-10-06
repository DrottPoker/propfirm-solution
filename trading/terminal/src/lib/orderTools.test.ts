import { describe, expect, it } from "vitest";

import type { PositionSnapshot } from "./api/types";
import { breakEvenStop, defaultPart, partProblem, trailingPips } from "./orderTools";

const limits = { volumeMin: 0.01, volumeStep: 0.01, volumeMax: 100 };

function position(overrides: Partial<PositionSnapshot>): PositionSnapshot {
  return {
    positionId: "p",
    symbol: "EURUSD",
    side: "Buy",
    volume: 1,
    openPrice: 1.08,
    stopLoss: null,
    takeProfit: null,
    openTime: "2026-10-05T08:00:00Z",
    currentPrice: 1.081,
    profit: 100,
    margin: 1_080,
    trailingDistance: null,
    ...overrides,
  };
}

describe("breakEvenStop", () => {
  it("is the open price once the price has moved past it", () => {
    expect(breakEvenStop(position({}))).toBe(1.08);
    expect(breakEvenStop(position({ side: "Sell", currentPrice: 1.079 }))).toBe(1.08);
  });

  it("is not possible before the price has moved past the open price", () => {
    expect(breakEvenStop(position({ currentPrice: 1.08 }))).toBeNull();
    expect(breakEvenStop(position({ side: "Sell", currentPrice: 1.081 }))).toBeNull();
  });

  it("is not needed when the stop loss already protects the open price", () => {
    expect(breakEvenStop(position({ stopLoss: 1.0805 }))).toBeNull();
    expect(breakEvenStop(position({ stopLoss: 1.0795 }))).toBe(1.08);
  });
});

describe("defaultPart", () => {
  it("starts from half the position, rounded down to the step", () => {
    expect(defaultPart(1, limits)).toBe(0.5);
    expect(defaultPart(0.15, limits)).toBe(0.07);
  });

  it("leaves at least the smallest volume", () => {
    expect(defaultPart(0.02, limits)).toBe(0.01);
    expect(defaultPart(0.01, limits)).toBeNull();
    expect(defaultPart(0.15, { volumeMin: 0.1, volumeStep: 0.01, volumeMax: 50 })).toBeNull();
  });
});

describe("partProblem", () => {
  it("accepts a part on the step that leaves enough open", () => {
    expect(partProblem(0.3, 1, limits)).toBeNull();
    expect(partProblem(0.99, 1, limits)).toBeNull();
  });

  it("refuses a part off the step, too small, or that leaves too little", () => {
    expect(partProblem(0.005, 1, limits)).toMatch("steps of 0.01");
    expect(partProblem(Number.NaN, 1, limits)).toMatch("at least 0.01");
    expect(partProblem(1, 1, limits)).toMatch("Leave at least");
  });
});

describe("trailingPips", () => {
  it("writes the distance in pips", () => {
    expect(trailingPips(0.001, 5)).toBe("10.0");
    expect(trailingPips(0.5, 2)).toBe("5.0");
    expect(trailingPips(300, 2)).toBe("3000");
  });
});
