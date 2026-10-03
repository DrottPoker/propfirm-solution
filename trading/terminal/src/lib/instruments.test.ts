import { describe, expect, it } from "vitest";

import type { InstrumentInfo } from "./api/types";
import { categoriesOf, categoryOf, currencySign, spreadPoints } from "./instruments";

function instrument(symbol: string, baseCurrency: string, quoteCurrency: string): InstrumentInfo {
  return {
    symbol,
    baseCurrency,
    quoteCurrency,
    contractSize: 100_000,
    digits: 5,
    volumeMin: 0.01,
    volumeStep: 0.01,
    volumeMax: 100,
    leverage: 100,
    spreadMarkupPoints: 2,
    commissionPerLotPerSide: 3.5,
  };
}

describe("categoryOf", () => {
  it("puts metals by their ISO code and other pairs in forex", () => {
    expect(categoryOf(instrument("XAUUSD", "XAU", "USD"))).toBe("Metals");
    expect(categoryOf(instrument("XAGUSD", "XAG", "USD"))).toBe("Metals");
    expect(categoryOf(instrument("EURUSD", "EUR", "USD"))).toBe("Forex");
  });
});

describe("categoriesOf", () => {
  it("lists the categories that have instruments in a fixed order", () => {
    expect(categoriesOf([instrument("XAUUSD", "XAU", "USD"), instrument("EURUSD", "EUR", "USD")])).toEqual(["Forex", "Metals"]);
    expect(categoriesOf([instrument("EURUSD", "EUR", "USD")])).toEqual(["Forex"]);
  });
});

describe("spreadPoints", () => {
  it("counts whole points", () => {
    expect(spreadPoints(1.08845, 1.08849, 5)).toBe(4);
    expect(spreadPoints(2650.25, 2650.5, 2)).toBe(25);
  });
});

describe("currencySign", () => {
  it("uses a sign when there is one and the first letter otherwise", () => {
    expect(currencySign("EUR")).toBe("€");
    expect(currencySign("XAU")).toBe("Au");
    expect(currencySign("SEK")).toBe("S");
  });
});
