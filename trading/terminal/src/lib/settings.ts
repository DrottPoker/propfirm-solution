import { create } from "zustand";

import { kronant } from "./colors";
import { onSettingsLoaded, readSetting, writeSetting } from "./syncedSettings";

/**
 * Choices the trader turns on or off in Settings (ADR 0055): what the chart shows, the sounds, and whether an order
 * asks before it is sent.
 */
const toggles = {
  chartVolume: { key: "trading.chartVolume", fallback: true },
  chartGrid: { key: "trading.chartGrid", fallback: true },
  chartAsk: { key: "trading.chartAsk", fallback: false },
  chartTrades: { key: "trading.chartTrades", fallback: true },
  fillSound: { key: "trading.fillSound", fallback: false },
  closeSound: { key: "trading.closeSound", fallback: false },
  warningSound: { key: "trading.warningSound", fallback: true },
  confirmOrders: { key: "trading.confirmOrders", fallback: false },
} as const;

export type Setting = keyof typeof toggles;

const soundVolumeKey = "trading.soundVolume";
const candleKeys = { up: "trading.candleUp", down: "trading.candleDown" } as const;

/** How loud the sounds are, from 0 to 100. */
export const defaultSoundVolume = 100;

/** The colors of rising and falling candles. */
export type CandleColors = { up: string; down: string };

export const defaultCandleColors: CandleColors = { up: kronant.profit, down: kronant.loss };

/** Ready-made candle colors, the first Kronant's own. Any other pair is the trader's own. */
export const candlePresets: readonly { name: string; colors: CandleColors }[] = [
  { name: "Green and red", colors: defaultCandleColors },
  { name: "Teal and red", colors: { up: "#26a69a", down: "#ef5350" } },
  { name: "Blue and orange", colors: { up: "#4c9be8", down: "#f0923a" } },
  { name: "Light and dark", colors: { up: "#ece7df", down: "#6b665e" } },
];

/** Reads a stored choice. Anything but "on" or "off" gives the default. */
export function parseSetting(raw: string | null, fallback: boolean): boolean {
  return raw === "on" ? true : raw === "off" ? false : fallback;
}

/** Reads a stored volume. Anything but a whole number from 0 to 100 gives the default. */
export function parseSoundVolume(raw: string | null): number {
  const value = raw !== null && /^\d{1,3}$/.test(raw) ? Number(raw) : NaN;
  return value >= 0 && value <= 100 ? value : defaultSoundVolume;
}

/** Reads a stored color as #rrggbb in lower case. Anything else gives the default. */
export function parseColor(raw: string | null, fallback: string): string {
  return raw !== null && /^#[0-9a-fA-F]{6}$/.test(raw) ? raw.toLowerCase() : fallback;
}

/** The ready-made colors the pair is, or null for the trader's own. */
export function presetOf(colors: CandleColors): string | null {
  return candlePresets.find((p) => p.colors.up === colors.up && p.colors.down === colors.down)?.name ?? null;
}

type Values = Record<Setting, boolean> & { soundVolume: number; candles: CandleColors };

function loadAll(): Values {
  const loaded = {} as Record<Setting, boolean>;
  for (const [setting, { key, fallback }] of Object.entries(toggles) as [Setting, (typeof toggles)[Setting]][]) {
    loaded[setting] = parseSetting(readSetting(key), fallback);
  }

  return {
    ...loaded,
    soundVolume: parseSoundVolume(readSetting(soundVolumeKey)),
    candles: {
      up: parseColor(readSetting(candleKeys.up), defaultCandleColors.up),
      down: parseColor(readSetting(candleKeys.down), defaultCandleColors.down),
    },
  };
}

interface SettingsState extends Values {
  change: (setting: Setting, on: boolean) => void;
  changeSoundVolume: (volume: number) => void;
  changeCandles: (colors: CandleColors) => void;
  /** Every choice back to its default, on every device. */
  reset: () => void;
}

/** The trader's choices, kept in the browser and on their login (ADR 0052). */
export const useSettings = create<SettingsState>()((set) => ({
  ...loadAll(),
  change: (setting, on) => {
    writeSetting(toggles[setting].key, on ? "on" : "off");
    set({ [setting]: on } as Pick<SettingsState, Setting>);
  },
  changeSoundVolume: (volume) => {
    const value = Math.round(Math.min(100, Math.max(0, volume)));
    writeSetting(soundVolumeKey, String(value));
    set({ soundVolume: value });
  },
  changeCandles: (colors) => {
    const candles = { up: parseColor(colors.up, defaultCandleColors.up), down: parseColor(colors.down, defaultCandleColors.down) };
    writeSetting(candleKeys.up, candles.up);
    writeSetting(candleKeys.down, candles.down);
    set({ candles });
  },
  reset: () => {
    [...Object.values(toggles).map((t) => t.key), soundVolumeKey, candleKeys.up, candleKeys.down].forEach((key) => writeSetting(key, null));
    set(loadAll());
  },
}));

onSettingsLoaded(() => useSettings.setState(loadAll()));
