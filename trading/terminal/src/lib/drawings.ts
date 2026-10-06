import { readSetting, writeSetting } from "./syncedSettings";

// The trader's drawings on the chart: horizontal lines, trend lines and rectangles. They are kept per symbol in the
// browser and on the trader's login (ADR 0052), at times and prices, so they stay where they were on every timeframe.

/** A place on the chart. Time is in UTC seconds. */
export interface ChartPoint {
  time: number;
  price: number;
}

export type Drawing =
  | { id: string; kind: "horizontal"; price: number }
  | { id: string; kind: "trend" | "rectangle"; from: ChartPoint; to: ChartPoint };

export type DrawingTool = Drawing["kind"];

/** The part of a drawing the pointer is on: an end to move on its own, or the drawing as a whole. */
export type DrawingPart = "from" | "to" | "body";

/** Drawings beyond this many on a symbol are not kept, so storage stays small. */
export const maxDrawings = 100;

const storagePrefix = "trading.drawings.";

const isPoint = (p: unknown): p is ChartPoint =>
  typeof p === "object" && p !== null && Number.isFinite((p as ChartPoint).time) && Number.isFinite((p as ChartPoint).price);

/** Reads stored drawings, leaving out anything that is not one. */
export function parseDrawings(raw: string | null): Drawing[] {
  if (!raw) {
    return [];
  }

  try {
    const value: unknown = JSON.parse(raw);
    if (!Array.isArray(value)) {
      return [];
    }

    return value
      .filter((d): d is Drawing => {
        if (typeof d !== "object" || d === null || typeof d.id !== "string") {
          return false;
        }

        return d.kind === "horizontal" ? Number.isFinite(d.price) : (d.kind === "trend" || d.kind === "rectangle") && isPoint(d.from) && isPoint(d.to);
      })
      .slice(0, maxDrawings);
  } catch {
    return [];
  }
}

export function loadDrawings(symbol: string): Drawing[] {
  return parseDrawings(readSetting(storagePrefix + symbol));
}

export function saveDrawings(symbol: string, drawings: readonly Drawing[]) {
  writeSetting(storagePrefix + symbol, drawings.length === 0 ? null : JSON.stringify(drawings.slice(-maxDrawings)));
}

/** A point on the screen, in pixels. */
export interface Pixel {
  x: number;
  y: number;
}

/** The shortest distance from the point to the segment between a and b. */
export function distanceToSegment(p: Pixel, a: Pixel, b: Pixel): number {
  const dx = b.x - a.x;
  const dy = b.y - a.y;
  const lengthSquared = dx * dx + dy * dy;
  const t = lengthSquared === 0 ? 0 : Math.max(0, Math.min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / lengthSquared));
  return Math.hypot(p.x - (a.x + t * dx), p.y - (a.y + t * dy));
}

/** A drawing on the screen: a horizontal line's height, or the two corners of a trend line or rectangle. */
export type DrawingPixels = { kind: "horizontal"; y: number } | { kind: "trend" | "rectangle"; from: Pixel; to: Pixel };

/** The part of the drawing within reach of the point, or null. Ends come before the body, so they can be grabbed. */
export function hitDrawing(drawing: DrawingPixels, p: Pixel, reach: number): DrawingPart | null {
  if (drawing.kind === "horizontal") {
    return Math.abs(p.y - drawing.y) <= reach ? "body" : null;
  }

  const { from, to } = drawing;
  if (Math.hypot(p.x - from.x, p.y - from.y) <= reach) {
    return "from";
  }

  if (Math.hypot(p.x - to.x, p.y - to.y) <= reach) {
    return "to";
  }

  if (drawing.kind === "trend") {
    return distanceToSegment(p, from, to) <= reach ? "body" : null;
  }

  // A rectangle is grabbed on its edges or inside it.
  const left = Math.min(from.x, to.x) - reach;
  const right = Math.max(from.x, to.x) + reach;
  const top = Math.min(from.y, to.y) - reach;
  const bottom = Math.max(from.y, to.y) + reach;
  return p.x >= left && p.x <= right && p.y >= top && p.y <= bottom ? "body" : null;
}

/** The drawing with the part moved: an end to the point, or the whole drawing by the time and price it moved. */
export function moveDrawing(drawing: Drawing, part: DrawingPart, start: ChartPoint, now: ChartPoint): Drawing {
  if (drawing.kind === "horizontal") {
    return { ...drawing, price: drawing.price + now.price - start.price };
  }

  if (part === "from") {
    return { ...drawing, from: now };
  }

  if (part === "to") {
    return { ...drawing, to: now };
  }

  const dt = now.time - start.time;
  const dp = now.price - start.price;
  return {
    ...drawing,
    from: { time: drawing.from.time + dt, price: drawing.from.price + dp },
    to: { time: drawing.to.time + dt, price: drawing.to.price + dp },
  };
}

/**
 * Where a time falls among the bars, counted in bars from the first: whole numbers at bar starts, fractions between
 * them, and past either end at the timeframe's bar length. The chart lays bars out by this count, not by time, so a
 * weekend takes no room.
 */
export function logicalOfTime(times: readonly number[], time: number, barSeconds: number): number {
  const last = times.length - 1;
  if (last < 0) {
    return 0;
  }

  if (time <= times[0]) {
    return (time - times[0]) / barSeconds;
  }

  if (time >= times[last]) {
    return last + (time - times[last]) / barSeconds;
  }

  // The last bar that starts at or before the time.
  let low = 0;
  let high = last;
  while (low < high) {
    const middle = Math.ceil((low + high) / 2);
    if (times[middle] <= time) {
      low = middle;
    } else {
      high = middle - 1;
    }
  }

  // Within a bar's own length the time is in that bar. In a gap after it, such as a weekend, it is at the bar's end.
  return low + Math.min(1, (time - times[low]) / barSeconds);
}

/** The time at a place among the bars, the opposite of logicalOfTime. */
export function timeOfLogical(times: readonly number[], logical: number, barSeconds: number): number {
  const last = times.length - 1;
  if (last < 0) {
    return 0;
  }

  if (logical <= 0) {
    return times[0] + logical * barSeconds;
  }

  if (logical >= last) {
    return times[last] + (logical - last) * barSeconds;
  }

  const index = Math.floor(logical);
  return times[index] + (logical - index) * barSeconds;
}
