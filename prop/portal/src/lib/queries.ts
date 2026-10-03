import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { ApiError, api, resultOf } from "./api/client";
import type { ChallengeStatus, PayoutStatus } from "./api/types";

export type Role = "trader" | "admin";

// Traders and administrators have separate sessions, so both can be logged in at once.
const meKey = (role: Role) => ["me", role];

/** How often account figures are refreshed while a page shows them. */
const liveRefreshMs = 5_000;

const accountPath = (accountId: string) => ({ params: { path: { accountId } } });

/** Who is logged in to this firm's portal with the role, or null when nobody is. */
export function useMe(role: Role) {
  return useQuery({
    queryKey: meKey(role),
    queryFn: async () => {
      const result = role === "admin" ? await api.GET("/api/portal/admin/me") : await api.GET("/api/portal/me");
      return result.response.status === 401 ? null : resultOf(result, "the logged in user");
    },
    staleTime: Infinity,
  });
}

/** The portal said no to the email and password, or to too many attempts. */
export class LoginFailedError extends Error {
  constructor(readonly tooManyAttempts: boolean) {
    super(tooManyAttempts ? "Too many attempts. Wait a minute and try again." : "Wrong email or password.");
    this.name = "LoginFailedError";
  }
}

export function useLogin(role: Role) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { email: string; password: string }) => {
      const result = role === "admin" ? await api.POST("/api/portal/admin/login", { body }) : await api.POST("/api/portal/login", { body });
      if (result.response.status === 401 || result.response.status === 429) {
        throw new LoginFailedError(result.response.status === 429);
      }

      return resultOf(result, "the login");
    },
    onSuccess: (me) => {
      queryClient.clear();
      queryClient.setQueryData(meKey(role), me);
    },
  });
}

/** The trader chooses a password with the firm's invitation, and is logged in. */
export function useAcceptInvite() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { token: string; password: string }) => {
      const result = await api.POST("/api/portal/invites/accept", { body });
      if (result.response.status === 401) {
        throw new ApiError("This invitation has expired or was already used. Ask your firm for a new one.", 401);
      }

      if (result.response.status === 429) {
        throw new LoginFailedError(true);
      }

      return resultOf(result, "the invitation");
    },
    onSuccess: (me) => {
      queryClient.clear();
      queryClient.setQueryData(meKey("trader"), me);
    },
  });
}

/** Ends the role's session. A session with the other role stays. */
export function useLogout(role: Role) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      await (role === "admin" ? api.POST("/api/portal/admin/logout") : api.POST("/api/portal/logout"));
    },
    onSuccess: () => {
      queryClient.clear();
      queryClient.setQueryData(meKey(role), null);
    },
  });
}

/** The logged in trader's challenge accounts. */
export function useMyAccounts() {
  return useQuery({
    queryKey: ["my-accounts"],
    queryFn: async () => resultOf(await api.GET("/api/portal/accounts"), "your accounts"),
    refetchInterval: liveRefreshMs,
  });
}

/** One of the trader's accounts, valued at the latest prices. */
export function useMyAccount(accountId: string | null) {
  return useQuery({
    queryKey: ["my-account", accountId],
    enabled: accountId !== null,
    queryFn: async () => resultOf(await api.GET("/api/portal/accounts/{accountId}", accountPath(accountId ?? "")), "the account"),
    refetchInterval: liveRefreshMs,
  });
}

/** The trader asks for a payout of the funded account's profit. The service answers with the reason when it cannot. */
export function useRequestPayout(accountId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => resultOf(await api.POST("/api/portal/accounts/{accountId}/payouts", accountPath(accountId)), "the payout"),
    onSettled: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: ["my-account", accountId] }),
        queryClient.invalidateQueries({ queryKey: ["my-accounts"] }),
      ]),
  });
}

/** A one-time link that logs the trader in to the trading terminal on the account. */
export function useTerminalLink() {
  return useMutation({
    mutationFn: async (accountId: string) =>
      resultOf(await api.POST("/api/portal/accounts/{accountId}/terminal-link", accountPath(accountId)), "the terminal link"),
  });
}

export function useChallenges() {
  return useQuery({
    queryKey: ["challenges"],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/challenges"), "the challenges"),
  });
}

export type AccountFilter = { email: string; status: ChallengeStatus | "" };

/** The firm's newest accounts, optionally one trader's or those with a status. */
export function useFirmAccounts(filter: AccountFilter) {
  return useQuery({
    queryKey: ["firm-accounts", filter],
    queryFn: async () =>
      resultOf(
        await api.GET("/api/portal/admin/accounts", {
          params: { query: { email: filter.email || undefined, status: filter.status || undefined, limit: 200 } },
        }),
        "the accounts",
      ),
    refetchInterval: liveRefreshMs,
  });
}

export function useFirmAccount(accountId: string) {
  return useQuery({
    queryKey: ["firm-account", accountId],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/accounts/{accountId}", accountPath(accountId)), "the account"),
    refetchInterval: liveRefreshMs,
  });
}

export function useHistory(accountId: string) {
  return useQuery({
    queryKey: ["history", accountId],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/accounts/{accountId}/history", accountPath(accountId)), "the history"),
    refetchInterval: liveRefreshMs,
  });
}

export function useStartAccount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { email: string; challengeId: string; reference: string | null }) =>
      resultOf(await api.POST("/api/portal/admin/accounts", { body }), "the new account"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["firm-accounts"] }),
  });
}

/** Approving funding or cancelling. Both answer with the account as it is afterwards. */
export function useAccountCommand(accountId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (command: { kind: "approve-funding" } | { kind: "cancel"; reason: string }) =>
      command.kind === "approve-funding"
        ? resultOf(await api.POST("/api/portal/admin/accounts/{accountId}/approve-funding", accountPath(accountId)), "the approval")
        : resultOf(
            await api.POST("/api/portal/admin/accounts/{accountId}/cancel", { ...accountPath(accountId), body: { reason: command.reason || null } }),
            "the cancellation",
          ),
    onSettled: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: ["firm-account", accountId] }),
        queryClient.invalidateQueries({ queryKey: ["history", accountId] }),
        queryClient.invalidateQueries({ queryKey: ["firm-accounts"] }),
      ]),
  });
}

/** The firm's newest payouts, all of them or only those with the statuses. */
export function useFirmPayouts(statuses: PayoutStatus[]) {
  return useQuery({
    queryKey: ["firm-payouts", statuses],
    queryFn: async () =>
      resultOf(await api.GET("/api/portal/admin/payouts", { params: { query: { status: statuses, limit: 200 } } }), "the payouts"),
    refetchInterval: liveRefreshMs,
  });
}

export type PayoutDecision =
  | { kind: "approve"; payoutId: string }
  | { kind: "mark-paid"; payoutId: string; reference: string }
  | { kind: "reject"; payoutId: string; reason: string };

/** The firm approves a payout, marks it as paid or rejects it. Each answers with the payout as it is afterwards. */
export function usePayoutDecision() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (decision: PayoutDecision) => {
      const path = { params: { path: { payoutId: decision.payoutId } } };
      switch (decision.kind) {
        case "approve":
          return resultOf(await api.POST("/api/portal/admin/payouts/{payoutId}/approve", path), "the approval");
        case "mark-paid":
          return resultOf(
            await api.POST("/api/portal/admin/payouts/{payoutId}/mark-paid", { ...path, body: { reference: decision.reference || null } }),
            "the payment",
          );
        case "reject":
          return resultOf(
            await api.POST("/api/portal/admin/payouts/{payoutId}/reject", { ...path, body: { reason: decision.reason || null } }),
            "the rejection",
          );
      }
    },
    onSettled: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: ["firm-payouts"] }),
        queryClient.invalidateQueries({ queryKey: ["firm-account"] }),
        queryClient.invalidateQueries({ queryKey: ["history"] }),
      ]),
  });
}

/** An invitation for the account's trader to choose a password for the portal. */
export function useInvite(accountId: string) {
  return useMutation({
    mutationFn: async () => resultOf(await api.POST("/api/portal/admin/accounts/{accountId}/invite", accountPath(accountId)), "the invitation"),
  });
}
