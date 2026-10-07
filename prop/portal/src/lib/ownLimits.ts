import type { LockedDay, OwnLimitAmounts, OwnLock } from "./api/types";
import { formatClock, formatMoney } from "./format";

// The limits a trader sets for themselves in the terminal (ADR 0054), as the firm sees them in the admin panel.

/** Whether the trader has set any limit, now or from the next trading day. */
export function hasOwnLimits(limits: OwnLimitAmounts, pending: OwnLimitAmounts | null): boolean {
  return [limits, pending].some((l) => l !== null && (l.dailyLoss !== null || l.dailyTarget !== null || l.maxTrades !== null));
}

/** An amount limit, for example "1,500.00 USD", or "Off". */
export function amountLimitText(amount: number | null, currency: string): string {
  return amount === null ? "Off" : `${formatMoney(amount)} ${currency}`;
}

/** A count limit, for example "6", or "Off". */
export function countLimitText(count: number | null): string {
  return count === null ? "Off" : String(count);
}

/** What a loosened limit becomes when the next trading day starts, for example "2,000.00 USD from the next trading day", or null when it stays. */
export function pendingText(now: number | null, pending: OwnLimitAmounts | null, pick: (l: OwnLimitAmounts) => number | null, text: (v: number | null) => string): string | null {
  if (pending === null || pick(pending) === now) {
    return null;
  }

  return `${text(pick(pending))} from the next trading day`;
}

/** The lock in a few words, for example "Daily loss limit reached, locked until 00:00". */
export function lockText(lock: OwnLock, timeZone: string): string {
  const until = formatClock(lock.until, timeZone);
  switch (lock.reason) {
    case "DailyLoss":
      return `Daily loss limit reached, locked until ${until}`;
    case "DailyTarget":
      return `Profit target reached, locked until ${until}`;
    default:
      return `Locked by the trader until ${until}`;
  }
}

/** What locked a day, for example "Daily loss limit of 1,500.00 USD reached. 2 positions closed." */
export function lockedDayText(day: LockedDay, currency: string): string {
  const closed = day.positionsClosed === 1 ? "1 position closed." : `${day.positionsClosed} positions closed.`;
  switch (day.reason) {
    case "DailyLoss":
      return `Daily loss limit of ${amountLimitText(day.limit, currency)} reached. ${closed}`;
    case "DailyTarget":
      return `Daily profit target of ${amountLimitText(day.limit, currency)} reached. ${closed}`;
    default:
      return day.positionsClosed === 0 ? "The trader locked the rest of the day." : `The trader locked the rest of the day. ${closed}`;
  }
}
