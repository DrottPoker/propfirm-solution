import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useEffect } from "react";

import type { AccountSnapshot, EventEnvelope, SymbolPrice } from "./api/types";
import { tradingApiUrl } from "./config";
import { createEventSync, type EventLoad } from "./eventSync";
import { fetchEvents } from "./queries";
import { maxEvents, useTradingStore } from "./store";

const retryDelayMs = 2_000;

/**
 * Keeps a SignalR connection for the account and feeds the store. Retries until the service is reachable. Loads the
 * latest events when it subscribes, and after a reconnect the events that were missed while disconnected.
 */
export function useTradingConnection(accountId: string): void {
  useEffect(() => {
    const store = useTradingStore.getState();
    store.reset();

    let stopped = false;
    let retryTimer: ReturnType<typeof setTimeout> | undefined;
    let loadTimer: ReturnType<typeof setTimeout> | undefined;

    const eventSync = createEventSync(
      (query) => fetchEvents(accountId, query),
      // A load that ends after the terminal has switched account must not reach the store.
      (events) => {
        if (!stopped) {
          useTradingStore.getState().addEvents(events);
        }
      },
      maxEvents,
    );

    const connection = new HubConnectionBuilder()
      .withUrl(`${tradingApiUrl}/hubs/trading`)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("Account", (account: AccountSnapshot) => useTradingStore.getState().setAccount(account));
    connection.on("Prices", (prices: SymbolPrice[]) => useTradingStore.getState().applyPrices(prices));
    connection.on("Events", (events: EventEnvelope[]) => eventSync.received(events));

    // Tries again until the events are loaded or a newer subscription takes over.
    const load = async (eventLoad: EventLoad) => {
      try {
        await eventLoad.run();
      } catch {
        if (!stopped && eventLoad.current) {
          loadTimer = setTimeout(() => void load(eventLoad), retryDelayMs);
        }
      }
    };

    const subscribe = async () => {
      const eventLoad = eventSync.subscribing();
      await connection.invoke("Subscribe", accountId);
      useTradingStore.getState().setConnection("connected");
      clearTimeout(loadTimer);
      await load(eventLoad);
    };

    // A failed subscription leaves the connection open, so it is closed and the next attempt starts over.
    const retry = async () => {
      await connection.stop();
      if (!stopped) {
        useTradingStore.getState().setConnection("disconnected");
        retryTimer = setTimeout(() => void start(), retryDelayMs);
      }
    };

    const start = async () => {
      try {
        useTradingStore.getState().setConnection("connecting");
        await connection.start();
        if (stopped) {
          return;
        }

        await subscribe();
      } catch {
        if (!stopped) {
          await retry();
        }
      }
    };

    connection.onreconnecting(() => useTradingStore.getState().setConnection("reconnecting"));
    connection.onreconnected(async () => {
      try {
        await subscribe();
      } catch {
        if (!stopped) {
          await retry();
        }
      }
    });
    connection.onclose(() => useTradingStore.getState().setConnection("disconnected"));

    // Deferred so that React development mode, which runs effects twice, does not start and abort a connection.
    const startTimer = setTimeout(() => void start(), 0);

    return () => {
      stopped = true;
      clearTimeout(startTimer);
      clearTimeout(retryTimer);
      clearTimeout(loadTimer);
      void connection.stop();
    };
  }, [accountId]);
}
