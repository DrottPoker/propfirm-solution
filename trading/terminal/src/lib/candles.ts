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
};

export const timeframes = Object.keys(barSeconds) as Timeframe[];

/** Bar start in UTC seconds, the same rule as the service uses. */
export function barStart(timeSeconds: number, timeframe: Timeframe): number {
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
