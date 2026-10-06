import { describe, expect, it } from "vitest";

import { defaultSizing, parseSizing, riskAmount, sizeForRisk, stepRisk } from "./riskSize";

describe("riskAmount", () => {
  it.each([
    ["250", "money", 250],
    ["1", "balance", 1_000],
    ["0.5", "balance", 500],
    ["25", "room", 1_250],
    ["1,5", "balance", 1_500],
  ] as const)("risks %s %s as %d", (input, unit, expected) => {
    expect(riskAmount(input, unit, 100_000, 5_000)).toBe(expected);
  });

  it.each([
    ["0", "money"],
    ["abc", "money"],
    ["1.234", "money"],
    ["101", "balance"],
  ] as const)("refuses %j %s", (input, unit) => {
    expect(riskAmount(input, unit, 100_000, 5_000)).toBeNull();
  });

  it("needs the room before a share of it can be risked", () => {
    expect(riskAmount("10", "room", 100_000, undefined)).toBeNull();
  });
});

describe("sizeForRisk", () => {
  // EURUSD in a USD account: a point on one lot is worth 1 USD.
  const eurusd = { digits: 5, pointValuePerLot: 1, limits: { volumeMin: 0.01, volumeStep: 0.01, volumeMax: 50 } };

  it("finds the largest volume in whole steps that loses at most the amount at the stop loss", () => {
    // 25 pips is 250 points, 250 USD on a lot. 1,000 USD then buys 4 lots.
    expect(sizeForRisk({ ...eurusd, amount: 1_000, side: "Buy", entry: 1.1, stopLoss: 1.0975 })).toEqual({ ok: true, lots: 4, risk: 1_000, atMost: false });
    // 999 USD is 3.996 lots, rounded down to 3.99.
    expect(sizeForRisk({ ...eurusd, amount: 999, side: "Sell", entry: 1.1, stopLoss: 1.1025 })).toMatchObject({ ok: true, lots: 3.99 });
  });

  it("lowers a volume above the largest to it", () => {
    expect(sizeForRisk({ ...eurusd, amount: 1_000_000, side: "Buy", entry: 1.1, stopLoss: 1.0975 })).toEqual({ ok: true, lots: 50, risk: 12_500, atMost: true });
  });

  it("refuses an amount too small for the smallest volume, and a stop loss on the wrong side", () => {
    expect(sizeForRisk({ ...eurusd, amount: 1, side: "Buy", entry: 1.1, stopLoss: 1.0975 })).toEqual({
      ok: false,
      reason: "Too little to risk: the smallest volume, 0.01 lots, risks 2.50.",
    });
    expect(sizeForRisk({ ...eurusd, amount: 1_000, side: "Buy", entry: 1.1, stopLoss: 1.11 })).toEqual({
      ok: false,
      reason: "The stop loss is on the wrong side of the price for a buy.",
    });
  });

  it("works in steps larger than a hundredth", () => {
    const index = { digits: 1, pointValuePerLot: 0.1, limits: { volumeMin: 0.1, volumeStep: 0.1, volumeMax: 100 } };

    // 50 index points are 500 points, 50 USD on a lot. 175 USD is 3.5 lots.
    expect(sizeForRisk({ ...index, amount: 175, side: "Buy", entry: 5_000, stopLoss: 4_950 })).toEqual({ ok: true, lots: 3.5, risk: 175, atMost: false });
  });
});

describe("stepRisk", () => {
  it.each([
    ["250", 1, "money", "260"],
    ["", 1, "money", "10"],
    ["10", -1, "money", "10"],
    ["1", 1, "balance", "1.1"],
    ["0.1", -1, "balance", "0.1"],
    ["100", 1, "room", "100"],
    ["25", -1, "room", "24"],
  ] as const)("steps %j by %d in %s to %j", (input, direction, unit, expected) => {
    expect(stepRisk(input, direction, unit)).toBe(expected);
  });
});

describe("parseSizing", () => {
  it("reads a stored choice and falls back for anything else", () => {
    expect(parseSizing('{"mode":"risk","unit":"room","risks":{"room":"20","money":"250"}}')).toEqual({
      mode: "risk",
      unit: "room",
      risks: { money: "250", balance: "1", room: "20" },
    });
    expect(parseSizing('{"mode":"other","unit":"euros","risks":{"money":5}}')).toEqual(defaultSizing);
    expect(parseSizing("not json")).toEqual(defaultSizing);
    expect(parseSizing(null)).toEqual(defaultSizing);
  });
});
