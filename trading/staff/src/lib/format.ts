// Display formatting only. Every figure comes from the trading service. Times are in UTC, the same for every member
// of staff wherever they are.

const whole = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });
const money = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const signedMoney = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2, signDisplay: "exceptZero" });
const signedWhole = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0, signDisplay: "exceptZero" });
const lots = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const signedLots = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2, signDisplay: "exceptZero" });
const oneDecimal = new Intl.NumberFormat("en-US", { maximumFractionDigits: 1 });

/** A count, for example 2,431. */
export function formatCount(value: number): string {
  return whole.format(value);
}

/** A count with its noun, for example "1 account" or "2,431 accounts". */
export function formatCountOf(value: number, noun: string, nouns = `${noun}s`): string {
  return `${whole.format(value)} ${value === 1 ? noun : nouns}`;
}

/** An amount with cents, for example 94,982.10. */
export function formatMoney(value: number): string {
  return money.format(value);
}

/** An amount with its sign, for example +412.00. */
export function formatSignedMoney(value: number): string {
  return signedMoney.format(value);
}

/** A whole amount with its sign, for example +184,210. */
export function formatSignedWhole(value: number): string {
  return signedWhole.format(Math.round(value));
}

/** Lots, for example 121.20. */
export function formatLots(value: number): string {
  return lots.format(value);
}

/** Lots with their sign, for example -84.50. */
export function formatSignedLots(value: number): string {
  return signedLots.format(value);
}

/** A large amount in millions or thousands, for example 32.1 M or 410 K, and below that in full. */
export function formatLarge(value: number): string {
  const size = Math.abs(value);
  if (size >= 1_000_000_000) {
    return `${oneDecimal.format(value / 1_000_000_000)} B`;
  }

  if (size >= 1_000_000) {
    return `${oneDecimal.format(value / 1_000_000)} M`;
  }

  if (size >= 10_000) {
    return `${whole.format(value / 1_000)} K`;
  }

  return whole.format(value);
}

/** A rate such as prices per second, with one decimal when it is small. */
export function formatRate(value: number): string {
  return value >= 100 ? whole.format(value) : oneDecimal.format(value);
}

/** Milliseconds, for example 3 ms or 0.4 ms. */
export function formatMilliseconds(value: number): string {
  return `${value >= 10 ? whole.format(value) : oneDecimal.format(value)} ms`;
}

/** Bytes in the largest whole unit, for example 41 GB. */
export function formatBytes(value: number): string {
  const units = ["bytes", "KB", "MB", "GB", "TB"];
  let size = value;
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit++;
  }

  return `${size >= 10 || unit === 0 ? whole.format(size) : oneDecimal.format(size)} ${units[unit]}`;
}

const utcTime = new Intl.DateTimeFormat("en-GB", { timeZone: "UTC", hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23" });
const utcClock = new Intl.DateTimeFormat("en-GB", { timeZone: "UTC", hour: "2-digit", minute: "2-digit", hourCycle: "h23" });
const utcDate = new Intl.DateTimeFormat("en-GB", { timeZone: "UTC", day: "numeric", month: "short", year: "numeric" });
const utcShortDate = new Intl.DateTimeFormat("en-GB", { timeZone: "UTC", day: "numeric", month: "short" });
const utcDay = new Intl.DateTimeFormat("en-GB", { timeZone: "UTC", weekday: "long", day: "numeric", month: "long" });
const utcWeekday = new Intl.DateTimeFormat("en-GB", { timeZone: "UTC", weekday: "short" });

/** The time of day with seconds, for example 14:28:11. */
export function formatTime(value: string | Date): string {
  return utcTime.format(new Date(value));
}

/** The time of day, for example 14:28. */
export function formatClock(value: string | Date): string {
  return utcClock.format(new Date(value));
}

/** A date, for example 2 Oct 2026. */
export function formatDate(value: string | Date): string {
  return utcDate.format(new Date(value));
}

/** A day of the week with its date, for example Thursday 8 October. */
export function formatDay(value: string | Date): string {
  return utcDay.format(new Date(value));
}

/**
 * When something happened, as short as it can be said: the time today, "Yesterday 14:52" or "7 Oct 09:40", with the year
 * only when it is another year.
 */
export function formatWhen(value: string | Date, now: Date = new Date()): string {
  const at = new Date(value);
  const day = (d: Date) => Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate());
  const days = Math.round((day(now) - day(at)) / 86_400_000);
  if (days === 0) {
    return formatClock(at);
  }

  if (days === 1) {
    return `Yesterday ${formatClock(at)}`;
  }

  if (days > 1 && days < 7) {
    return `${utcWeekday.format(at)} ${formatClock(at)}`;
  }

  return at.getUTCFullYear() === now.getUTCFullYear() ? `${utcShortDate.format(at)} ${formatClock(at)}` : formatDate(at);
}

/** How long ago, for example "0.3 s ago", "4 min ago", "2 h ago" or "3 days ago". */
export function formatAgo(value: string | Date, now: Date = new Date()): string {
  const seconds = Math.max(0, (now.getTime() - new Date(value).getTime()) / 1000);
  if (seconds < 10) {
    return `${oneDecimal.format(seconds)} s ago`;
  }

  return `${formatDuration(seconds)} ago`;
}

/** A length of time, for example 45 s, 4 min 21 s, 2 h 5 min or 3 days. */
export function formatDuration(totalSeconds: number): string {
  const seconds = Math.round(totalSeconds);
  if (seconds < 60) {
    return `${seconds} s`;
  }

  if (seconds < 600) {
    const rest = seconds % 60;
    return rest === 0 ? `${Math.floor(seconds / 60)} min` : `${Math.floor(seconds / 60)} min ${rest} s`;
  }

  if (seconds < 3600) {
    return `${Math.round(seconds / 60)} min`;
  }

  if (seconds < 86_400) {
    const minutes = Math.round((seconds % 3600) / 60);
    return minutes === 0 ? `${Math.floor(seconds / 3600)} h` : `${Math.floor(seconds / 3600)} h ${minutes} min`;
  }

  const days = Math.floor(seconds / 86_400);
  return days === 1 ? "1 day" : `${days} days`;
}

const priceFormats = new Map<number, Intl.NumberFormat>();

/** A price with the symbol's decimals, grouped like 2,661.40, or a dash without one. */
export function formatPrice(value: number | null | undefined, digits: number): string {
  if (value == null) {
    return "-";
  }

  let format = priceFormats.get(digits);
  if (!format) {
    format = new Intl.NumberFormat("en-US", { minimumFractionDigits: digits, maximumFractionDigits: digits });
    priceFormats.set(digits, format);
  }

  return format.format(value);
}

const feedNames: Record<string, string> = {
  CapitalCom: "Capital.com",
  Tiingo: "Tiingo",
  Synthetic: "Made-up prices",
};

/** The name of a price feed as people know it, for example Capital.com. */
export function feedName(feed: string): string {
  return feedNames[feed] ?? feed;
}

/** The feed's name within a sentence, where made-up prices are no name. */
export function feedInText(feed: string): string {
  return feed === "Synthetic" ? "made-up prices" : feedName(feed);
}
