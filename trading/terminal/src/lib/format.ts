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

export function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString("en-GB", { hour12: false });
}

/** Date and time in UTC, for example 2026-10-05 14:12:34. */
export function formatUtc(date: Date): string {
  return date.toISOString().slice(0, 19).replace("T", " ");
}
