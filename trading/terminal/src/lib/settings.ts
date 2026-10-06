import { create } from "zustand";

/** Choices the trader turns on or off on this device: the volume under the chart, and a sound when an order fills. */
const settings = {
  chartVolume: { key: "trading.chartVolume", fallback: true },
  fillSound: { key: "trading.fillSound", fallback: false },
} as const;

export type Setting = keyof typeof settings;

/** Reads a stored choice. Anything but "on" or "off" gives the default. */
export function parseSetting(raw: string | null, fallback: boolean): boolean {
  return raw === "on" ? true : raw === "off" ? false : fallback;
}

function load(setting: Setting): boolean {
  const { key, fallback } = settings[setting];
  try {
    return typeof window === "undefined" ? fallback : parseSetting(window.localStorage.getItem(key), fallback);
  } catch {
    return fallback;
  }
}

interface SettingsState extends Record<Setting, boolean> {
  change: (setting: Setting, on: boolean) => void;
}

/** The trader's choices, kept in the browser when it allows storage. */
export const useSettings = create<SettingsState>()((set) => ({
  chartVolume: load("chartVolume"),
  fillSound: load("fillSound"),
  change: (setting, on) => {
    try {
      window.localStorage.setItem(settings[setting].key, on ? "on" : "off");
    } catch {
      // Private windows may refuse storage. The choice then lasts until the page is closed.
    }
    set({ [setting]: on } as Pick<SettingsState, Setting>);
  },
}));
