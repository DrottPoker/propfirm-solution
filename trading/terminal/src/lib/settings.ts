import { create } from "zustand";

import { onSettingsLoaded, readSetting, writeSetting } from "./syncedSettings";

/**
 * Choices the trader turns on or off: the volume under the chart, a sound when an order fills and a sound with the
 * warnings about the account's rules.
 */
const settings = {
  chartVolume: { key: "trading.chartVolume", fallback: true },
  fillSound: { key: "trading.fillSound", fallback: false },
  warningSound: { key: "trading.warningSound", fallback: true },
} as const;

export type Setting = keyof typeof settings;

/** Reads a stored choice. Anything but "on" or "off" gives the default. */
export function parseSetting(raw: string | null, fallback: boolean): boolean {
  return raw === "on" ? true : raw === "off" ? false : fallback;
}

function load(setting: Setting): boolean {
  const { key, fallback } = settings[setting];
  return parseSetting(readSetting(key), fallback);
}

function loadAll(): Record<Setting, boolean> {
  return { chartVolume: load("chartVolume"), fillSound: load("fillSound"), warningSound: load("warningSound") };
}

interface SettingsState extends Record<Setting, boolean> {
  change: (setting: Setting, on: boolean) => void;
}

/** The trader's choices, kept in the browser and on their login (ADR 0052). */
export const useSettings = create<SettingsState>()((set) => ({
  ...loadAll(),
  change: (setting, on) => {
    writeSetting(settings[setting].key, on ? "on" : "off");
    set({ [setting]: on } as Pick<SettingsState, Setting>);
  },
}));

onSettingsLoaded(() => useSettings.setState(loadAll()));
