import { describe, expect, it } from "vitest";

import type { InstrumentInfo } from "./api/types";
import { parsePrice, parseVolume, pipSize, stepPrice, stepVolume } from "./orderInput";

const eurUsd: InstrumentInfo = {
  symbol: "EURUSD",
  baseCurrency: "EUR",
  quoteCurrency: "USD",
  contractSize: 100_000,
  digits: 5,
  volumeMin: 0.01,
  volumeStep: 0.01,
  volumeMax: 100,
  leverage: 100,
  spreadMarkupPoints: 2,
  commissionPerLotPerSide: 3.5,
};

describe("parsePrice", () => {
  it.each([
    ["", null],
    ["  ", null],
    ["1.08", 1.08],
    ["1.08012", 1.08012],
    ["1,08012", 1.08012],
  ])("accepts %j", (input, expected) => {
    expect(parsePrice(input, 5)).toEqual({ ok: true, value: expected });
  });

  it.each(["1.080125", "0", "-1.08", "abc", "1.08.1", "1e3"])("rejects %j", (input) => {
    expect(parsePrice(input, 5)).toEqual({ ok: false });
  });
});

describe("parseVolume", () => {
  it.each([
    ["0.01", 0.01],
    ["1", 1],
    ["1.00", 1],
    ["0.29", 0.29],
    ["100", 100],
  ])("accepts %j", (input, expected) => {
    expect(parseVolume(input, eurUsd)).toEqual({ ok: true, value: expected });
  });

  it.each(["", "0", "0.001", "0.015", "100.01", "abc"])("rejects %j", (input) => {
    expect(parseVolume(input, eurUsd)).toEqual({ ok: false });
  });
});

describe("stepVolume", () => {
  it.each([
    ["1.00", 1, "1.01"],
    ["1.00", -1, "0.99"],
    ["0.01", -1, "0.01"],
    ["100", 1, "100.00"],
    ["1,5", 1, "1.51"],
    ["0.015", 1, "0.03"],
    ["", 1, "0.01"],
    ["abc", -1, "0.01"],
  ] as const)("steps %j by %i to %j", (input, direction, expected) => {
    expect(stepVolume(input, direction, eurUsd)).toBe(expected);
  });

  it("keeps whole lots for whole steps", () => {
    expect(stepVolume("2", 1, { ...eurUsd, volumeMin: 1, volumeStep: 1, volumeMax: 10 })).toBe("3");
  });
});

describe("pipSize", () => {
  it.each([
    [5, 0.0001],
    [3, 0.01],
    [2, 0.1],
    [0, 1],
  ])("is one tenth of the last two decimals for %i digits", (digits, expected) => {
    expect(pipSize(digits)).toBeCloseTo(expected, 10);
  });
});

describe("stepPrice", () => {
  it("steps a typed price by one pip", () => {
    expect(stepPrice("1.08000", 1, 5, undefined)).toBe("1.08010");
    expect(stepPrice("1.08000", -1, 5, undefined)).toBe("1.07990");
    expect(stepPrice("150.000", 1, 3, undefined)).toBe("150.010");
  });

  it("starts from the current price when nothing is typed", () => {
    expect(stepPrice("", 1, 5, 1.08845)).toBe("1.08855");
    expect(stepPrice("abc", -1, 2, 2650.25)).toBe("2650.15");
  });

  it("stays empty without a typed or current price", () => {
    expect(stepPrice("", 1, 5, undefined)).toBe("");
  });

  it("never goes below one point", () => {
    expect(stepPrice("0.00005", -1, 5, undefined)).toBe("0.00001");
  });
});
