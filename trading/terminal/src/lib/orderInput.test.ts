import { describe, expect, it } from "vitest";

import type { InstrumentInfo } from "./api/types";
import { parsePrice, parseVolume } from "./orderInput";

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
