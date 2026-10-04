// Display formatting only. The portal never calculates money: amounts come from Prop.Api and the trading platform.

const money = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const signedMoney = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2, signDisplay: "exceptZero" });

const signedPercent = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2, signDisplay: "exceptZero" });

const lots = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 8 });

// Prices keep the decimals the trading platform gave them, since they differ between instruments.
const price = new Intl.NumberFormat("en-US", { maximumFractionDigits: 10 });

const wholeNumber = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });

const ratio = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export function formatMoney(value: number | null | undefined): string {
  return value == null ? "-" : money.format(value);
}

/** A result with its sign, for example "+3,655.20" or "-1,769.60". Zero has none. */
export function formatSignedMoney(value: number | null | undefined): string {
  return value == null ? "-" : signedMoney.format(value);
}

/** A percentage with its sign, for example "+3.66%". */
export function formatSignedPercent(value: number | null | undefined): string {
  return value == null ? "-" : `${signedPercent.format(value)}%`;
}

/** A volume in lots, for example "0.20". */
export function formatLots(value: number): string {
  return lots.format(value);
}

export function formatPrice(value: number): string {
  return price.format(value);
}

/** A ratio such as a profit factor, for example "3.31". */
export function formatRatio(value: number): string {
  return ratio.format(value);
}

/** A level on a chart axis, for example "104,000". */
export function formatAxisMoney(value: number): string {
  return wholeNumber.format(value);
}

export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString("en-GB", { dateStyle: "medium", timeStyle: "short" });
}

/** A time in a table of trades, for example "2 Oct 13:40". */
export function formatShortDateTime(iso: string): string {
  return new Date(iso).toLocaleString("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });
}

/** A date, for example "4 Nov 2026". A plain date such as "2026-11-04" is shown as it is, in any time zone. */
export function formatDate(iso: string): string {
  return isPlainDate(iso)
    ? new Date(`${iso}T00:00:00Z`).toLocaleDateString("en-GB", { dateStyle: "medium", timeZone: "UTC" })
    : new Date(iso).toLocaleDateString("en-GB", { dateStyle: "medium" });
}

/** A trading day, for example "Mon 5 Oct". Trading days are plain dates, the same in every time zone. */
export function formatDay(isoDate: string): string {
  return new Date(`${isoDate}T00:00:00Z`).toLocaleDateString("en-GB", { weekday: "short", day: "numeric", month: "short", timeZone: "UTC" });
}

/** The day before a plain date, for example the last day before a deadline. */
export function dayBefore(isoDate: string): string {
  const date = new Date(`${isoDate}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() - 1);
  return date.toISOString().slice(0, 10);
}

/** Whole days from one plain date to another, negative when the second is earlier. */
export function daysBetween(fromIsoDate: string, toIsoDate: string): number {
  return Math.round((Date.parse(`${toIsoDate}T00:00:00Z`) - Date.parse(`${fromIsoDate}T00:00:00Z`)) / 86_400_000);
}

function isPlainDate(iso: string): boolean {
  return /^\d{4}-\d{2}-\d{2}$/.test(iso);
}
