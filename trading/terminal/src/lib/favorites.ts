const storageKey = "trading.favorites";

/** Symbols the trader starred on this device, if the browser allows storage. */
export function loadFavorites(): string[] {
  try {
    return typeof window === "undefined" ? [] : parseFavorites(window.localStorage.getItem(storageKey));
  } catch {
    return [];
  }
}

export function saveFavorites(symbols: readonly string[]) {
  try {
    window.localStorage.setItem(storageKey, JSON.stringify(symbols));
  } catch {
    // Private windows may refuse storage. Favorites are only a convenience.
  }
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
