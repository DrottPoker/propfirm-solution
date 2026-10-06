import { readSetting, writeSetting } from "./syncedSettings";

const storageKey = "trading.favorites";

/** Symbols the trader starred, kept in the browser and on their login (ADR 0052). */
export function loadFavorites(): string[] {
  return parseFavorites(readSetting(storageKey));
}

export function saveFavorites(symbols: readonly string[]) {
  writeSetting(storageKey, JSON.stringify(symbols));
}

/** Reads stored favorites, ignoring anything that is not a list of symbols. */
export function parseFavorites(raw: string | null): string[] {
  if (!raw) {
    return [];
  }

  try {
    const value: unknown = JSON.parse(raw);
    return Array.isArray(value) ? value.filter((s): s is string => typeof s === "string") : [];
  } catch {
    return [];
  }
}

export function toggleFavorite(symbols: readonly string[], symbol: string): string[] {
  return symbols.includes(symbol) ? symbols.filter((s) => s !== symbol) : [...symbols, symbol];
}
