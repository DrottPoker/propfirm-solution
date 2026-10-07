import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api, ensureOk, resultOf } from "./api/client";
import type { IncidentRequest, IncidentStatus, OpsFirm, OpsFirmGroup } from "./api/types";
import { fieldErrorOf, LoginFailedError } from "./queries";

// Our own admin view, where our staff see what waits for them, review firms and follow what they pay (ADRs 0021 and
// 0024). It has its own session, on its own address.

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

/** What waits for us across the firms, and how the platform is doing (ADR 0024). */
export function useOpsOverview() {
  return useQuery({
    queryKey: ["ops-overview"],
    queryFn: async () => resultOf(await api.GET("/api/portal/ops/overview"), "the overview"),
    refetchInterval: 30_000,
  });
}

/** How many applications wait for us and how many charges were declined, for the menu. */
export function useOpsWaiting() {
  return useQuery({
    queryKey: ["ops-waiting"],
    queryFn: async () => resultOf(await api.GET("/api/portal/ops/waiting"), "what waits for us"),
    refetchInterval: 30_000,
  });
}

/** The firms in a group that the search finds, with the counts in every group. */
export function useOpsFirms(group: OpsFirmGroup, search: string) {
  return useQuery({
    queryKey: ["ops-firms", group, search],
    queryFn: async () => resultOf(await api.GET("/api/portal/ops/firms", { params: { query: { group, search: search || undefined } } }), "the firms"),
    placeholderData: keepPreviousData,
    refetchInterval: 30_000,
  });
}

export function useOpsFirm(firmId: string) {
  return useQuery({
    queryKey: ["ops-firm", firmId],
    queryFn: async () => resultOf(await api.GET("/api/portal/ops/firms/{firmId}", { params: { path: { firmId } } }), "the firm"),
  });
}

/** What firms pay us, and what they have not paid. */
export function useOpsBilling() {
  return useQuery({
    queryKey: ["ops-billing"],
    queryFn: async () => resultOf(await api.GET("/api/portal/ops/billing"), "what firms pay"),
    refetchInterval: 30_000,
  });
}

/** Ticks or unticks one of our checks in the firm's review. The firm's page then shows the checks as they were saved, with who ticked them. */
export function useOpsCheck(firmId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ item, done }: { item: string; done: boolean }) => {
      const result = await api.PUT("/api/portal/ops/firms/{firmId}/checks/{item}", { params: { path: { firmId, item } }, body: { done } });
      if (result.data) {
        return result.data;
      }

      throw fieldErrorOf(result.error, result.response.status, "the check");
    },
    onSuccess: (checks) => queryClient.setQueryData<OpsFirm>(["ops-firm", firmId], (firm) => (firm ? { ...firm, checks } : firm)),
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
      return Promise.all(
        [["ops-firms"], ["ops-overview"], ["ops-waiting"], ["ops-billing"]].map((queryKey) => queryClient.invalidateQueries({ queryKey })),
      );
    },
  });
}

/** Our incidents of the last 90 days and those that go on, with the price feed now and the firms an outage would reach (ADR 0053). */
export function useOpsIncidents() {
  return useQuery({
    queryKey: ["ops-incidents"],
    queryFn: async () => resultOf(await api.GET("/api/portal/ops/incidents"), "the incidents"),
    refetchInterval: 15_000,
  });
}

function refreshIncidents(queryClient: ReturnType<typeof useQueryClient>) {
  return Promise.all([queryClient.invalidateQueries({ queryKey: ["ops-incidents"] }), queryClient.invalidateQueries({ queryKey: ["ops-waiting"] })]);
}

/** Writes a new draft, or changes an incident. The answer is the incident as it is afterwards. */
export function useSaveIncident(incidentId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: IncidentRequest) => {
      const result =
        incidentId === null
          ? await api.POST("/api/portal/ops/incidents", { body })
          : await api.PUT("/api/portal/ops/incidents/{incidentId}", { params: { path: { incidentId } }, body });
      return resultOf(result, "the incident");
    },
    onSuccess: () => refreshIncidents(queryClient),
  });
}

/** Shows the incident to the firms it concerns, in their terminals and on their status pages, and emails their administrators. */
export function usePublishIncident() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (incidentId: string) =>
      resultOf(await api.POST("/api/portal/ops/incidents/{incidentId}/publish", { params: { path: { incidentId } } }), "the incident"),
    onSuccess: () => refreshIncidents(queryClient),
  });
}

/** Says how the incident goes on. Resolved ends it. */
export function usePostIncidentUpdate(incidentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { status: IncidentStatus; text: string }) =>
      resultOf(await api.POST("/api/portal/ops/incidents/{incidentId}/updates", { params: { path: { incidentId } }, body }), "the update"),
    onSuccess: () => refreshIncidents(queryClient),
  });
}

/** Hides a draft, such as a false alarm. */
export function useDismissIncident(incidentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => ensureOk(await api.POST("/api/portal/ops/incidents/{incidentId}/dismiss", { params: { path: { incidentId } } }), "the dismissal"),
    onSuccess: () => refreshIncidents(queryClient),
  });
}
