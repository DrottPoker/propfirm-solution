import { useMutation, useQuery } from "@tanstack/react-query";

import { api, commandResult, queryResult } from "./api/client";
import type { CommandResponse, PlaceOrderRequest, Timeframe } from "./api/types";
import { useTradingStore } from "./store";

const accountPath = (accountId: string) => ({ params: { path: { accountId } } });

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
