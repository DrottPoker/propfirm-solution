import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useEffect } from "react";

import type { AccountSnapshot, EventEnvelope, SymbolPrice } from "./api/types";
import { tradingApiUrl } from "./config";
import { loadEvents } from "./queries";
import { useTradingStore } from "./store";

const retryDelayMs = 2_000;

/**
 * Keeps a SignalR connection for the account and feeds the store. Retries until the service is
 * reachable, and after a reconnect fetches the events that were missed while disconnected.
 */
export function useTradingConnection(accountId: string): void {
  useEffect(() => {
    const store = useTradingStore.getState();
    store.reset();

    const connection = new HubConnectionBuilder()
      .withUrl(`${tradingApiUrl}/hubs/trading`)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("Account", (account: AccountSnapshot) => useTradingStore.getState().setAccount(account));
    connection.on("Prices", (prices: SymbolPrice[]) => useTradingStore.getState().applyPrices(prices));
    connection.on("Events", (events: EventEnvelope[]) => useTradingStore.getState().addEvents(events));

    const lastSequence = () => useTradingStore.getState().events.at(-1)?.sequence ?? 0;

    connection.onreconnecting(() => useTradingStore.getState().setConnection("reconnecting"));
    connection.onreconnected(async () => {
      await connection.invoke("Subscribe", accountId);
      useTradingStore.getState().setConnection("connected");
      await loadEvents(accountId, lastSequence());
    });
    connection.onclose(() => useTradingStore.getState().setConnection("disconnected"));

    let stopped = false;
    let retryTimer: ReturnType<typeof setTimeout> | undefined;

    const start = async () => {
      try {
        useTradingStore.getState().setConnection("connecting");
        await connection.start();
        if (stopped) {
          return;
        }

        await connection.invoke("Subscribe", accountId);
        useTradingStore.getState().setConnection("connected");
        await loadEvents(accountId, lastSequence());
      } catch {
        if (!stopped) {
          useTradingStore.getState().setConnection("disconnected");
          retryTimer = setTimeout(() => void start(), retryDelayMs);
        }
      }
    };

    // Deferred so that React development mode, which runs effects twice, does not start and abort a connection.
    const startTimer = setTimeout(() => void start(), 0);

    return () => {
      stopped = true;
      clearTimeout(startTimer);
      clearTimeout(retryTimer);
      void connection.stop();
    };
  }, [accountId]);
}
