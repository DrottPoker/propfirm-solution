import { type QueryClient, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api, commandResult, markLoggedIn, markLoggedOut, queryResult } from "./api/client";
import type { CommandResponse, EventEnvelope, PlaceOrderRequest, Timeframe } from "./api/types";
import { dayCandleCount, dayTimeframe, summarizeDay } from "./daySummary";
import type { EventQuery } from "./eventSync";
import { newEvents } from "./notices";
import { useTradingStore } from "./store";

const accountPath = (accountId: string) => ({ params: { path: { accountId } } });

export const meKey = ["me"];

/** The logged in trader, or null when nobody is logged in. */
export function useMe() {
  return useQuery({
    queryKey: meKey,
    queryFn: async () => {
      const result = await api.GET("/api/auth/me");
      return result.response.status === 401 ? null : queryResult(result, "the logged in trader");
    },
    staleTime: Infinity,
  });
}

/** The servers traders can log in to. Each firm has one, as in MetaTrader. */
export function useServers() {
  return useQuery({
    queryKey: ["servers"],
    queryFn: async () => queryResult(await api.GET("/api/servers"), "the servers"),
    staleTime: Infinity,
  });
}

/** One server, also one that is not listed, for example the one in a link from the firm's portal. Null if it does not exist. */
export function useServer(serverId: string | null) {
  return useQuery({
    queryKey: ["server", serverId],
    queryFn: async () => {
      const result = await api.GET("/api/servers/{id}", { params: { path: { id: serverId ?? "" } } });
      return result.response.status === 404 ? null : queryResult(result, "the server");
    },
    enabled: serverId !== null && serverId.length > 0,
    staleTime: Infinity,
  });
}

/** The service said no to the server, email and password, or to too many attempts. */
export class LoginFailedError extends Error {
  constructor(readonly tooManyAttempts: boolean) {
    super(tooManyAttempts ? "Too many attempts. Wait a minute and try again." : "Wrong server, email or password.");
    this.name = "LoginFailedError";
  }
}

export function useLogin() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: { server: string; email: string; password: string }) => {
      const result = await api.POST("/api/auth/login", { body: input });
      if (result.response.status === 401 || result.response.status === 429) {
        throw new LoginFailedError(result.response.status === 429);
      }

      return queryResult(result, "the login");
    },
    onSuccess: (me) => {
      markLoggedIn();
      queryClient.setQueryData(meKey, me);
    },
  });
}

/** Logs in with a one-time link from the firm's portal. */
export function useLinkLogin() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (token: string) => {
      const result = await api.POST("/api/auth/link", { body: { token } });
      if (result.response.status === 401) {
        throw new LinkLoginFailedError();
      }

      if (result.response.status === 429) {
        throw new LoginFailedError(true);
      }

      return queryResult(result, "the login link");
    },
    onSuccess: (me) => {
      markLoggedIn();
      queryClient.setQueryData(meKey, me);
    },
  });
}

/** The service refused the login link. */
export class LinkLoginFailedError extends Error {
  constructor() {
    super("This link has expired or was already used. Open the terminal again from your firm's portal, or log in.");
    this.name = "LinkLoginFailedError";
  }
}

export function useLogout() {
  const queryClient = useQueryClient();
  return useMutation({
    // Marked first, so calls refused while the session ends do not send the trader back to the firm's portal.
    mutationFn: async () => {
      markLoggedOut();
      await api.POST("/api/auth/logout");
    },
    onSuccess: () => queryClient.setQueryData(meKey, null),
  });
}

export function useInstruments(accountId: string) {
  return useQuery({
    queryKey: ["instruments", accountId],
    queryFn: async () => queryResult(await api.GET("/api/accounts/{accountId}/instruments", accountPath(accountId)), "instruments"),
    staleTime: Infinity,
  });
}

/**
 * A refresh interval that ends on whole multiples of the period on the clock. Every component that uses the same
 * query then refreshes at the same moment and shares one request, instead of each sending its own a little apart.
 */
export function onTheClock(periodMs: number): () => number {
  return () => periodMs - (Date.now() % periodMs);
}

const pointValueRefreshMs = 10_000;

/**
 * What a point of the symbol is worth on the account, from the engine. It follows the conversion rate, so it is
 * refreshed now and then. Null until the symbol has a conversion rate.
 */
export function usePointValue(accountId: string, symbol: string | null) {
  return useQuery({
    queryKey: ["pointValue", accountId, symbol],
    enabled: symbol !== null,
    queryFn: async () => {
      const result = await api.GET("/api/accounts/{accountId}/instruments/{symbol}/point-value", {
        params: { path: { accountId, symbol: symbol ?? "" } },
      });
      return result.response.status === 404 ? null : queryResult(result, "the point value");
    },
    staleTime: pointValueRefreshMs,
    refetchInterval: onTheClock(pointValueRefreshMs),
  });
}

/** Candle history. Live updates are applied by the chart itself. */
export function useCandles(accountId: string, symbol: string | null, timeframe: Timeframe) {
  return useQuery({
    queryKey: ["candles", accountId, symbol, timeframe],
    enabled: symbol !== null,
    queryFn: async () =>
      queryResult(
        await api.GET("/api/accounts/{accountId}/candles/{symbol}", {
          params: { path: { accountId, symbol: symbol ?? "" }, query: { timeframe, count: 500 } },
        }),
        "candles",
      ),
    staleTime: Infinity,
  });
}

const daySummaryRefreshMs = 5 * 60 * 1000;

/** The last 24 hours of a symbol for the watchlist and the chart header. Shown together with the live bid. */
export function useDaySummary(accountId: string, symbol: string | null) {
  return useQuery({
    queryKey: ["day", accountId, symbol],
    enabled: symbol !== null,
    queryFn: async () => {
      const candles = queryResult(
        await api.GET("/api/accounts/{accountId}/candles/{symbol}", {
          params: { path: { accountId, symbol: symbol ?? "" }, query: { timeframe: dayTimeframe, count: dayCandleCount } },
        }),
        "candles",
      );
      return summarizeDay(candles, Date.now());
    },
    staleTime: daySummaryRefreshMs,
    refetchInterval: onTheClock(daySummaryRefreshMs),
  });
}

/** Loads the account's charts and day summaries again, for example after the service restarted and rebuilt them. */
export function reloadCandles(queryClient: QueryClient, accountId: string): void {
  void queryClient.invalidateQueries({ queryKey: ["candles", accountId] });
  void queryClient.invalidateQueries({ queryKey: ["day", accountId] });
}

/** A page of the account's events, oldest first. Kept in the store by the realtime connection. */
export async function fetchEvents(accountId: string, query: EventQuery): Promise<EventEnvelope[]> {
  return queryResult(await api.GET("/api/accounts/{accountId}/events", { params: { path: { accountId }, query } }), "events");
}

// Commands add their events to the store right away, and tell the trader of them. The same events also arrive in
// realtime, are merged and are told only once.
function addEvents(response: CommandResponse) {
  useTradingStore.getState().addEvents(response.events);
  newEvents.announce(response.events);
}

export function usePlaceOrder(accountId: string) {
  return useMutation({
    mutationFn: async (request: PlaceOrderRequest) =>
      commandResult(await api.POST("/api/accounts/{accountId}/orders", { ...accountPath(accountId), body: request })),
    onSuccess: addEvents,
  });
}

export function useClosePosition(accountId: string) {
  return useMutation({
    mutationFn: async (positionId: string) =>
      commandResult(
        await api.POST("/api/accounts/{accountId}/positions/{positionId}/close", { params: { path: { accountId, positionId } } }),
      ),
    onSuccess: addEvents,
  });
}

export function useCancelOrder(accountId: string) {
  return useMutation({
    mutationFn: async (orderId: string) =>
      commandResult(await api.DELETE("/api/accounts/{accountId}/orders/{orderId}", { params: { path: { accountId, orderId } } })),
    onSuccess: addEvents,
  });
}

export function useModifyStops(accountId: string) {
  return useMutation({
    mutationFn: async (input: { positionId: string; stopLoss: number | null; takeProfit: number | null }) =>
      commandResult(
        await api.PUT("/api/accounts/{accountId}/positions/{positionId}/stops", {
          params: { path: { accountId, positionId: input.positionId } },
          body: { stopLoss: input.stopLoss, takeProfit: input.takeProfit },
        }),
      ),
    onSuccess: addEvents,
  });
}
