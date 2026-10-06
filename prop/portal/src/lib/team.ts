import { formatDate } from "./format";

// The reader's calendar day of a moment, as a number of days that subtracts without daylight saving getting in the way.
function localDay(time: number): number {
  const date = new Date(time);
  return Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()) / 86_400_000;
}

/**
 * When an administrator last logged in, for the Team page, counted in the reader's calendar days: "Logged in today",
 * "Last logged in yesterday", "Last logged in 3 days ago", the date after a month, or "Never logged in".
 */
export function lastLoginText(iso: string | null, now: number): string {
  if (iso === null) {
    return "Never logged in";
  }

  const days = localDay(now) - localDay(Date.parse(iso));
  if (days <= 0) {
    return "Logged in today";
  }

  if (days === 1) {
    return "Last logged in yesterday";
  }

  return days < 30 ? `Last logged in ${days} days ago` : `Last logged in on ${formatDate(iso)}`;
}
