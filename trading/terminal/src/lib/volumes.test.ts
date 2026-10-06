import { describe, expect, it } from "vitest";

import type { InstrumentInfo } from "./api/types";
import { defaultVolume, parseVolumes, startVolume } from "./volumes";

const gold: InstrumentInfo = {
  symbol: "XAUUSD",
  baseCurrency: "XAU",
  quoteCurrency: "USD",
  contractSize: 100,
  digits: 2,
  volumeMin: 0.01,
  volumeStep: 0.01,
  volumeMax: 50,
  leverage: 30,
  spreadMarkupPoints: 0,
  commissionPerLotPerSide: 3.5,
};

describe("parseVolumes", () => {
  it.each([
    [null, {}],
    ["", {}],
    ['{"XAUUSD":"0.10","EURUSD":"2.00"}', { XAUUSD: "0.10", EURUSD: "2.00" }],
    ['{"XAUUSD":0.1,"EURUSD":"2.00"}', { EURUSD: "2.00" }],
    ['["0.10"]', {}],
    ["null", {}],
    ["not json", {}],
  ])("reads %j as %j", (raw, expected) => {
    expect(parseVolumes(raw)).toEqual(expected);
  });
});

describe("startVolume", () => {
  it("starts from the volume last chosen for the symbol", () => {
    expect(startVolume(gold, { XAUUSD: "0.10", EURUSD: "2.00" })).toBe("0.10");
  });

  it("starts from the default without a volume for the symbol", () => {
    expect(startVolume(gold, { EURUSD: "0.10" })).toBe(defaultVolume);
    expect(defaultVolume).toBe("1.00");
  });

  it("starts from the default when the instrument no longer allows the volume", () => {
    expect(startVolume(gold, { XAUUSD: "60.00" })).toBe(defaultVolume);
    expect(startVolume(gold, { XAUUSD: "0.015" })).toBe(defaultVolume);
    expect(startVolume(gold, { XAUUSD: "lots" })).toBe(defaultVolume);
  });
});
