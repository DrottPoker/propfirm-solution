import { describe, expect, it } from "vitest";

import { clampPeriod, indicatorLabel, maxIndicators, parseIndicators } from "./chartStudies";

describe("parseIndicators", () => {
  it("keeps indicators and leaves out anything else", () => {
    const raw = JSON.stringify([
      { id: "a", kind: "SMA", period: 20 },
      { id: "b", kind: "RSI", period: 1 },
      { id: "c", kind: "Ichimoku", period: 9 },
      { id: "d", kind: "EMA", period: 50.5 },
      { id: "e", kind: "MACD", period: 12 },
    ]);

    expect(parseIndicators(raw).map((i) => i.id)).toEqual(["a", "e"]);
    expect(parseIndicators("[1, 2]")).toEqual([]);
    expect(parseIndicators("{")).toEqual([]);
  });

  it("keeps at most the limit", () => {
    const many = Array.from({ length: maxIndicators + 3 }, (_, i) => ({ id: `${i}`, kind: "SMA", period: 10 }));

    expect(parseIndicators(JSON.stringify(many))).toHaveLength(maxIndicators);
  });
});

describe("indicatorLabel", () => {
  it("names the indicator with its periods", () => {
    expect(indicatorLabel({ id: "a", kind: "EMA", period: 50 })).toBe("EMA 50");
    expect(indicatorLabel({ id: "b", kind: "Bollinger", period: 20 })).toBe("BB 20, 2");
    expect(indicatorLabel({ id: "c", kind: "MACD", period: 12 })).toBe("MACD 12, 26, 9");
  });
});

describe("clampPeriod", () => {
  it("keeps a typed period within the limits", () => {
    expect(clampPeriod("50")).toBe(50);
    expect(clampPeriod("1")).toBe(2);
    expect(clampPeriod("9999")).toBe(500);
    expect(clampPeriod("")).toBeNull();
  });
});
