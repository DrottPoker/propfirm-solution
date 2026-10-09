import { useEffect, useState } from "react";

import type { MarketHours, MarketPeriod } from "./api/types";
import { formatDayTime, formatTime } from "./format";

// The market hours are loaded again a second after a market opens or closes, so the service sees the change too, and
// at least every hour.
const refreshAfterChangeMs = 1_000;
const maxRefreshMs = 60 * 60 * 1000;

/** How long until the market hours should be loaded again: just after the next market opens or closes. */
export function marketRefreshDelay(markets: readonly MarketHours[], now: number): number {
  const untilChange = Math.min(...markets.map((m) => (m.nextChange ? Date.parse(m.nextChange) - now : Infinity)));
  return Math.min(maxRefreshMs, Math.max(refreshAfterChangeMs, untilChange + refreshAfterChangeMs));
}

/** Whether the service said the market is closed. Hours not yet loaded count as open, since the engine decides anyway. */
export function isClosed(market: MarketHours | undefined): boolean {
  return market?.isOpen === false;
}

/** When a closed market opens, for example "Opens Mon 12 Oct 00:00 Stockholm time". */
export function opensText(market: MarketHours, timeZone: string, zoneName: string): string {
  return market.nextChange ? `Opens ${formatDayTime(market.nextChange, timeZone)} ${zoneName}` : "Closed until further notice";
}

// An open market warns this long before it closes, for example for positions over the weekend.
const closingWarningMs = 30 * 60 * 1000;

/** When an open market closes soon, the minutes left and the time it closes, for example 12 and 23:00. */
export function closingSoon(market: MarketHours | undefined, now: Date | null, timeZone: string): { minutes: number; at: string } | null {
  if (!market?.isOpen || !market.nextChange || !now) {
    return null;
  }

  const left = Date.parse(market.nextChange) - now.getTime();
  return left > 0 && left <= closingWarningMs ? { minutes: Math.ceil(left / 60_000), at: formatTime(market.nextChange, timeZone).slice(0, 5) } : null;
}

/**
 * The open periods as the trader reads them, in the time zone: "Mon 5 Oct 00:00 - 23:00" within a day, and
 * "Sun 11 Oct 23:00 - Fri 16 Oct 23:00" over several.
 */
export function sessionLines(sessions: readonly MarketPeriod[], timeZone: string): string[] {
  return sessions.map(({ opens, closes }) => {
    const from = formatDayTime(opens, timeZone);
    const to = formatDayTime(closes, timeZone);
    const sameDay = from.slice(0, -6) === to.slice(0, -6);
    return `${from} - ${sameDay ? to.slice(-5) : to}`;
  });
}

/**
 * Now, for telling which day a time was: from the first render, since what uses it is only shown in the browser, and
 * again every minute, so "Today" turns into "Yesterday" after midnight.
 */
export function useClock(): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 60_000);
    return () => clearInterval(timer);
  }, []);
  return now;
}

/** The time now, updated every period, or null before the page is shown, so the server and the browser render alike. */
export function useNow(periodMs: number): Date | null {
  const [now, setNow] = useState<Date | null>(null);

  useEffect(() => {
    const tick = () => setNow(new Date());
    const first = setTimeout(tick, 0);
    const timer = setInterval(tick, periodMs);
    return () => {
      clearTimeout(first);
      clearInterval(timer);
    };
  }, [periodMs]);

  return now;
}
