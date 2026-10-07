import { HubConnectionBuilder, type IRetryPolicy, LogLevel } from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";

import type { AccountRules, AccountSnapshot, EventEnvelope, SymbolPrice, TerminalNotice } from "./api/types";
import { tradingApiUrl } from "./config";
import { createEventSync, type EventLoad } from "./eventSync";
import { newEvents } from "./notices";
import { fetchEvents, noticeKey, reloadCandles, reloadMarketHours, rulesKey } from "./queries";
import { maxEvents, useTradingStore } from "./store";

const retryDelayMs = 2_000;

/** Reconnects at once, then every few seconds for as long as it takes, since a restart of the service can take a while. */
export const reconnectPolicy: IRetryPolicy = {
  nextRetryDelayInMilliseconds: (context) => (context.previousRetryCount === 0 ? 0 : retryDelayMs),
};

/**
 * Keeps a SignalR connection for the account and feeds the store. Retries until the service is reachable. Loads the
 * latest events when it subscribes, and after a reconnect the events that were missed while disconnected. The charts
 * are loaded again after a lost or failed connection, since the service may have restarted and rebuilt them, for
 * example without the made-up prices of the synthetic feed.
 */
export function useTradingConnection(accountId: string): void {
  const queryClient = useQueryClient();

  useEffect(() => {
    const store = useTradingStore.getState();
    store.reset();
    newEvents.reset();

    let stopped = false;
    let candlesMissed = false;
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
      .withAutomaticReconnect(reconnectPolicy)
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("Account", (account: AccountSnapshot) => useTradingStore.getState().setAccount(account));
    connection.on("Prices", (prices: SymbolPrice[]) => useTradingStore.getState().applyPrices(prices));
    // A handler that returns a value would answer the service, which expects no answer.
    connection.on("Rules", (rules: AccountRules) => {
      queryClient.setQueryData(rulesKey(accountId), rules);
    });
    // Also on subscribing, so a notice set while the connection was lost is shown.
    connection.on("Notice", (notice: TerminalNotice | null) => {
      queryClient.setQueryData(noticeKey(accountId), notice);
    });
    // Pushed events are new, unlike the ones loaded at the start or after a reconnect, so the trader is told of them.
    connection.on("Events", (events: EventEnvelope[]) => {
      eventSync.received(events);
      newEvents.announce(events);
    });

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
      if (candlesMissed) {
        candlesMissed = false;
        reloadCandles(queryClient, accountId);
        reloadMarketHours(queryClient, accountId);
        // New rules may have been told while the connection was lost.
        void queryClient.invalidateQueries({ queryKey: rulesKey(accountId) });
      }

      clearTimeout(loadTimer);
      await load(eventLoad);
    };

    // A failed subscription leaves the connection open, so it is closed and the next attempt starts over.
    const retry = async () => {
      candlesMissed = true;
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

    connection.onreconnecting(() => {
      candlesMissed = true;
      useTradingStore.getState().setConnection("reconnecting");
    });
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
  }, [accountId, queryClient]);
}
