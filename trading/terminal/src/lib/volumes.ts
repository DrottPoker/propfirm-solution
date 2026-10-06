import type { InstrumentInfo } from "./api/types";
import { parseVolume } from "./orderInput";
import { readSetting, writeSetting } from "./syncedSettings";

const storageKey = "trading.volumes";

/** The volume an order ticket starts with when the trader has not chosen one for the symbol. */
export const defaultVolume = "1.00";

/** The volume the trader last chose for each symbol, kept in the browser and on their login (ADR 0052). */
export function loadVolumes(): Record<string, string> {
  return parseVolumes(readSetting(storageKey));
}

/** Remembers the volume for the symbol, so the next ticket for it starts there. */
export function rememberVolume(symbol: string, volume: string) {
  const volumes = loadVolumes();
  if (volumes[symbol] !== volume) {
    writeSetting(storageKey, JSON.stringify({ ...volumes, [symbol]: volume }));
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
