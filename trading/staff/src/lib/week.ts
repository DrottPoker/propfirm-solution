import type { MarketPeriod } from "./api/types";

/** Where a market is open within one day, as parts of the day from 0 to 1. */
export type DaySegment = { from: number; to: number };

const day = 86_400_000;

/**
 * When a market is open on each day of the week from `weekStart`, Sunday first, as parts of each day,
 * for the week's bar in the instrument list.
 */
export function weekSegments(weekStart: string, periods: MarketPeriod[]): DaySegment[][] {
  const start = new Date(weekStart).getTime();
  return Array.from({ length: 7 }, (_, index) => {
    const dayStart = start + index * day;
    const dayEnd = dayStart + day;
    return periods
      .map((period) => ({ opens: new Date(period.opens).getTime(), closes: new Date(period.closes).getTime() }))
      .filter((period) => period.opens < dayEnd && period.closes > dayStart)
      .map((period) => ({
        from: (Math.max(period.opens, dayStart) - dayStart) / day,
        to: (Math.min(period.closes, dayEnd) - dayStart) / day,
      }));
  });
}

/** In words, for screen readers: "Open all week", "Closed all week" or the days it is open, such as "Open Sunday to Friday". */
export function weekLabel(segments: DaySegment[][]): string {
  const names = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
  const open = segments.map((day) => day.length > 0);
  if (open.every(Boolean)) {
    return segments.every((day) => day.length === 1 && day[0].from === 0 && day[0].to === 1) ? "Open all week" : "Open every day, with breaks";
  }

  if (!open.some(Boolean)) {
    return "Closed all week";
  }

  const first = open.indexOf(true);
  const last = open.lastIndexOf(true);
  return first === last ? `Open ${names[first]}` : `Open ${names[first]} to ${names[last]}`;
}
