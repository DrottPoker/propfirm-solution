import { type QueryClient, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api, commandResult, markLoggedIn, markLoggedOut, queryResult } from "./api/client";
import type { Candle, CommandResponse, EventEnvelope, MarketHours, OwnLimits, PlaceOrderRequest, Timeframe } from "./api/types";
import { dayCandleCount, dayTimeframe, summarizeDay } from "./daySummary";
import type { EventQuery } from "./eventSync";
import { marketRefreshDelay } from "./marketHours";
import { newEvents } from "./notices";
import { flushSettings } from "./syncedSettings";
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
    // Settings changed a moment ago are sent first. Then marked, so calls refused while the session ends do not
    // send the trader back to the firm's portal.
    mutationFn: async () => {
      await flushSettings();
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

/**
 * When each of the account's markets is open, as the service says. Loaded again just after the next market opens or
 * closes, also in a tab in the background, so the terminal never waits for a refusal to show it.
 */
export function useMarketHours(accountId: string) {
  return useQuery({
    queryKey: ["marketHours", accountId],
    queryFn: async () => queryResult(await api.GET("/api/accounts/{accountId}/market-hours", accountPath(accountId)), "market hours"),
    staleTime: (query) => marketRefreshDelay(query.state.data ?? [], Date.now()),
    refetchInterval: (query) => marketRefreshDelay(query.state.data ?? [], Date.now()),
    refetchIntervalInBackground: true,
  });
}

/** The symbol's market hours, or undefined until they are loaded. A market not yet known counts as open: the engine decides anyway. */
export function useMarket(accountId: string, symbol: string | null): MarketHours | undefined {
  return useMarketHours(accountId).data?.find((m) => m.symbol === symbol);
}

export const noticeKey = (accountId: string) => ["notice", accountId];

/** The firm's notice for its terminals, such as an outage, or null. New ones arrive in realtime (ADR 0053). */
export function useNotice(accountId: string) {
  return useQuery({
    queryKey: noticeKey(accountId),
    queryFn: async () => queryResult(await api.GET("/api/accounts/{accountId}/notice", accountPath(accountId)), "the notice").notice,
    staleTime: Infinity,
  });
}

/** The details of one of the account's trades: its fills, the feed's prices behind them and the markup (ADR 0053). */
export function useTradeDetails(accountId: string, positionId: string) {
  return useQuery({
    queryKey: ["trade-details", accountId, positionId],
    queryFn: async () =>
      queryResult(await api.GET("/api/accounts/{accountId}/positions/{positionId}/receipt", { params: { path: { accountId, positionId } } }), "the trade's details"),
    staleTime: 30_000,
  });
}

/** Why the account's loss limit was broken, which never changes once it was (ADR 0053). */
export function useBreachReport(accountId: string) {
  return useQuery({
    queryKey: ["breach", accountId],
    queryFn: async () => queryResult(await api.GET("/api/accounts/{accountId}/breach-report", accountPath(accountId)), "the breach report"),
    staleTime: Infinity,
  });
}

export const rulesKey = (accountId: string) => ["rules", accountId];

/**
 * The account's rules as the firm's system last told them: trading days, deadlines and the consistency rule. New ones
 * arrive in realtime (ADR 0052).
 */
export function useAccountRules(accountId: string) {
  return useQuery({
    queryKey: rulesKey(accountId),
    queryFn: async () => queryResult(await api.GET("/api/accounts/{accountId}/rules", accountPath(accountId)), "the rules"),
    staleTime: Infinity,
  });
}

/** Loads the market hours again, for example after the service restarted, perhaps with another price feed. */
export function reloadMarketHours(queryClient: QueryClient, accountId: string): void {
  void queryClient.invalidateQueries({ queryKey: ["marketHours", accountId] });
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

/** Candles that start before the time in UTC seconds, oldest first, for scrolling back. Empty where the history ends. */
export async function fetchOlderCandles(accountId: string, symbol: string, timeframe: Timeframe, before: number, count = 500): Promise<Candle[]> {
  return queryResult(
    await api.GET("/api/accounts/{accountId}/candles/{symbol}", {
      params: { path: { accountId, symbol }, query: { timeframe, count, before: new Date(before * 1000).toISOString() } },
    }),
    "candles",
  );
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

/** Closes a position, or with a volume only that part of it. */
export function useClosePosition(accountId: string) {
  return useMutation({
    mutationFn: async (input: { positionId: string; volume?: number }) =>
      commandResult(
        await api.POST("/api/accounts/{accountId}/positions/{positionId}/close", {
          params: { path: { accountId, positionId: input.positionId } },
          body: input.volume === undefined ? undefined : { volume: input.volume },
        }),
      ),
    onSuccess: addEvents,
  });
}

/** Closes every position, or those in a symbol, at the same moment. Positions whose market is closed stay open. */
export function useCloseAllPositions(accountId: string) {
  return useMutation({
    mutationFn: async (symbol: string | null) =>
      commandResult(
        await api.POST("/api/accounts/{accountId}/positions/close-all", { ...accountPath(accountId), body: symbol === null ? undefined : { symbol } }),
      ),
    onSuccess: addEvents,
  });
}

/** Sets the trader's own limits (ADR 0054). Stricter ones apply at once, looser ones from the next trading day. */
export function useSetOwnLimits(accountId: string) {
  return useMutation({
    mutationFn: async (limits: OwnLimits) => commandResult(await api.PUT("/api/accounts/{accountId}/limits", { ...accountPath(accountId), body: limits })),
    onSuccess: addEvents,
  });
}

/** Locks new orders until the next trading day, closing every position or keeping them (ADR 0054). Nobody can undo it. */
export function useLockTrading(accountId: string) {
  return useMutation({
    mutationFn: async (closePositions: boolean) =>
      commandResult(await api.POST("/api/accounts/{accountId}/lock", { ...accountPath(accountId), body: { closePositions } })),
    onSuccess: addEvents,
  });
}

/** Moves a pending order and sets its stops. */
export function useModifyOrder(accountId: string) {
  return useMutation({
    mutationFn: async (input: { orderId: string; price: number; stopLoss: number | null; takeProfit: number | null; trailingStop: boolean }) =>
      commandResult(
        await api.PUT("/api/accounts/{accountId}/orders/{orderId}", {
          params: { path: { accountId, orderId: input.orderId } },
          body: { price: input.price, stopLoss: input.stopLoss, takeProfit: input.takeProfit, trailingStop: input.trailingStop },
        }),
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

/**
 * Sets a position's stops. The trailing stop is always given, since a change without it turns it off: a stop loss
 * dragged on a trailing position keeps trailing at the new distance.
 */
export function useModifyStops(accountId: string) {
  return useMutation({
    mutationFn: async (input: { positionId: string; stopLoss: number | null; takeProfit: number | null; trailingStop: boolean }) =>
      commandResult(
        await api.PUT("/api/accounts/{accountId}/positions/{positionId}/stops", {
          params: { path: { accountId, positionId: input.positionId } },
          body: { stopLoss: input.stopLoss, takeProfit: input.takeProfit, trailingStop: input.trailingStop },
        }),
      ),
    onSuccess: addEvents,
  });
}
