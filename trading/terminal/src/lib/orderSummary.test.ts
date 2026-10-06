import { describe, expect, it } from "vitest";

import type { FloorSnapshot } from "./api/types";
import { estimatedMargin, formatShare, nearestLimit, pipValue, riskText, shareOfRoom, shareRisk, stopLossRisk } from "./orderSummary";

const floor = (floorId: string, headroom: number): FloorSnapshot => ({
  floorId,
  rule: { kind: "TrailingFloor", distance: 5_000 },
  level: 95_000,
  highWaterMark: 100_000,
  headroom,
});

describe("estimatedMargin", () => {
  it("is the value in the quote currency over the leverage when the account is in the quote currency", () => {
    // 1 lot of EURUSD at 1.08700 is 108,700 USD, and a point is worth 1 USD per lot.
    const margin = estimatedMargin({ lots: 1, price: 1.087, digits: 5, contractSize: 100_000, leverage: 100, pointValuePerLot: 1, baseIsAccountCurrency: false });
    expect(margin).toBeCloseTo(1_087, 6);
  });

  it("is the contract over the leverage when the account is in the base currency", () => {
    const margin = estimatedMargin({ lots: 1, price: 150.123, digits: 3, contractSize: 100_000, leverage: 100, pointValuePerLot: 0.666, baseIsAccountCurrency: true });
    expect(margin).toBe(1_000);
  });

  it("follows the contract size and leverage of gold", () => {
    // 0.10 lot of 100 ounces at 2,400.00 is 24,000 USD, over a leverage of 30.
    const margin = estimatedMargin({ lots: 0.1, price: 2_400, digits: 2, contractSize: 100, leverage: 30, pointValuePerLot: 1, baseIsAccountCurrency: false });
    expect(margin).toBeCloseTo(800, 6);
  });

  it("converts crosses through the quote currency's rate", () => {
    // EURGBP at 0.85500 with GBPUSD at 1.27000: a point is worth 1.27 USD per lot, and EUR is 1.08585 USD.
    const margin = estimatedMargin({ lots: 1, price: 0.855, digits: 5, contractSize: 100_000, leverage: 100, pointValuePerLot: 1.27, baseIsAccountCurrency: false });
    expect(margin).toBeCloseTo(1_085.85, 6);
  });
});

describe("pipValue", () => {
  it("is ten points at the volume", () => {
    expect(pipValue(1, 5, 1)).toBeCloseTo(10, 9);
    expect(pipValue(0.5, 3, 0.666)).toBeCloseTo(3.33, 9);
  });

  it("is a tenth of a dollar per ounce for gold", () => {
    // 0.10 lot of 100 ounces moving 0.1 is 1 USD.
    expect(pipValue(0.1, 2, 1)).toBeCloseTo(1, 9);
  });
});

describe("stopLossRisk", () => {
  it("is the loss at the stop loss", () => {
    expect(stopLossRisk("Buy", 1, 1.087, 1.086, 5, 1)).toBe(100);
    expect(stopLossRisk("Sell", 0.5, 1.087, 1.0875, 5, 1)).toBe(25);
  });

  it("is nothing when the stop loss is on the winning side", () => {
    expect(stopLossRisk("Sell", 1, 1.087, 1.086, 5, 1)).toBeNull();
    expect(stopLossRisk("Buy", 1, 1.087, 1.087, 5, 1)).toBeNull();
  });
});

describe("nearestLimit", () => {
  it("is the limit with the least room left", () => {
    expect(nearestLimit([floor("max-loss", 10_000), floor("daily", 4_500)])?.floorId).toBe("daily");
    expect(nearestLimit([floor("daily", 5_000), floor("max-loss", 1_200)])?.floorId).toBe("max-loss");
  });

  it("is nothing without limits or with only broken ones", () => {
    expect(nearestLimit([])).toBeNull();
    expect(nearestLimit([floor("daily", 0), floor("max-loss", -20)])).toBeNull();
  });
});

describe("shareOfRoom and shareRisk", () => {
  it("measures the loss against the room left", () => {
    expect(shareOfRoom(18, floor("daily", 4_500))).toBeCloseTo(0.4, 9);
    expect(shareRisk(0.4)).toBe("ok");
    expect(shareRisk(50)).toBe("warning");
    expect(shareRisk(100)).toBe("danger");
  });
});

describe("formatShare", () => {
  it.each([
    [0.4, "0.4%"],
    [0.04, "under 0.1%"],
    [0, "0.0%"],
    [9.96, "10.0%"],
    [35.4, "35%"],
    [250, "250%"],
  ])("writes %j as %j", (percent, expected) => {
    expect(formatShare(percent)).toBe(expected);
  });
});

describe("riskText", () => {
  it("says what the stop loss risks and how much of the room it takes", () => {
    expect(riskText(18, "USD", floor("daily", 4_500))).toBe("Risks 18.00 USD, 0.4% of today's room");
    expect(riskText(1_500, "USD", floor("max-loss", 3_000))).toBe("Risks 1,500.00 USD, 50% of the room to the max loss limit");
  });

  it("says only the amount without a limit", () => {
    expect(riskText(18, "EUR", null)).toBe("Risks 18.00 EUR");
  });
});
