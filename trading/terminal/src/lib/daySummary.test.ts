import { describe, expect, it } from "vitest";

import type { Candle } from "./api/types";
import { dayFigures, summarizeDay } from "./daySummary";

// 2026-10-05 12:07 UTC
const now = Date.UTC(2026, 9, 5, 12, 7);
const minutes = (n: number) => n * 60 * 1000;

function candle(startMs: number, close: number, high = close, low = close): Candle {
  return { time: new Date(startMs).toISOString(), open: close, high, low, close, tickCount: 1 };
}

describe("summarizeDay", () => {
  it("takes the close of the last bar that ended 24 hours ago as the reference", () => {
    const dayAgo = now - minutes(24 * 60);
    const candles = [
      candle(dayAgo - minutes(37), 1.1),
      // Ends 2 minutes before the window starts.
      candle(dayAgo - minutes(22), 1.2),
      // Ends inside the window.
      candle(dayAgo - minutes(7), 1.3, 1.35, 1.25),
      candle(now - minutes(7), 1.4, 1.45, 1.38),
    ];

    expect(summarizeDay(candles, now)).toEqual({ reference: 1.2, high: 1.45, low: 1.25, closes: [1.3, 1.4] });
  });

  it("has no reference with fewer than 24 hours of prices", () => {
    const summary = summarizeDay([candle(now - minutes(37), 1.1), candle(now - minutes(7), 1.2)], now);

    expect(summary.reference).toBeNull();
    expect(summary.closes).toEqual([1.1, 1.2]);
  });

  it("has no high or low without candles", () => {
    expect(summarizeDay([], now)).toEqual({ reference: null, high: null, low: null, closes: [] });
  });
});

describe("dayFigures", () => {
  const summary = { reference: 1.2, high: 1.3, low: 1.1, closes: [1.15, 1.25] };

  it("measures the change from the reference to the live bid", () => {
    const figures = dayFigures(summary, 1.26);

    expect(figures.change).toBeCloseTo(0.06, 10);
    expect(figures.changePercent).toBeCloseTo(5, 10);
    expect(figures.points).toEqual([1.15, 1.25, 1.26]);
  });

  it("lets the live bid set a new high or low", () => {
    expect(dayFigures(summary, 1.35).high).toBe(1.35);
    expect(dayFigures(summary, 1.05).low).toBe(1.05);
  });

  it("has no change without a reference or a bid", () => {
    expect(dayFigures({ ...summary, reference: null }, 1.26).change).toBeNull();
    expect(dayFigures(summary, undefined).change).toBeNull();
    expect(dayFigures(undefined, undefined)).toEqual({ change: null, changePercent: null, high: null, low: null, points: [] });
  });
});
