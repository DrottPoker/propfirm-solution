import type { InstrumentInfo } from "./api/types";
import { parseVolume } from "./orderInput";

const storageKey = "trading.volumes";

/** The volume an order ticket starts with when the trader has not chosen one for the symbol. */
export const defaultVolume = "1.00";

/** The volume the trader last chose for each symbol on this device, if the browser allows storage. */
export function loadVolumes(): Record<string, string> {
  try {
    return typeof window === "undefined" ? {} : parseVolumes(window.localStorage.getItem(storageKey));
  } catch {
    return {};
  }
}

/** Remembers the volume for the symbol, so the next ticket for it starts there. */
export function rememberVolume(symbol: string, volume: string) {
  try {
    window.localStorage.setItem(storageKey, JSON.stringify({ ...loadVolumes(), [symbol]: volume }));
  } catch {
    // Private windows may refuse storage. The volume is only a convenience.
  }
}

/** Reads stored volumes, ignoring anything that is not a symbol with a volume as text. */
export function parseVolumes(raw: string | null): Record<string, string> {
  if (!raw) {
    return {};
  }

  try {
    const value: unknown = JSON.parse(raw);
    if (typeof value !== "object" || value === null || Array.isArray(value)) {
      return {};
    }

    return Object.fromEntries(Object.entries(value).filter((entry): entry is [string, string] => typeof entry[1] === "string"));
  } catch {
    return {};
  }
}

/**
 * The volume a ticket for the instrument starts with: the one the trader last chose for it, as long as the
 * instrument still allows it, and otherwise the default.
 */
export function startVolume(instrument: InstrumentInfo, remembered: Record<string, string>): string {
  const volume = remembered[instrument.symbol];
  return volume !== undefined && parseVolume(volume, instrument).ok ? volume : defaultVolume;
}
