import { create } from "zustand";

import { onSettingsLoaded, readSetting, writeSetting } from "./syncedSettings";

// Price alerts (ADR 0058): the trader names a price, and the terminal says so once the bid reaches it, with a note, a
// sound and, in the background, the computer's own notification. They are kept in the browser and on the login (ADR
// 0052), and watched while the terminal is open; each one goes once it has fired.

export interface PriceAlert {
  id: string;
  symbol: string;
  price: number;
  /** Which way the bid must move to reach the price: up to it from below, or down to it from above. */
  direction: "up" | "down";
  createdAt: string;
}

/** More would be hard to keep track of, and storage stays small. */
export const maxAlerts = 50;

const storageKey = "trading.alerts";

/** The way the bid must move from where it is now to reach the price. */
export function directionTo(price: number, bid: number): PriceAlert["direction"] {
  return price >= bid ? "up" : "down";
}

/** Whether the bid has reached the alert's price, coming from the side it was set on. */
export function reached(alert: PriceAlert, bid: number): boolean {
  return alert.direction === "up" ? bid >= alert.price : bid <= alert.price;
}

/** What the alert waits for, for example "EURUSD bid rises to 1.12300". */
export function alertText(alert: PriceAlert, digits: number): string {
  return `${alert.symbol} bid ${alert.direction === "up" ? "rises" : "falls"} to ${alert.price.toFixed(digits)}`;
}

/** Reads stored alerts, leaving out anything that is not one. */
export function parseAlerts(raw: string | null): PriceAlert[] {
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
        (a): a is PriceAlert =>
          typeof a === "object" &&
          a !== null &&
          typeof a.id === "string" &&
          typeof a.symbol === "string" &&
          Number.isFinite(a.price) &&
          a.price > 0 &&
          (a.direction === "up" || a.direction === "down") &&
          typeof a.createdAt === "string",
      )
      .map(({ id, symbol, price, direction, createdAt }) => ({ id, symbol, price, direction, createdAt }))
      .slice(0, maxAlerts);
  } catch {
    return [];
  }
}

interface AlertsState {
  alerts: PriceAlert[];
  /** Adds an alert at the price, set from where the bid is now. False when there are already as many as kept. */
  add: (symbol: string, price: number, bid: number) => boolean;
  remove: (id: string) => void;
}

const save = (alerts: readonly PriceAlert[]) => writeSetting(storageKey, alerts.length === 0 ? null : JSON.stringify(alerts));

export const useAlerts = create<AlertsState>()((set, get) => ({
  alerts: parseAlerts(readSetting(storageKey)),
  add: (symbol, price, bid) => {
    const { alerts } = get();
    if (alerts.length >= maxAlerts) {
      return false;
    }

    const next = [...alerts, { id: crypto.randomUUID(), symbol, price, direction: directionTo(price, bid), createdAt: new Date().toISOString() }];
    save(next);
    set({ alerts: next });
    return true;
  },
  remove: (id) => {
    const next = get().alerts.filter((a) => a.id !== id);
    save(next);
    set({ alerts: next });
  },
}));

onSettingsLoaded(() => useAlerts.setState({ alerts: parseAlerts(readSetting(storageKey)) }));
