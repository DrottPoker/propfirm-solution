import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { ApiError, api, ensureOk, unwrap } from "./api/client";

/** How often the live pages ask again. The figures change with every price, so a few seconds is enough. */
export const liveInterval = 5_000;

export const meKey = ["staff", "me"];

/** The logged in staff member, or null when nobody is logged in. */
export function useMe() {
  return useQuery({
    queryKey: meKey,
    queryFn: async () => {
      const result = await api.GET("/api/staff/v1/me");
      return result.response.status === 401 ? null : unwrap(result, "load who is logged in");
    },
    staleTime: Infinity,
    retry: false,
  });
}

export function useLogin() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (login: { email: string; password: string }) => {
      const result = await api.POST("/api/staff/v1/login", { body: login });
      if (result.response.status === 429) {
        throw new ApiError("Too many attempts. Wait a minute and try again.", 429, null);
      }

      if (result.response.status === 401) {
        throw new ApiError("Wrong email or password.", 401, null);
      }

      return unwrap(result, "log in");
    },
    onSuccess: (me) => queryClient.setQueryData(meKey, me),
  });
}

export function useLogout() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => ensureOk(await api.POST("/api/staff/v1/logout"), "log out"),
    onSettled: () => {
      queryClient.clear();
      queryClient.setQueryData(meKey, null);
    },
  });
}

export function useOverview() {
  return useQuery({
    queryKey: ["staff", "overview"],
    queryFn: async () => unwrap(await api.GET("/api/staff/v1/overview"), "load the overview"),
    refetchInterval: liveInterval,
  });
}

export type ServerGroup = "all" | "needsUs" | "listed" | "notListed" | "configuration";

export function useServers(group: ServerGroup, search: string) {
  return useQuery({
    queryKey: ["staff", "servers", group, search],
    queryFn: async () =>
      unwrap(await api.GET("/api/staff/v1/servers", { params: { query: { group, search: search.trim() || undefined } } }), "load the servers"),
    refetchInterval: 15_000,
    placeholderData: (previous) => previous,
  });
}

/** One server, or null when there is none with the id. */
export function useServer(id: string) {
  return useQuery({
    queryKey: ["staff", "server", id],
    queryFn: async () => {
      const result = await api.GET("/api/staff/v1/servers/{id}", { params: { path: { id } } });
      return result.response.status === 404 ? null : unwrap(result, "load the server");
    },
    refetchInterval: liveInterval,
  });
}

export function useServerEvents(id: string, account: string | null) {
  return useQuery({
    queryKey: ["staff", "server", id, "events", account],
    queryFn: async () =>
      unwrap(
        await api.GET("/api/staff/v1/servers/{id}/events", { params: { path: { id }, query: { account: account ?? undefined, limit: 30 } } }),
        "load the server's events",
      ),
    refetchInterval: liveInterval,
  });
}

/** An account by its number, or null when there is none. */
export function useAccount(accountId: string | null) {
  return useQuery({
    queryKey: ["staff", "account", accountId],
    queryFn: async () => {
      const result = await api.GET("/api/staff/v1/accounts/{accountId}", { params: { path: { accountId: accountId ?? "" } } });
      return result.response.status === 404 ? null : unwrap(result, "load the account");
    },
    enabled: accountId !== null,
    refetchInterval: liveInterval,
  });
}

export function useSearch(text: string) {
  const query = text.trim();
  return useQuery({
    queryKey: ["staff", "search", query],
    queryFn: async () => unwrap(await api.GET("/api/staff/v1/search", { params: { query: { q: query } } }), "search"),
    enabled: query.length > 0,
    placeholderData: (previous) => previous,
  });
}

export function usePriceFeed() {
  return useQuery({
    queryKey: ["staff", "price-feed"],
    queryFn: async () => unwrap(await api.GET("/api/staff/v1/price-feed"), "load the price feed"),
    refetchInterval: liveInterval,
  });
}

export function useInstruments() {
  return useQuery({
    queryKey: ["staff", "instruments"],
    queryFn: async () => unwrap(await api.GET("/api/staff/v1/instruments"), "load the instruments"),
    staleTime: 60_000,
  });
}

export function useExposure(server: string | null) {
  return useQuery({
    queryKey: ["staff", "exposure", server],
    queryFn: async () => unwrap(await api.GET("/api/staff/v1/exposure", { params: { query: { server: server ?? undefined } } }), "load the exposure"),
    refetchInterval: liveInterval,
    placeholderData: (previous) => previous,
  });
}

export function useEngine() {
  return useQuery({
    queryKey: ["staff", "engine"],
    queryFn: async () => unwrap(await api.GET("/api/staff/v1/engine"), "load the engine"),
    refetchInterval: liveInterval,
  });
}

export function useCurrencies() {
  return useQuery({
    queryKey: ["staff", "currencies"],
    queryFn: async () => unwrap(await api.GET("/api/staff/v1/currencies"), "load the currencies"),
    staleTime: Infinity,
  });
}

/** Whether a server with the name can be made now, asked once the name looks right. */
export function useServerName(id: string) {
  return useQuery({
    queryKey: ["staff", "server-name", id],
    queryFn: async () => unwrap(await api.GET("/api/staff/v1/server-names/{id}", { params: { path: { id } } }), "check the server name"),
    enabled: /^[a-z0-9][a-z0-9-]{1,62}$/.test(id),
  });
}

export function useCreateServer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (server: { id: string; name: string; currency: string }) =>
      unwrap(await api.POST("/api/staff/v1/servers", { body: server }), "make the server"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["staff"] }),
  });
}

export function useSetListed(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (listed: boolean) =>
      unwrap(await api.PATCH("/api/staff/v1/servers/{id}", { params: { path: { id } }, body: { listed } }), "change the listing"),
    onSuccess: (server) => {
      queryClient.setQueryData(["staff", "server", id], server);
      return queryClient.invalidateQueries({ queryKey: ["staff", "servers"] });
    },
  });
}

export function useReplaceKey(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (reason: string) =>
      unwrap(await api.POST("/api/staff/v1/servers/{id}/admin-key", { params: { path: { id } }, body: { reason } }), "stop the key"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["staff", "server", id] }),
  });
}

export function useRetryGap() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (gapId: string) =>
      ensureOk(await api.POST("/api/staff/v1/price-feed/gaps/{gapId}/retry", { params: { path: { gapId } } }), "try the gap again"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["staff", "price-feed"] }),
  });
}

export function useReloadHistory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => ensureOk(await api.POST("/api/staff/v1/price-feed/history/reload"), "load the history again"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["staff", "price-feed"] }),
  });
}
