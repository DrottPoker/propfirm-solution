import type { InstrumentInfo } from "./api/types";
import type { DigitsOf } from "./events";

/** The watchlist group of an instrument, set by the platform. */
export type Category = InstrumentInfo["category"];

const categoryOrder: readonly Category[] = ["Forex", "Metals", "Indices", "Commodities", "Crypto"];

/** The symbol the terminal opens on: EURUSD, the most traded pair, when the group has it, otherwise the first. */
export const preferredSymbol = "EURUSD";

export function initialInstrument<T extends { symbol: string }>(instruments: readonly T[]): T | null {
  return instruments.find((i) => i.symbol === preferredSymbol) ?? instruments[0] ?? null;
}

export function categoryOf(instrument: InstrumentInfo): Category {
  return instrument.category;
}

/** The categories that have instruments, in a fixed order. */
export function categoriesOf(instruments: readonly InstrumentInfo[]): Category[] {
  const present = new Set(instruments.map(categoryOf));
  return categoryOrder.filter((c) => present.has(c));
}

/** Price decimals by symbol, for the instruments the terminal knows. Unknown symbols get 5. */
export function digitsLookup(instruments: readonly InstrumentInfo[]): DigitsOf {
  const bySymbol = new Map(instruments.map((i) => [i.symbol, i.digits]));
  return (symbol) => bySymbol.get(symbol) ?? 5;
}

/** The spread in points, the smallest price step of the instrument. */
export function spreadPoints(bid: number, ask: number, digits: number): number {
  return Math.round((ask - bid) * 10 ** digits);
}

const currencySigns: Record<string, string> = {
  USD: "$",
  EUR: "€",
  GBP: "£",
  JPY: "¥",
  CHF: "Fr",
  AUD: "A$",
  CAD: "C$",
  NZD: "N$",
  XAU: "Au",
  XAG: "Ag",
  XPT: "Pt",
  XPD: "Pd",
  BTC: "₿",
  ETH: "Ξ",
};

/** A short sign for a currency or metal, for example € for EUR and Au for gold. */
export function currencySign(currency: string): string {
  return currencySigns[currency] ?? currency.slice(0, 1);
}
