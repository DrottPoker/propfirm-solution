import { create } from "zustand";

import type { AccountSnapshot, EventEnvelope, SymbolPrice } from "./api/types";

export type ConnectionState = "connecting" | "connected" | "reconnecting" | "disconnected";

/** Which way the bid moved on its last change. */
export type PriceMove = "up" | "down";

/** The latest events the store keeps. Also the most the service returns in one call, so one call can fill the store. */
export const maxEvents = 1_000;

interface TradingState {
  connection: ConnectionState;
  account: AccountSnapshot | null;
  prices: Record<string, SymbolPrice>;
  moves: Record<string, PriceMove>;
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
  moves: {},
  events: [],
  setConnection: (connection) => set({ connection }),
  setAccount: (account) => set({ account }),
  applyPrices: (prices) => set((state) => applyPrices(state, prices)),
  addEvents: (events) => set((state) => ({ events: mergeEvents(state.events, events) })),
  reset: () => set({ connection: "connecting", account: null, prices: {}, moves: {}, events: [] }),
}));

/** Stores the latest prices and remembers which way each bid moved. */
export function applyPrices(
  state: Pick<TradingState, "prices" | "moves">,
  prices: readonly SymbolPrice[],
): Pick<TradingState, "prices" | "moves"> {
  const next = { ...state.prices };
  const moves = { ...state.moves };
  for (const price of prices) {
    const previous = next[price.symbol];
    if (previous && price.bid !== previous.bid) {
      moves[price.symbol] = price.bid > previous.bid ? "up" : "down";
    }
    next[price.symbol] = price;
  }
  return { prices: next, moves };
}

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
