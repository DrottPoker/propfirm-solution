import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api, resultOf } from "./api/client";
import type { FirmFilter } from "./api/types";
import { fieldErrorOf, LoginFailedError } from "./queries";

// Our own admin view, where our staff review firms (ADR 0021). It has its own session, on its own address.

const meKey = ["ops-me"];

/** The staff member who is logged in, or null when nobody is. */
export function useOpsMe() {
  return useQuery({
    queryKey: meKey,
    queryFn: async () => {
      const result = await api.GET("/api/portal/ops/me");
      return result.response.status === 401 ? null : resultOf(result, "the logged in user");
    },
    staleTime: Infinity,
  });
}

export function useOpsLogin() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { email: string; password: string }) => {
      const result = await api.POST("/api/portal/ops/login", { body });
      if (result.response.status === 401 || result.response.status === 429) {
        throw new LoginFailedError(result.response.status === 429);
      }

      return resultOf(result, "the login");
    },
    onSuccess: (me) => {
      queryClient.clear();
      queryClient.setQueryData(meKey, me);
    },
  });
}

export function useOpsLogout() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      await api.POST("/api/portal/ops/logout");
    },
    onSuccess: () => {
      queryClient.clear();
      queryClient.setQueryData(meKey, null);
    },
  });
}

/** The firms that wait for us, the suspended ones, or all. */
export function useOpsFirms(filter: FirmFilter) {
  return useQuery({
    queryKey: ["ops-firms", filter],
    queryFn: async () => resultOf(await api.GET("/api/portal/ops/firms", { params: { query: { filter } } }), "the firms"),
    refetchInterval: 30_000,
  });
}

export function useOpsFirm(firmId: string) {
  return useQuery({
    queryKey: ["ops-firm", firmId],
    queryFn: async () => resultOf(await api.GET("/api/portal/ops/firms/{firmId}", { params: { path: { firmId } } }), "the firm"),
  });
}

export type OpsAction = "approve" | "request-changes" | "reject" | "suspend" | "unsuspend";

/** A decision on the firm's application, or a suspension. The answer is the firm as it is afterwards. */
export function useOpsAction(firmId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ action, text }: { action: OpsAction; text: string }) => {
      const path = { params: { path: { firmId } } };
      const message = text.trim() || null;
      const result =
        action === "approve"
          ? await api.POST("/api/portal/ops/firms/{firmId}/approve", { ...path, body: { message } })
          : action === "request-changes"
            ? await api.POST("/api/portal/ops/firms/{firmId}/request-changes", { ...path, body: { message } })
            : action === "reject"
              ? await api.POST("/api/portal/ops/firms/{firmId}/reject", { ...path, body: { message } })
              : action === "suspend"
                ? await api.POST("/api/portal/ops/firms/{firmId}/suspend", { ...path, body: { reason: message } })
                : await api.POST("/api/portal/ops/firms/{firmId}/unsuspend", path);
      if (result.data) {
        return result.data;
      }

      throw fieldErrorOf(result.error, result.response.status, "the decision");
    },
    onSuccess: (firm) => {
      queryClient.setQueryData(["ops-firm", firmId], firm);
      return queryClient.invalidateQueries({ queryKey: ["ops-firms"] });
    },
  });
}
