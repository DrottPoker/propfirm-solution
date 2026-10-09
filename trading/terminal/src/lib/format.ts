// Display formatting only. The terminal never calculates money: amounts come from the engine.

const money = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const signedMoney = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2, signDisplay: "exceptZero" });
const units = new Intl.NumberFormat("en-US", { maximumFractionDigits: 2 });

export function formatMoney(value: number): string {
  return money.format(value);
}

/** Money with a plus sign for gains, for profit and loss. */
export function formatSignedMoney(value: number): string {
  return signedMoney.format(value);
}

export function formatPrice(value: number | null | undefined, digits: number): string {
  return value == null ? "-" : value.toFixed(digits);
}

/** A price change with its sign, for example +0.00312. */
export function formatSignedPrice(value: number, digits: number): string {
  const text = value.toFixed(digits);
  return Number(text) === 0 ? (0).toFixed(digits) : value > 0 ? `+${text}` : text;
}

export function formatVolume(value: number): string {
  return value.toFixed(2);
}

/** A quantity of a currency or metal, for example 100,000 for one lot of EURUSD. */
export function formatUnits(value: number): string {
  return units.format(value);
}

export function formatPercent(value: number | null | undefined): string {
  return value == null ? "-" : `${value.toFixed(2)}%`;
}

export function formatSignedPercent(value: number | null | undefined): string {
  return value == null ? "-" : `${formatSignedPrice(value, 2)}%`;
}

// Times are shown in the account's time zone, the one its trading day follows, so every time on screen agrees.
const timeFormats = new Map<string, Intl.DateTimeFormat>();

function partsIn(value: string | Date, timeZone: string): Record<string, string> {
  let format = timeFormats.get(timeZone);
  if (!format) {
    format = new Intl.DateTimeFormat("en-GB", {
      timeZone,
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      hourCycle: "h23",
    });
    timeFormats.set(timeZone, format);
  }

  return Object.fromEntries(format.formatToParts(new Date(value)).map((part) => [part.type, part.value]));
}

/** A time of day in the time zone, for example 17:38:05. */
export function formatTime(value: string | Date, timeZone: string): string {
  const p = partsIn(value, timeZone);
  return `${p.hour}:${p.minute}:${p.second}`;
}

/** A time of day to the minute in the time zone, for example 00:00. */
export function formatClock(value: string | Date, timeZone: string): string {
  const p = partsIn(value, timeZone);
  return `${p.hour}:${p.minute}`;
}

/** Date and time in the time zone, for example 2026-10-05 17:38:05. */
export function formatDateTime(value: string | Date, timeZone: string): string {
  const p = partsIn(value, timeZone);
  return `${p.year}-${p.month}-${p.day} ${p.hour}:${p.minute}:${p.second}`;
}

/** Date and time to the minute in the time zone, for example 2026-10-05 17:38. */
export function formatMinute(value: string | Date, timeZone: string): string {
  return formatDateTime(value, timeZone).slice(0, 16);
}

const monthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** What a chart tick mark shows. */
export type ChartTick = "year" | "month" | "day" | "time" | "seconds";

/** A chart tick mark at the time in UTC seconds, in the time zone: 2026, Oct, 5, 17:38 or 17:38:05. */
export function formatChartTick(seconds: number, tick: ChartTick, timeZone: string): string {
  const p = partsIn(new Date(seconds * 1000), timeZone);
  switch (tick) {
    case "year":
      return p.year;
    case "month":
      return monthNames[Number(p.month) - 1];
    case "day":
      return String(Number(p.day));
    case "time":
      return `${p.hour}:${p.minute}`;
    case "seconds":
      return `${p.hour}:${p.minute}:${p.second}`;
  }
}

const dayFormats = new Map<string, Intl.DateTimeFormat>();

/** A day and time to the minute in the time zone, for example Mon 12 Oct 00:00. */
export function formatDayTime(value: string | Date, timeZone: string): string {
  let format = dayFormats.get(timeZone);
  if (!format) {
    format = new Intl.DateTimeFormat("en-GB", { timeZone, weekday: "short", day: "numeric", month: "short" });
    dayFormats.set(timeZone, format);
  }

  const day = Object.fromEntries(format.formatToParts(new Date(value)).map((part) => [part.type, part.value]));
  const p = partsIn(value, timeZone);
  return `${day.weekday} ${day.day} ${day.month} ${p.hour}:${p.minute}`;
}

/** A day and time to the millisecond in the time zone, for example Tue 6 Oct 14:02:11.284, as in a trade's details. */
export function formatMoment(value: string | Date, timeZone: string): string {
  const day = formatDayTime(value, timeZone).replace(/ \d\d:\d\d$/, "");
  const milliseconds = String(new Date(value).getUTCMilliseconds()).padStart(3, "0");
  return `${day} ${formatTime(value, timeZone)}.${milliseconds}`;
}

/** The calendar day of the time in the time zone, as 2026-10-05, for telling days apart. */
export function dayKey(value: string | Date, timeZone: string): string {
  const p = partsIn(value, timeZone);
  return `${p.year}-${p.month}-${p.day}`;
}

// Whole days between two calendar days, counted on their dates so a change of summer time does not matter.
function daysBetween(from: string, to: string): number {
  return Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000);
}

const weekdayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const fullMonthNames = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

/**
 * The day of the time as a heading over the rows of that day: "Today", "Yesterday", or the weekday and date, for
 * example "Friday 3 October", with the year when it is another year than now.
 */
export function dayHeading(value: string | Date, timeZone: string, now: Date): string {
  const day = dayKey(value, timeZone);
  const today = dayKey(now, timeZone);
  const ago = daysBetween(day, today);
  if (ago === 0) {
    return "Today";
  }

  if (ago === 1) {
    return "Yesterday";
  }

  const [year, month, date] = day.split("-").map(Number);
  const weekday = weekdayNames[new Date(Date.UTC(year, month - 1, date)).getUTCDay()];
  return `${weekday} ${date} ${fullMonthNames[month - 1]}${day.slice(0, 4) === today.slice(0, 4) ? "" : ` ${year}`}`;
}

/**
 * A time with its day, short, when it is not today: "06:37", "Yesterday 21:17" or "3 Oct 06:50", and "3 Oct 2025
 * 06:50" in another year. With seconds, "06:37:12".
 */
export function formatWhen(value: string | Date, timeZone: string, now: Date, seconds = false): string {
  const time = seconds ? formatTime(value, timeZone) : formatClock(value, timeZone);
  const day = dayKey(value, timeZone);
  const today = dayKey(now, timeZone);
  const ago = daysBetween(day, today);
  if (ago === 0) {
    return time;
  }

  if (ago === 1) {
    return `Yesterday ${time}`;
  }

  const [year, month, date] = day.split("-").map(Number);
  return `${date} ${monthNames[month - 1]}${day.slice(0, 4) === today.slice(0, 4) ? "" : ` ${year}`} ${time}`;
}

/** When something happened, to the second, in a sentence: "today at 06:50:57", "yesterday at 21:17:00" or "on 3 Oct at 06:50:57". */
export function formatAt(value: string | Date, timeZone: string, now: Date): string {
  const when = formatWhen(value, timeZone, now, true);
  const time = formatTime(value, timeZone);
  return when === time ? `today at ${time}` : when.startsWith("Yesterday ") ? `yesterday at ${time}` : `on ${when.slice(0, -time.length - 1)} at ${time}`;
}

/** Rows grouped by the day they happened in the time zone, in the order given, each group under its heading. */
export function byDay<T>(rows: readonly T[], timeOf: (row: T) => string, timeZone: string, now: Date): { day: string; heading: string; rows: T[] }[] {
  const groups: { day: string; heading: string; rows: T[] }[] = [];
  for (const row of rows) {
    const day = dayKey(timeOf(row), timeZone);
    const last = groups.at(-1);
    if (last?.day === day) {
      last.rows.push(row);
    } else {
      groups.push({ day, heading: dayHeading(timeOf(row), timeZone, now), rows: [row] });
    }
  }

  return groups;
}

/** The time zone's name for the trader: "Stockholm time" for Europe/Stockholm, "UTC" for UTC. */
export function timeZoneName(timeZone: string): string {
  if (timeZone === "UTC" || timeZone === "Etc/UTC") {
    return "UTC";
  }

  const city = timeZone.split("/").pop() ?? timeZone;
  return `${city.replace(/_/g, " ")} time`;
}

/** The time zone when the browser knows it, otherwise UTC, so a zone the browser lacks never breaks the page. */
export function usableTimeZone(timeZone: string | null | undefined): string {
  if (!timeZone) {
    return "UTC";
  }

  try {
    new Intl.DateTimeFormat("en-GB", { timeZone });
    return timeZone;
  } catch {
    return "UTC";
  }
}
