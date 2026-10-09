import { create } from "zustand";

import { kronant } from "./colors";
import { onSettingsLoaded, readSetting, writeSetting } from "./syncedSettings";
import { themeKey } from "./themeKey";

/**
 * Choices the trader turns on or off in Settings (ADR 0055): what the chart shows, the sounds, and whether an order
 * asks before it is sent.
 */
const toggles = {
  chartVolume: { key: "trading.chartVolume", fallback: true },
  chartGrid: { key: "trading.chartGrid", fallback: true },
  chartAsk: { key: "trading.chartAsk", fallback: false },
  chartTrades: { key: "trading.chartTrades", fallback: true },
  // The watchlist's prices turn green or red for a moment when they move (ADR 0058).
  priceTicks: { key: "trading.priceTicks", fallback: true },
  watchlistSpread: { key: "trading.watchlistSpread", fallback: false },
  watchlistChart: { key: "trading.watchlistChart", fallback: true },
  fillSound: { key: "trading.fillSound", fallback: false },
  closeSound: { key: "trading.closeSound", fallback: false },
  warningSound: { key: "trading.warningSound", fallback: true },
  alertSound: { key: "trading.alertSound", fallback: true },
  confirmOrders: { key: "trading.confirmOrders", fallback: false },
  // Buy and sell buttons on the chart itself, for trading from the chart and in full screen (ADR 0058).
  chartTrading: { key: "trading.chartTrading", fallback: false },
  // A second press before a position is closed from the table.
  confirmCloses: { key: "trading.confirmCloses", fallback: false },
  // The computer's own notifications for fills, closes, alerts and warnings while the terminal is in the background.
  notifications: { key: "trading.notifications", fallback: false },
} as const;

export type Setting = keyof typeof toggles;

const soundVolumeKey = "trading.soundVolume";
const chartTypeKey = "trading.chartType";
const timeZoneKey = "trading.timeZone";

/** The terminal's look: dark, light, or as the computer is set (ADR 0058). */
export type ThemeChoice = "dark" | "light" | "system";

export const themeChoices: readonly { value: ThemeChoice; name: string }[] = [
  { value: "dark", name: "Dark" },
  { value: "light", name: "Light" },
  { value: "system", name: "As the computer" },
];

/** Reads a stored theme. Anything else gives the dark theme, Kronant's own. */
export function parseTheme(raw: string | null): ThemeChoice {
  return themeChoices.some((t) => t.value === raw) ? (raw as ThemeChoice) : "dark";
}

/**
 * Which time zone times are shown in (ADR 0058): the account's, which its trading day and the firm's portal follow, the
 * computer's, or UTC.
 */
export type TimeZoneChoice = "account" | "computer" | "utc";

export const timeZoneChoices: readonly { value: TimeZoneChoice; name: string }[] = [
  { value: "account", name: "The account's" },
  { value: "computer", name: "This computer's" },
  { value: "utc", name: "UTC" },
];

export function parseTimeZoneChoice(raw: string | null): TimeZoneChoice {
  return timeZoneChoices.some((t) => t.value === raw) ? (raw as TimeZoneChoice) : "account";
}

/** How the chart draws the prices: candles, OHLC bars, a line of closes, or Heikin Ashi candles. */
export type ChartType = "candles" | "bars" | "line" | "heikinAshi";

export const chartTypes: readonly { type: ChartType; name: string }[] = [
  { type: "candles", name: "Candles" },
  { type: "bars", name: "Bars" },
  { type: "line", name: "Line" },
  { type: "heikinAshi", name: "Heikin Ashi" },
];

/** Reads a stored chart type. Anything else gives candles. */
export function parseChartType(raw: string | null): ChartType {
  return chartTypes.some((t) => t.type === raw) ? (raw as ChartType) : "candles";
}
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

type Values = Record<Setting, boolean> & {
  soundVolume: number;
  candles: CandleColors;
  chartType: ChartType;
  theme: ThemeChoice;
  timeZone: TimeZoneChoice;
  /** Whether the trader chose to be asked before orders, or left it to the firm's profile (ADR 0058). */
  confirmOrdersChosen: boolean;
};

function loadAll(): Values {
  const loaded = {} as Record<Setting, boolean>;
  for (const [setting, { key, fallback }] of Object.entries(toggles) as [Setting, (typeof toggles)[Setting]][]) {
    loaded[setting] = parseSetting(readSetting(key), fallback);
  }

  return {
    ...loaded,
    soundVolume: parseSoundVolume(readSetting(soundVolumeKey)),
    chartType: parseChartType(readSetting(chartTypeKey)),
    theme: parseTheme(readSetting(themeKey)),
    timeZone: parseTimeZoneChoice(readSetting(timeZoneKey)),
    confirmOrdersChosen: readSetting(toggles.confirmOrders.key) !== null,
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
  changeChartType: (type: ChartType) => void;
  changeTheme: (theme: ThemeChoice) => void;
  changeTimeZone: (timeZone: TimeZoneChoice) => void;
  /** Every choice back to its default, on every device. */
  reset: () => void;
}

/** The trader's choices, kept in the browser and on their login (ADR 0052). */
export const useSettings = create<SettingsState>()((set) => ({
  ...loadAll(),
  change: (setting, on) => {
    writeSetting(toggles[setting].key, on ? "on" : "off");
    set({ [setting]: on, ...(setting === "confirmOrders" ? { confirmOrdersChosen: true } : {}) } as Pick<SettingsState, Setting>);
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
  changeChartType: (chartType) => {
    writeSetting(chartTypeKey, chartType);
    set({ chartType });
  },
  changeTheme: (theme) => {
    writeSetting(themeKey, theme);
    set({ theme });
  },
  changeTimeZone: (timeZone) => {
    writeSetting(timeZoneKey, timeZone);
    set({ timeZone });
  },
  reset: () => {
    [...Object.values(toggles).map((t) => t.key), soundVolumeKey, chartTypeKey, themeKey, timeZoneKey, candleKeys.up, candleKeys.down].forEach((key) =>
      writeSetting(key, null),
    );
    set(loadAll());
  },
}));

onSettingsLoaded(() => useSettings.setState(loadAll()));
