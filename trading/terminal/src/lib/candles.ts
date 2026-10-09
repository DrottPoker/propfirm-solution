import type { Candle, Timeframe } from "./api/types";

/** A chart bar. Time is the bar start in UTC seconds. Ticks is the number of prices in the bar, the tick volume. */
export interface Bar {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
  ticks: number;
}

const barSeconds: Record<Timeframe, number> = {
  M1: 60,
  M5: 300,
  M15: 900,
  M30: 1800,
  H1: 3600,
  H4: 14_400,
  D1: 86_400,
  W1: 604_800,
  // A month varies; this is its length for placing drawings between bars, never for where a bar starts.
  MN: 2_592_000,
};

export const timeframes = Object.keys(barSeconds) as Timeframe[];

/** How long a bar of the timeframe is, in seconds. A month counts as 30 days. */
export function secondsOf(timeframe: Timeframe): number {
  return barSeconds[timeframe];
}

/** Older bars put in front of the ones on the chart. Bars at or after the chart's first are left out. */
export function mergeOlder(older: readonly Bar[], bars: readonly Bar[]): Bar[] {
  const first = bars[0]?.time ?? Infinity;
  return [...older.filter((b) => b.time < first), ...bars];
}

const daySeconds = 86_400;

/** Bar start in UTC seconds, the same rule as the service uses: Monday 00:00 for a week, the first 00:00 for a month. */
export function barStart(timeSeconds: number, timeframe: Timeframe): number {
  if (timeframe === "W1") {
    const day = timeSeconds - (timeSeconds % daySeconds);
    // 1 January 1970 was a Thursday, three days after a Monday.
    const sinceMonday = (Math.floor(day / daySeconds) + 3) % 7;
    return day - sinceMonday * daySeconds;
  }

  if (timeframe === "MN") {
    const date = new Date(timeSeconds * 1000);
    return Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1) / 1000;
  }

  return timeSeconds - (timeSeconds % barSeconds[timeframe]);
}

export function toBar(candle: Candle): Bar {
  return {
    time: Date.parse(candle.time) / 1000,
    open: candle.open,
    high: candle.high,
    low: candle.low,
    close: candle.close,
    ticks: candle.tickCount,
  };
}

/**
 * Applies a live bid to the latest bar: updates it, or starts a new one when the bar time has passed.
 * Returns null for prices older than the latest bar. The terminal gets at most one price per symbol every
 * 100 ms, so the live bar can count fewer ticks than the service until the candles are loaded again.
 */
export function applyPrice(last: Bar | undefined, bid: number, timeSeconds: number, timeframe: Timeframe): Bar | null {
  const start = barStart(Math.floor(timeSeconds), timeframe);
  if (last === undefined || start > last.time) {
    return { time: start, open: bid, high: bid, low: bid, close: bid, ticks: 1 };
  }

  if (start < last.time) {
    return null;
  }

  return { ...last, high: Math.max(last.high, bid), low: Math.min(last.low, bid), close: bid, ticks: last.ticks + 1 };
}

/**
 * Heikin Ashi bars from the bars: each closes at the average of its bar's four prices and opens halfway between the
 * previous Heikin Ashi bar's open and close, which smooths the trend. Only for drawing; prices stay the bars' own.
 */
export function heikinAshi(bars: readonly Bar[]): Bar[] {
  const result: Bar[] = [];
  for (const bar of bars) {
    const previous = result.at(-1);
    const close = (bar.open + bar.high + bar.low + bar.close) / 4;
    const open = previous ? (previous.open + previous.close) / 2 : (bar.open + bar.close) / 2;
    result.push({ ...bar, open, close, high: Math.max(bar.high, open, close), low: Math.min(bar.low, open, close) });
  }

  return result;
}
