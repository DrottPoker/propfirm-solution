import { describe, expect, it } from "vitest";

import { applyPrice, barStart, toBar } from "./candles";

// 2026-10-05 08:00:00 UTC
const eight = Date.UTC(2026, 9, 5, 8, 0, 0) / 1000;

describe("barStart", () => {
  it("floors to the timeframe in UTC", () => {
    expect(barStart(eight + 59, "M1")).toBe(eight);
    expect(barStart(eight + 14 * 60 + 59, "M15")).toBe(eight);
    expect(barStart(eight + 3 * 3600, "H4")).toBe(eight);
    expect(barStart(eight, "D1")).toBe(Date.UTC(2026, 9, 5) / 1000);
  });
});

describe("applyPrice", () => {
  const bar = { time: eight, open: 1.1, high: 1.2, low: 1.0, close: 1.15, ticks: 4 };

  it("updates the latest bar within its time", () => {
    expect(applyPrice(bar, 1.25, eight + 30, "M1")).toEqual({ ...bar, high: 1.25, close: 1.25, ticks: 5 });
    expect(applyPrice(bar, 0.95, eight + 30, "M1")).toEqual({ ...bar, low: 0.95, close: 0.95, ticks: 5 });
  });

  it("starts a new bar when the time has passed", () => {
    expect(applyPrice(bar, 1.3, eight + 61, "M1")).toEqual({ time: eight + 60, open: 1.3, high: 1.3, low: 1.3, close: 1.3, ticks: 1 });
  });

  it("starts the first bar when there is none", () => {
    expect(applyPrice(undefined, 1.3, eight + 5, "M5")).toEqual({ time: eight, open: 1.3, high: 1.3, low: 1.3, close: 1.3, ticks: 1 });
  });

  it("ignores prices older than the latest bar", () => {
    expect(applyPrice(bar, 1.3, eight - 1, "M1")).toBeNull();
  });
});

describe("toBar", () => {
  it("converts the candle time to UTC seconds", () => {
    const candle = { time: "2026-10-05T08:00:00+00:00", open: 1, high: 2, low: 0.5, close: 1.5, tickCount: 3 };
    expect(toBar(candle)).toEqual({ time: eight, open: 1, high: 2, low: 0.5, close: 1.5, ticks: 3 });
  });
});
