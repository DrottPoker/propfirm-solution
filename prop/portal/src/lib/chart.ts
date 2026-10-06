// Geometry for the balance chart. The chart draws Prop.Api's figures; nothing here changes an amount.

/** Clean values for an axis between min and max, such as 96,000 / 98,000 / 100,000. */
export function niceTicks(min: number, max: number, count = 5): number[] {
  if (!Number.isFinite(min) || !Number.isFinite(max) || max <= min) {
    return Number.isFinite(min) ? [min] : [];
  }

  const step = niceStep((max - min) / Math.max(count - 1, 1));
  const ticks: number[] = [];
  for (let value = Math.ceil(min / step) * step; value <= max + step / 1e9; value += step) {
    ticks.push(Number(value.toFixed(10)));
  }

  return ticks;
}

function niceStep(rough: number): number {
  const power = 10 ** Math.floor(Math.log10(rough));
  const fraction = rough / power;
  const nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 2.5 ? 2.5 : fraction <= 5 ? 5 : 10;
  return nice * power;
}

/**
 * The value range that holds every value, widened a little so lines never touch the edges, and at least
 * <paramref name="minSpan"/> wide so a flat line sits in the middle instead of filling the chart.
 */
export function valueDomain(values: number[], minSpan: number): [number, number] {
  const finite = values.filter(Number.isFinite);
  if (finite.length === 0) {
    return [0, 1];
  }

  let low = Math.min(...finite);
  let high = Math.max(...finite);
  if (high - low < minSpan) {
    const middle = (low + high) / 2;
    low = middle - minSpan / 2;
    high = middle + minSpan / 2;
  }

  const padding = (high - low) * 0.08;
  return [low - padding, high + padding];
}

export type Scale = (value: number) => number;

/** A linear scale from a domain onto a range of pixels. */
export function linearScale([d0, d1]: [number, number], [r0, r1]: [number, number]): Scale {
  const span = d1 - d0 || 1;
  return (value) => r0 + ((value - d0) / span) * (r1 - r0);
}

/**
 * An SVG path that holds each value until the next one, as a balance does between its changes, and then holds
 * the last value until <paramref name="endX"/>.
 */
export function stepPath(points: { x: number; y: number }[], endX: number): string {
  if (points.length === 0) {
    return "";
  }

  const [first, ...rest] = points;
  let path = `M${round(first.x)} ${round(first.y)}`;
  for (const point of rest) {
    path += `H${round(point.x)}V${round(point.y)}`;
  }

  return `${path}H${round(Math.max(endX, points[points.length - 1].x))}`;
}

/** The index of the time closest to <paramref name="time"/> in times sorted from oldest. -1 when there are none. */
export function nearestIndex(times: number[], time: number): number {
  if (times.length === 0) {
    return -1;
  }

  let low = 0;
  let high = times.length - 1;
  while (low < high) {
    const middle = (low + high) >> 1;
    if (times[middle] < time) {
      low = middle + 1;
    } else {
      high = middle;
    }
  }

  return low > 0 && time - times[low - 1] <= times[low] - time ? low - 1 : low;
}

/** The level in force at <paramref name="time"/>: the last one set at or before it, from levels sorted from oldest. */
export function levelAt(levels: { time: number; level: number }[], time: number): number | null {
  let found: number | null = null;
  for (const level of levels) {
    if (level.time > time) {
      break;
    }

    found = level.level;
  }

  return found;
}

/** Times for the axis: evenly spread from start to end, at most <paramref name="count"/>. */
export function timeTicks(start: number, end: number, count: number): number[] {
  if (end <= start || count < 2) {
    return [start];
  }

  return Array.from({ length: count }, (_, i) => start + ((end - start) * i) / (count - 1));
}

function round(value: number): number {
  return Math.round(value * 10) / 10;
}

/** A line in the balance chart besides the balance: the profit target or one of the loss limits. */
export type ChartLine = "daily" | "target" | "maxLoss";

/** Where a line is: in the chart, or above or below it with only its name at the edge. */
export type LinePlace = "shown" | "above" | "below";

/** How much taller than the balance's own range the chart may grow to hold a line, before the line is named instead. */
const lineRoom = 3;

/**
 * The value range of the balance chart. It follows the balance and equity now, so a small move can be seen. The daily
 * loss limit, the profit target and the max loss limit are held too, in that order, each only while it keeps the range
 * within three times the balance's own. A line left out is named at the chart's top or bottom edge instead, and comes
 * into the chart as the balance gets near it.
 */
export function balanceDomain(input: {
  balances: number[];
  equity: number | null;
  target: number | null;
  daily: number[];
  maxLoss: number[];
  minSpan: number;
}): { domain: [number, number]; places: Record<ChartLine, LinePlace> } {
  const held = [...input.balances, ...(input.equity == null ? [] : [input.equity])];
  const own = valueDomain(held, input.minSpan);
  const limit = (own[1] - own[0]) * lineRoom;
  const places: Record<ChartLine, LinePlace> = { daily: "shown", target: "shown", maxLoss: "shown" };
  const lines: [ChartLine, number[]][] = [
    ["daily", input.daily],
    ["target", input.target == null ? [] : [input.target]],
    ["maxLoss", input.maxLoss],
  ];
  for (const [line, levels] of lines) {
    if (levels.length === 0) {
      continue;
    }

    const wider = valueDomain([...held, ...levels], input.minSpan);
    if (wider[1] - wider[0] <= limit) {
      held.push(...levels);
    } else {
      places[line] = Math.min(...levels) > own[1] ? "above" : "below";
    }
  }

  return { domain: valueDomain(held, input.minSpan), places };
}

const minute = 60_000;
const hour = 60 * minute;
const day = 24 * hour;

/**
 * Times for the axis with their labels, at most <paramref name="count"/>. The label shows as much as the range
 * needs: seconds within ten minutes, the time of day within 20 hours, the day and time within four days, and
 * otherwise the day. A label that would repeat the one before it is left out.
 */
export function timeAxis(start: number, end: number, count: number, timeZone?: string): { time: number; label: string }[] {
  const span = end - start;
  const options: Intl.DateTimeFormatOptions =
    span < 10 * minute
      ? { hour: "2-digit", minute: "2-digit", second: "2-digit" }
      : span < 20 * hour
        ? { hour: "2-digit", minute: "2-digit" }
        : span < 4 * day
          ? { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" }
          : { day: "numeric", month: "short" };
  const format = new Intl.DateTimeFormat("en-GB", { ...options, timeZone });
  const ticks: { time: number; label: string }[] = [];
  for (const time of timeTicks(start, end, count)) {
    const label = format.format(time);
    if (ticks.at(-1)?.label !== label) {
      ticks.push({ time, label });
    }
  }

  return ticks;
}
