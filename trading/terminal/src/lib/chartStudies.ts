import { create } from "zustand";

import { onSettingsLoaded, readSetting, writeSetting } from "./syncedSettings";

/** The indicators the trader can add to the chart. */
export type IndicatorKind = "SMA" | "EMA" | "Bollinger" | "RSI" | "MACD";

export interface Indicator {
  id: string;
  kind: IndicatorKind;
  /** How many candles it works over. MACD always uses 12, 26 and 9, so it has none. */
  period: number;
  /** Hidden from the chart for a while, kept with its period. */
  hidden?: boolean;
}

/** Each kind's name, the period it starts with, and whether it gets a pane of its own under the candles. */
export const indicatorKinds: Record<IndicatorKind, { name: string; period: number | null; ownPane: boolean }> = {
  SMA: { name: "Moving average", period: 20, ownPane: false },
  EMA: { name: "Exponential moving average", period: 20, ownPane: false },
  Bollinger: { name: "Bollinger Bands", period: 20, ownPane: false },
  RSI: { name: "RSI", period: 14, ownPane: true },
  MACD: { name: "MACD", period: null, ownPane: true },
};

export const indicatorOrder: readonly IndicatorKind[] = ["SMA", "EMA", "Bollinger", "RSI", "MACD"];

/** More would crowd the chart, and each RSI or MACD takes a pane. */
export const maxIndicators = 8;

export const periodLimits = { min: 2, max: 500 } as const;

/** Bollinger Bands are this many standard deviations wide on each side. */
export const bollingerWidth = 2;

/** MACD's fast and slow averages and its signal line. */
export const macdPeriods = { fast: 12, slow: 26, signal: 9 } as const;

/** A short name for the chart, for example "SMA 20", "BB 20, 2" or "MACD 12, 26, 9". */
export function indicatorLabel(indicator: Indicator): string {
  switch (indicator.kind) {
    case "Bollinger":
      return `BB ${indicator.period}, ${bollingerWidth}`;
    case "MACD":
      return `MACD ${macdPeriods.fast}, ${macdPeriods.slow}, ${macdPeriods.signal}`;
    default:
      return `${indicator.kind} ${indicator.period}`;
  }
}

/** A period the trader typed, kept within the limits. Not a number gives null. */
export function clampPeriod(text: string): number | null {
  const value = Number.parseInt(text, 10);
  return Number.isFinite(value) ? Math.min(periodLimits.max, Math.max(periodLimits.min, value)) : null;
}

const storageKey = "trading.indicators";

/** Reads stored indicators, leaving out anything that is not one. */
export function parseIndicators(raw: string | null): Indicator[] {
  if (!raw) {
    return [];
  }

  try {
    const value: unknown = JSON.parse(raw);
    if (!Array.isArray(value)) {
      return [];
    }

    return value
      .filter(
        (i): i is Indicator =>
          typeof i === "object" &&
          i !== null &&
          typeof i.id === "string" &&
          typeof i.kind === "string" &&
          i.kind in indicatorKinds &&
          Number.isInteger(i.period) &&
          i.period >= periodLimits.min &&
          i.period <= periodLimits.max,
      )
      .map((i) => (i.hidden === true ? { id: i.id, kind: i.kind, period: i.period, hidden: true } : { id: i.id, kind: i.kind, period: i.period }))
      .slice(0, maxIndicators);
  } catch {
    return [];
  }
}

function load(): Indicator[] {
  return parseIndicators(readSetting(storageKey));
}

function save(indicators: readonly Indicator[]) {
  writeSetting(storageKey, JSON.stringify(indicators));
}

interface ChartStudiesState {
  indicators: Indicator[];
  add: (kind: IndicatorKind) => void;
  remove: (id: string) => void;
  setPeriod: (id: string, period: number) => void;
  setHidden: (id: string, hidden: boolean) => void;
}

/** The indicators on the chart, the same for every symbol, kept in the browser and on the trader's login (ADR 0052). */
export const useChartStudies = create<ChartStudiesState>()((set) => {
  const change = (update: (indicators: Indicator[]) => Indicator[]) =>
    set((state) => {
      const indicators = update(state.indicators);
      save(indicators);
      return { indicators };
    });

  return {
    indicators: load(),
    add: (kind) =>
      change((indicators) =>
        indicators.length >= maxIndicators ? indicators : [...indicators, { id: crypto.randomUUID(), kind, period: indicatorKinds[kind].period ?? macdPeriods.fast }],
      ),
    remove: (id) => change((indicators) => indicators.filter((i) => i.id !== id)),
    setPeriod: (id, period) => change((indicators) => indicators.map((i) => (i.id === id ? { ...i, period } : i))),
    setHidden: (id, hidden) =>
      change((indicators) => indicators.map((i) => (i.id === id ? (hidden ? { ...i, hidden: true } : { id: i.id, kind: i.kind, period: i.period }) : i))),
  };
});

onSettingsLoaded(() => useChartStudies.setState({ indicators: load() }));
