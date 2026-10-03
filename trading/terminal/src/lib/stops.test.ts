import { describe, expect, it } from "vitest";

import { clampStop, estimatedProfit, priceForAmount, resolveStops, stepAmount, stopKindAt } from "./stops";

describe("stopKindAt", () => {
  it("is a stop loss below a buy and a take profit above it", () => {
    expect(stopKindAt("Buy", 1.088, 1.089, 5)).toBe("stopLoss");
    expect(stopKindAt("Buy", 1.09, 1.089, 5)).toBe("takeProfit");
  });

  it("is a stop loss above a sell and a take profit below it", () => {
    expect(stopKindAt("Sell", 1.09, 1.089, 5)).toBe("stopLoss");
    expect(stopKindAt("Sell", 1.088, 1.089, 5)).toBe("takeProfit");
  });

  it("is nothing at the price itself", () => {
    expect(stopKindAt("Buy", 1.08900000001, 1.089, 5)).toBeNull();
  });
});

describe("resolveStops", () => {
  it("passes typed prices through", () => {
    expect(resolveStops("price", "1.08000", "", "Buy", undefined, 1, 5, undefined)).toEqual({ ok: true, stopLoss: 1.08, takeProfit: null });
    expect(resolveStops("price", "1.080001", "", "Buy", undefined, 1, 5, undefined)).toEqual({
      ok: false,
      error: "Stop loss and take profit need at most 5 decimals.",
    });
  });

  it("turns amounts into prices from the entry for the side", () => {
    expect(resolveStops("money", "100", "200", "Buy", 1.08849, 1, 5, 1)).toEqual({ ok: true, stopLoss: 1.08749, takeProfit: 1.09049 });
    expect(resolveStops("money", "100", "", "Sell", 1.08845, 1, 5, 1)).toEqual({ ok: true, stopLoss: 1.08945, takeProfit: null });
  });

  it("needs a point value and an entry only when there is an amount", () => {
    expect(resolveStops("money", "", "", "Buy", undefined, 1, 5, undefined)).toEqual({ ok: true, stopLoss: null, takeProfit: null });
    expect(resolveStops("money", "100", "", "Buy", 1.1, 1, 5, undefined).ok).toBe(false);
  });

  it("refuses amounts that are not money or too small for a point", () => {
    expect(resolveStops("money", "-5", "", "Buy", 1.1, 1, 5, 1).ok).toBe(false);
    expect(resolveStops("money", "1.005", "", "Buy", 1.1, 1, 5, 1).ok).toBe(false);
    expect(resolveStops("money", "0.50", "", "Buy", 1.1, 1, 5, 1)).toEqual({
      ok: false,
      error: "An amount is smaller than one point, or too large for the price.",
    });
  });
});

describe("estimatedProfit", () => {
  it("counts points times volume times the point value", () => {
    // 20 pips on 1 lot of EURUSD, 1 USD per point
    expect(estimatedProfit("Buy", 1, 1.0885, 1.0905, 5, 1)).toBeCloseTo(200, 10);
    expect(estimatedProfit("Sell", 1, 1.0885, 1.0905, 5, 1)).toBeCloseTo(-200, 10);
    // USDJPY at about 150: 0.6666 USD per point
    expect(estimatedProfit("Buy", 2, 150, 149.5, 3, 0.6666)).toBeCloseTo(-666.6, 10);
  });
});

describe("priceForAmount", () => {
  it("puts the stop loss below a buy and the take profit above it", () => {
    expect(priceForAmount("stopLoss", "Buy", 100, 1, 1.08849, 5, 1)).toBe(1.08749);
    expect(priceForAmount("takeProfit", "Buy", 200, 1, 1.08849, 5, 1)).toBe(1.09049);
  });

  it("puts the stop loss above a sell and the take profit below it", () => {
    expect(priceForAmount("stopLoss", "Sell", 100, 0.5, 1.08845, 5, 1)).toBe(1.09045);
    expect(priceForAmount("takeProfit", "Sell", 100, 0.5, 1.08845, 5, 1)).toBe(1.08645);
  });

  it("rounds the distance down so the amount is not exceeded", () => {
    // 100 USD at 0.6666 USD per point is 150.0 points, and 101 USD is 151.5 points.
    expect(priceForAmount("stopLoss", "Buy", 100, 1, 150, 3, 0.6666)).toBe(149.85);
    expect(priceForAmount("stopLoss", "Buy", 101, 1, 150, 3, 0.6666)).toBe(149.849);
    // Whole points even when the division is not exact in binary
    expect(priceForAmount("takeProfit", "Buy", 100, 0.1, 1.1, 5, 1)).toBe(1.11);
  });

  it("has no price for less than a point or nothing to divide by", () => {
    expect(priceForAmount("stopLoss", "Buy", 0.5, 1, 1.1, 5, 1)).toBeNull();
    expect(priceForAmount("stopLoss", "Buy", 0, 1, 1.1, 5, 1)).toBeNull();
    expect(priceForAmount("stopLoss", "Buy", 100, 1, 1.1, 5, 0)).toBeNull();
  });

  it("has no price at or below zero", () => {
    expect(priceForAmount("stopLoss", "Buy", 200_000, 1, 1.1, 5, 1)).toBeNull();
  });
});

describe("clampStop", () => {
  it("keeps a buy's stop loss below the bid and its take profit above", () => {
    expect(clampStop("stopLoss", "Buy", 1.0895, 1.089, 5)).toBe(1.08899);
    expect(clampStop("stopLoss", "Buy", 1.088, 1.089, 5)).toBe(1.088);
    expect(clampStop("takeProfit", "Buy", 1.0885, 1.089, 5)).toBe(1.08901);
  });

  it("keeps a sell's stop loss above the ask and its take profit below", () => {
    expect(clampStop("stopLoss", "Sell", 1.0885, 1.089, 5)).toBe(1.08901);
    expect(clampStop("takeProfit", "Sell", 1.0895, 1.089, 5)).toBe(1.08899);
  });
});

describe("stepAmount", () => {
  it.each([
    ["", 1, 10, "10.00"],
    ["abc", -1, 10, "10.00"],
    ["100", 1, 10, "110.00"],
    ["100", -1, 10, "90.00"],
    ["10", -1, 10, "10.00"],
    ["95", 1, 10, "110.00"],
    ["", 1, 3.7, "3.70"],
    ["", 1, 0.001, "0.01"],
  ] as const)("steps %j by %i with step %d to %j", (input, direction, step, expected) => {
    expect(stepAmount(input, direction, step)).toBe(expected);
  });
});
