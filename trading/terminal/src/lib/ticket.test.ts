import { describe, expect, it } from "vitest";

import type { InstrumentInfo } from "./api/types";
import { distanceUnit, markupPips, pendingType, pendingTypeProblem, pipsOf, pipsOfRoom, pipsText, spreadText, startVolume } from "./ticket";

const eurusd: InstrumentInfo = {
  symbol: "EURUSD",
  name: "Euro / US Dollar",
  category: "Forex",
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

const quote = { bid: 1.12, ask: 1.12009 };

describe("pendingType", () => {
  it("makes a buy below the ask a limit and above it a stop", () => {
    expect(pendingType("Buy", 1.119, quote, 5)).toBe("Limit");
    expect(pendingType("Buy", 1.121, quote, 5)).toBe("Stop");
  });

  it("makes a sell above the bid a limit and below it a stop", () => {
    expect(pendingType("Sell", 1.121, quote, 5)).toBe("Limit");
    expect(pendingType("Sell", 1.119, quote, 5)).toBe("Stop");
  });

  it("has no pending type at the market price itself", () => {
    expect(pendingType("Buy", 1.12009, quote, 5)).toBeNull();
    expect(pendingType("Sell", 1.12, quote, 5)).toBeNull();
  });
});

describe("pendingTypeProblem", () => {
  it("takes a limit at a better price than the market and a stop at a worse one", () => {
    expect(pendingTypeProblem("Buy", "Limit", 1.119, quote, 5)).toBeNull();
    expect(pendingTypeProblem("Sell", "Limit", 1.121, quote, 5)).toBeNull();
    expect(pendingTypeProblem("Buy", "Stop", 1.121, quote, 5)).toBeNull();
    expect(pendingTypeProblem("Sell", "Stop", 1.119, quote, 5)).toBeNull();
  });

  it("says which side of the market each type waits on, measured from the ask for a buy and the bid for a sell", () => {
    expect(pendingTypeProblem("Buy", "Limit", 1.121, quote, 5)).toBe("A buy limit waits below the market price, now 1.12009.");
    expect(pendingTypeProblem("Sell", "Limit", 1.119, quote, 5)).toBe("A sell limit waits above the market price, now 1.12000.");
    expect(pendingTypeProblem("Buy", "Stop", 1.119, quote, 5)).toBe("A buy stop waits above the market price, now 1.12009.");
    expect(pendingTypeProblem("Sell", "Stop", 1.121, quote, 5)).toBe("A sell stop waits below the market price, now 1.12000.");
  });

  it("takes neither type at the market price itself", () => {
    expect(pendingTypeProblem("Buy", "Limit", 1.12009, quote, 5)).not.toBeNull();
    expect(pendingTypeProblem("Buy", "Stop", 1.12009, quote, 5)).not.toBeNull();
  });
});

describe("distances", () => {
  it("tells currencies and metals in pips and the rest in points", () => {
    expect(distanceUnit({ category: "Forex" })).toBe("pips");
    expect(distanceUnit({ category: "Metals" })).toBe("pips");
    expect(distanceUnit({ category: "Indices" })).toBe("points");
  });

  it("turns prices and the engine's markup into pips", () => {
    expect(pipsOf(0.00009, 5)).toBe(0.9);
    expect(pipsOf(0.6, 2)).toBe(6);
    expect(markupPips(2, 5)).toBe(0.2);
    expect(pipsText(0.9, "pips")).toBe("0.9 pips");
    expect(pipsText(1, "points")).toBe("1 point");
    expect(pipsText(123.4, "pips")).toBe("123 pips");
    expect(spreadText({ bid: 1.08845, ask: 1.08849 }, eurusd)).toBe("0.4 pips");
    expect(spreadText({ bid: 24010.5, ask: 24011.7 }, { category: "Indices", digits: 1 })).toBe("1.2 points");
  });

  it("tells how many pips the room lasts", () => {
    expect(pipsOfRoom(500, 10)).toBe(50);
    expect(pipsOfRoom(500, 0)).toBe(0);
  });
});

describe("startVolume", () => {
  const smallest = { kind: "Smallest" as const, value: null };

  it("starts from the volume the trader last chose for the symbol", () => {
    expect(startVolume(eurusd, { EURUSD: "0.25" }, smallest)).toBe("0.25");
  });

  // 1 lot of gold is about 420,000 USD, so a ticket never starts there by default (ADR 0058).
  it("starts at the smallest volume, or at the firm's lots within the limits", () => {
    expect(startVolume(eurusd, {}, smallest)).toBe("0.01");
    expect(startVolume(eurusd, {}, { kind: "Lots", value: 0.1 })).toBe("0.10");
    expect(startVolume(eurusd, {}, { kind: "Lots", value: 500 })).toBe("100.00");
    expect(startVolume(eurusd, { EURUSD: "0.001" }, smallest)).toBe("0.01");
  });
});
