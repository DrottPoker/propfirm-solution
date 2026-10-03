// Display formatting only. The portal never calculates money: amounts come from Prop.Api and the trading platform.

const money = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export function formatMoney(value: number | null | undefined): string {
  return value == null ? "-" : money.format(value);
}

export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString("en-GB", { dateStyle: "medium", timeStyle: "short" });
}

/** A date, for example "4 Nov 2026". A plain date such as "2026-11-04" is shown as it is, in any time zone. */
export function formatDate(iso: string): string {
  return isPlainDate(iso)
    ? new Date(`${iso}T00:00:00Z`).toLocaleDateString("en-GB", { dateStyle: "medium", timeZone: "UTC" })
    : new Date(iso).toLocaleDateString("en-GB", { dateStyle: "medium" });
}

/** The day before a plain date, for example the last day before a deadline. */
export function dayBefore(isoDate: string): string {
  const date = new Date(`${isoDate}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() - 1);
  return date.toISOString().slice(0, 10);
}

function isPlainDate(iso: string): boolean {
  return /^\d{4}-\d{2}-\d{2}$/.test(iso);
}
