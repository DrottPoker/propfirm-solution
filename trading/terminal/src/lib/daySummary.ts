import type { Candle, Timeframe } from "./api/types";

const dayMs = 24 * 60 * 60 * 1000;

/** Candles used for the last 24 hours: 96 bars of 15 minutes, the bar before them and the current one. */
export const dayTimeframe: Timeframe = "M15";
export const dayCandleCount = 100;
const barMs = 15 * 60 * 1000;

/** The last 24 hours of a symbol's bid. Live prices are added when it is shown. */
export interface DaySummary {
  /** The bid 24 hours ago, or null when the service has fewer than 24 hours of prices. */
  reference: number | null;
  high: number | null;
  low: number | null;
  /** Closing bids over the last 24 hours, oldest first, for the sparkline. */
  closes: number[];
}

export function summarizeDay(candles: readonly Candle[], nowMs: number): DaySummary {
  const start = nowMs - dayMs;
  let reference: number | null = null;
  const recent: Candle[] = [];
  for (const candle of candles) {
    if (Date.parse(candle.time) + barMs <= start) {
      reference = candle.close;
    } else {
      recent.push(candle);
    }
  }

  return {
    reference,
    high: recent.length > 0 ? Math.max(...recent.map((c) => c.high)) : null,
    low: recent.length > 0 ? Math.min(...recent.map((c) => c.low)) : null,
    closes: recent.map((c) => c.close),
  };
}

export interface DayFigures {
  /** Bid change over 24 hours, null without a reference. */
  change: number | null;
  changePercent: number | null;
  high: number | null;
  low: number | null;
  /** Sparkline points ending with the live bid. */
  points: number[];
}

/** Combines the summary with the live bid, which can be a new high or low. */
export function dayFigures(summary: DaySummary | undefined, bid: number | undefined): DayFigures {
  const reference = summary?.reference ?? null;
  const highs = [summary?.high, bid].filter((v): v is number => v != null);
  const lows = [summary?.low, bid].filter((v): v is number => v != null);
  const change = reference !== null && bid !== undefined ? bid - reference : null;
  return {
    change,
    changePercent: change !== null && reference ? (change / reference) * 100 : null,
    high: highs.length > 0 ? Math.max(...highs) : null,
    low: lows.length > 0 ? Math.min(...lows) : null,
    points: bid !== undefined ? [...(summary?.closes ?? []), bid] : (summary?.closes ?? []),
  };
}
