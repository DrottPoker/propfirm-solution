import { create } from "zustand";

import type { AccountSnapshot, EventEnvelope, SymbolPrice } from "./api/types";

export type ConnectionState = "connecting" | "connected" | "reconnecting" | "disconnected";

/** The latest events the store keeps. Also the most the service returns in one call, so one call can fill the store. */
export const maxEvents = 1_000;

interface TradingState {
  connection: ConnectionState;
  account: AccountSnapshot | null;
  prices: Record<string, SymbolPrice>;
  /** Oldest first, unique by sequence number. */
  events: EventEnvelope[];
  setConnection: (connection: ConnectionState) => void;
  setAccount: (account: AccountSnapshot) => void;
  applyPrices: (prices: SymbolPrice[]) => void;
  addEvents: (events: readonly EventEnvelope[]) => void;
  reset: () => void;
}

/** Live state pushed by the trading service. Values are shown as received and never recalculated. */
export const useTradingStore = create<TradingState>()((set) => ({
  connection: "connecting",
  account: null,
  prices: {},
  events: [],
  setConnection: (connection) => set({ connection }),
  setAccount: (account) => set({ account }),
  applyPrices: (prices) =>
    set((state) => {
      const next = { ...state.prices };
      for (const price of prices) {
        next[price.symbol] = price;
      }
      return { prices: next };
    }),
  addEvents: (events) => set((state) => ({ events: mergeEvents(state.events, events) })),
  reset: () => set({ connection: "connecting", account: null, prices: {}, events: [] }),
}));

/** Merges events from the REST API and realtime, which can overlap or arrive out of order. */
export function mergeEvents(current: EventEnvelope[], incoming: readonly EventEnvelope[]): EventEnvelope[] {
  if (incoming.length === 0) {
    return current;
  }

  const bySequence = new Map(current.map((e) => [e.sequence, e]));
  for (const envelope of incoming) {
    bySequence.set(envelope.sequence, envelope);
  }

  return [...bySequence.values()].sort((a, b) => a.sequence - b.sequence).slice(-maxEvents);
}
