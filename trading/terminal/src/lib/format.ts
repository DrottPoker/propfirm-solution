// Display formatting only. The terminal never calculates money: amounts come from the engine.

const money = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export function formatMoney(value: number): string {
  return money.format(value);
}

export function formatPrice(value: number | null | undefined, digits: number): string {
  return value == null ? "-" : value.toFixed(digits);
}

export function formatVolume(value: number): string {
  return value.toFixed(2);
}

export function formatPercent(value: number | null | undefined): string {
  return value == null ? "-" : `${value.toFixed(2)}%`;
}

export function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString("en-GB", { hour12: false });
}
