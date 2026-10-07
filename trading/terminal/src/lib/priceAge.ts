import type { MarketHours } from "./api/types";

// How fresh the prices are (ADR 0053). Ages are counted from when the terminal received each price, by its own clock,
// so a computer whose clock is wrong does not make prices look old. Closed markets have no prices to wait for.

/** Without a new price for any open market for this long, the price feed counts as stopped. */
export const feedStoppedAfterMs = 60_000;

/** A price this old is shown with its age in the watchlist and the chart. */
export const oldPriceAfterMs = 60_000;

function isOpen(symbol: string, markets: readonly MarketHours[] | undefined): boolean {
  return markets?.find((m) => m.symbol === symbol)?.isOpen ?? true;
}

/** How long ago the symbol's last price came, or null without one. */
export function priceAgeMs(symbol: string, receivedAt: Readonly<Record<string, number>>, now: number): number | null {
  const at = receivedAt[symbol];
  return at === undefined ? null : Math.max(0, now - at);
}

/**
 * Whether orders for the symbol would be refused for an old price: its market is open and its last price is older than
 * the service allows.
 */
export function priceTooOld(
  symbol: string,
  receivedAt: Readonly<Record<string, number>>,
  markets: readonly MarketHours[] | undefined,
  maxAgeMs: number,
  now: number,
): boolean {
  const age = priceAgeMs(symbol, receivedAt, now);
  return age !== null && age > maxAgeMs && isOpen(symbol, markets);
}

/** Whether to show the symbol's price as old: its market is open and nothing has come for a minute. */
export function priceIsOld(symbol: string, receivedAt: Readonly<Record<string, number>>, markets: readonly MarketHours[] | undefined, now: number): boolean {
  return priceTooOld(symbol, receivedAt, markets, oldPriceAfterMs, now);
}

/**
 * How long ago the last price came for any open market, when that is long enough to mean the feed has stopped. Null
 * while prices come, before any has, and while every market is closed.
 */
export function feedStoppedFor(receivedAt: Readonly<Record<string, number>>, markets: readonly MarketHours[] | undefined, now: number): number | null {
  const open = Object.entries(receivedAt).filter(([symbol]) => isOpen(symbol, markets));
  if (open.length === 0) {
    return null;
  }

  const newest = Math.max(...open.map(([, at]) => at));
  return now - newest >= feedStoppedAfterMs ? now - newest : null;
}

/** An age for people: "45 s", "4 min", "2 h 10 min". Rounded down. */
export function ageText(ms: number): string {
  const seconds = Math.floor(ms / 1000);
  if (seconds < 60) {
    return `${seconds} s`;
  }

  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return `${minutes} min`;
  }

  const rest = minutes % 60;
  return rest === 0 ? `${Math.floor(minutes / 60)} h` : `${Math.floor(minutes / 60)} h ${rest} min`;
}
