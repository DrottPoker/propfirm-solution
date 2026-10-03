import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api, commandResult, queryResult } from "./api/client";
import type { CommandResponse, PlaceOrderRequest, Timeframe } from "./api/types";
import { useTradingStore } from "./store";

const accountPath = (accountId: string) => ({ params: { path: { accountId } } });

const meKey = ["me"];

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
    onSuccess: (me) => queryClient.setQueryData(meKey, me),
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
    onSuccess: (me) => queryClient.setQueryData(meKey, me),
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
    mutationFn: async () => {
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

/** Loads event history into the store, from after the given sequence number. */
export async function loadEvents(accountId: string, after: number): Promise<void> {
  const events = queryResult(
    await api.GET("/api/accounts/{accountId}/events", { params: { path: { accountId }, query: { after, limit: 1_000 } } }),
    "events",
  );
  useTradingStore.getState().addEvents(events);
}

// Commands add their events to the store right away. The same events also arrive in realtime and are merged.
function addEvents(response: CommandResponse) {
  useTradingStore.getState().addEvents(response.events);
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
