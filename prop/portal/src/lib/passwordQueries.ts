import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { ApiError, api, resultOf } from "./api/client";
import type { LinkCheck } from "./api/types";
import { LoginFailedError, type Role } from "./queries";

// A forgotten password and the links in emails (finding 1 and 20 of the walkthrough): for a firm's traders and
// administrators on its portal, and for our staff on our admin view.

/** Whose password: one of the firm's two roles on its portal, or our staff on our admin view. */
export type PasswordAudience = Role | "staff";

const resetPath = {
  trader: "/api/portal/password-reset",
  admin: "/api/portal/admin/password-reset",
  staff: "/api/portal/ops/password-reset",
} as const;

const checkPath = {
  trader: "/api/portal/password-reset/check",
  admin: "/api/portal/admin/password-reset/check",
  staff: "/api/portal/ops/password-reset/check",
} as const;

const confirmPath = {
  trader: "/api/portal/password-reset/confirm",
  admin: "/api/portal/admin/password-reset/confirm",
  staff: "/api/portal/ops/password-reset/confirm",
} as const;

/** Asks for an email with a link to choose a new password. The answer is the same whether the email is known or not. */
export function useRequestPasswordReset(audience: PasswordAudience) {
  return useMutation({
    mutationFn: async (email: string) => {
      const result = await api.POST(resetPath[audience], { body: { email } });
      if (result.response.status === 429) {
        throw new LoginFailedError(true);
      }

      if (!result.response.ok) {
        throw new ApiError("The link could not be sent right now. Try again shortly.", result.response.status);
      }
    },
  });
}

/** Whether a link from an email works. Null for a link that does not exist. */
export function useLinkCheck(kind: "reset" | "invite", audience: PasswordAudience, token: string | null) {
  return useQuery({
    queryKey: ["link-check", kind, audience, token],
    enabled: token !== null,
    queryFn: async (): Promise<LinkCheck | null> => {
      const body = { token: token ?? "" };
      const result =
        kind === "reset"
          ? await api.POST(checkPath[audience], { body })
          : audience === "admin"
            ? await api.POST("/api/portal/admin/invites/check", { body })
            : await api.POST("/api/portal/invites/check", { body });
      return result.response.status === 404 ? null : resultOf(result, "the link");
    },
    staleTime: Infinity,
    retry: false,
  });
}

/**
 * Asks for an email with a login link to each firm the email administers, for an administrator who does not remember the
 * firm's address. The answer is the same whether the email is known or not.
 */
export function useLoginHelp() {
  return useMutation({
    mutationFn: async (email: string) => {
      const result = await api.POST("/api/portal/login-help", { body: { email } });
      if (result.response.status === 429) {
        throw new LoginFailedError(true);
      }

      if (!result.response.ok) {
        throw new ApiError("The email could not be sent right now. Try again shortly.", result.response.status);
      }
    },
  });
}

const meKeyOf: Record<PasswordAudience, readonly string[]> = { trader: ["me", "trader"], admin: ["me", "admin"], staff: ["ops-me"] };

/** Chooses the new password with the link, and logs the person in. */
export function useConfirmPasswordReset(audience: PasswordAudience) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { token: string; password: string }) => {
      const result = await api.POST(confirmPath[audience], { body });
      if (result.response.status === 429) {
        throw new LoginFailedError(true);
      }

      return resultOf(result, "the new password");
    },
    onSuccess: (me) => {
      queryClient.clear();
      queryClient.setQueryData(meKeyOf[audience], me);
    },
  });
}
