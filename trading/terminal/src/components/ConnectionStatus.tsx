"use client";

import { useTradingStore, type ConnectionState } from "@/lib/store";

import { useFeedStopped } from "./PriceAlerts";

/** Whether prices stream in: live, on the way, or offline. */
export type ConnectionStatus = { label: string; tone: "live" | "waiting" | "offline" };

// In the status bar beside the firm's server, so "Live" reads as the connection and not as a real money account.
const statuses: Record<ConnectionState, ConnectionStatus> = {
  connecting: { label: "Connecting", tone: "waiting" },
  connected: { label: "Live", tone: "live" },
  reconnecting: { label: "Reconnecting", tone: "waiting" },
  disconnected: { label: "Offline", tone: "offline" },
};

const tones: Record<ConnectionStatus["tone"], { dot: string; text: string }> = {
  live: { dot: "animate-pulse-dot bg-profit text-profit/50", text: "text-foreground" },
  waiting: { dot: "bg-warning", text: "text-warning" },
  offline: { dot: "bg-loss", text: "text-loss" },
};

/** Connected without prices for a minute is paused: the price feed has stopped, which the connection itself cannot show. */
export function useConnectionStatus(accountId: string): ConnectionStatus {
  const paused = useFeedStopped(accountId) !== null;
  const state = useTradingStore((s) => s.connection);
  return paused ? { label: "Prices paused", tone: "waiting" } : statuses[state];
}

/** The connection as a dot and a word: green and pulsing while live, amber on the way, red when offline. */
export function ConnectionStatusText({ status }: { status: ConnectionStatus }) {
  const tone = tones[status.tone];
  return (
    <span role="status" className={`flex items-center gap-1.5 ${tone.text}`} title="Prices and figures come live from the trading service">
      <span aria-hidden="true" className={`size-1.5 shrink-0 rounded-full ${tone.dot}`} />
      {status.label}
    </span>
  );
}
