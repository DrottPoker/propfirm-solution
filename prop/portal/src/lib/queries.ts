import { keepPreviousData, type QueryClient, useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { ApiError, api, ensureOk, resultOf } from "./api/client";
import type {
  AccountGroup,
  ChallengeDefinition,
  FirmApplication,
  FirmSettings,
  FirmDocument,
  FirmStatus,
  OrderStatus,
  PaymentProvider,
  PayoutMethod,
  PayoutStatus,
  SupportTicket,
  SupportTicketGroup,
  TraderSummary,
  TradingSymbolRequest,
  Verification,
  VerificationResponse,
} from "./api/types";
import type { SupportViewer } from "./support";

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

/**
 * A trader who already chose a password, on an order's page, opens the link from the email: it confirms the email and
 * logs the trader in.
 */
export function useConfirmInvite() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (token: string) => {
      const result = await api.POST("/api/portal/invites/confirm", { body: { token } });
      if (result.response.status === 401) {
        throw new ApiError("This link has expired or was already used. Log in, and ask for a new one in the portal.", 401);
      }

      return resultOf(result, "the confirmation");
    },
    onSuccess: (me) => {
      queryClient.clear();
      queryClient.setQueryData(meKey("trader"), me);
    },
  });
}

/** Emails the logged-in trader a new link that confirms the email. */
export function useSendEmailConfirmation() {
  return useMutation({
    mutationFn: async () => {
      const result = await api.POST("/api/portal/me/confirm-email");
      if (!result.response.ok) {
        resultOf(result, "the email");
      }
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

/** The logged in trader's challenge accounts, valued at the latest prices. */
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

/**
 * How a stage of the account has gone, or the latest stage without one: the trader's own, or for the firm any of its
 * accounts. Asked again only when the account's history version changes, and the last answer stays on screen while
 * the next one loads.
 */
export function usePerformance(accountId: string, stage: number | null, historyVersion: string, role: Role = "trader") {
  return useQuery({
    queryKey: ["performance", role, accountId, stage, historyVersion],
    queryFn: async () => {
      const request = { params: { path: { accountId }, query: { stage: stage ?? undefined } } };
      return resultOf(
        role === "admin"
          ? await api.GET("/api/portal/admin/accounts/{accountId}/performance", request)
          : await api.GET("/api/portal/accounts/{accountId}/performance", request),
        "the account's history",
      );
    },
    staleTime: Infinity,
    placeholderData: keepPreviousData,
  });
}

/** The stage's closed positions, newest first, a page at a time. Asked again when the history version changes. */
export function useTrades(accountId: string, stage: number | null, historyVersion: string, role: Role = "trader") {
  return useInfiniteQuery({
    queryKey: ["trades", role, accountId, stage, historyVersion],
    queryFn: async ({ pageParam }) => {
      const request = { params: { path: { accountId }, query: { stage: stage ?? undefined, before: pageParam ?? undefined, limit: tradesPerPage } } };
      return resultOf(
        role === "admin"
          ? await api.GET("/api/portal/admin/accounts/{accountId}/trades", request)
          : await api.GET("/api/portal/accounts/{accountId}/trades", request),
        "the closed trades",
      );
    },
    initialPageParam: null as number | null,
    getNextPageParam: (page) => page.next,
    staleTime: Infinity,
    placeholderData: keepPreviousData,
  });
}

/** Where the stage's closed positions are as a CSV file, for the trader or the firm. */
export function tradesCsvUrl(accountId: string, stage: number, role: Role = "trader"): string {
  return `/api/portal/${role === "admin" ? "admin/" : ""}accounts/${accountId}/trades.csv?stage=${stage}`;
}

/** How many closed trades are shown at first, and added by each "Show more". */
export const tradesPerPage = 25;

/** The trader's payouts from every account, with totals per currency. */
export function useMyPayouts() {
  return useQuery({
    queryKey: ["my-payouts"],
    queryFn: async () => resultOf(await api.GET("/api/portal/payouts"), "your payouts"),
    refetchInterval: liveRefreshMs,
  });
}

/** How the trader wants to be paid, or null before the trader has said. */
export function useMyPayoutMethod() {
  return useQuery({
    queryKey: ["my-payout-method"],
    queryFn: async () => resultOf(await api.GET("/api/portal/payout-method"), "how you get paid").method,
  });
}

/** Saves how the trader wants to be paid. Payouts on their way keep the method they were asked for with. */
export function useSavePayoutMethod() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (method: PayoutMethod) => resultOf(await api.PUT("/api/portal/payout-method", { body: method }), "how you get paid").method,
    onSuccess: (method) => queryClient.setQueryData(["my-payout-method"], method),
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
        queryClient.invalidateQueries({ queryKey: ["my-payouts"] }),
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

/** The firm's challenges. Not asked while the firm's trading server is being set up, since its first challenge comes with it. */
export function useChallenges(enabled = true) {
  return useQuery({
    queryKey: ["challenges"],
    enabled,
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/challenges"), "the challenges"),
  });
}

/** What the admin panel looks for among the firm's accounts. */
export type AccountQuery = { search: string; group: AccountGroup; challengeId: string };

/** How many accounts the admin panel shows at first, and adds with each "Show more". */
export const accountsPerPage = 50;

/** The firm's accounts the search finds in the group, newest first, a page at a time, with how many are in each group. */
export function useAccountSearch(query: AccountQuery) {
  return useInfiniteQuery({
    queryKey: ["firm-accounts", query],
    queryFn: async ({ pageParam }) =>
      resultOf(
        await api.GET("/api/portal/admin/accounts", {
          params: {
            query: {
              search: query.search.trim() || undefined,
              group: query.group,
              challengeId: query.challengeId || undefined,
              before: pageParam ?? undefined,
              limit: accountsPerPage,
            },
          },
        }),
        "the accounts",
      ),
    initialPageParam: null as number | null,
    getNextPageParam: (page) => page.next,
    placeholderData: keepPreviousData,
    refetchInterval: 30_000,
  });
}

/** The firm's accounts that wait for a funded account, the newest few with how many there are. */
export function useWaitingAccounts(limit: number) {
  return useQuery({
    queryKey: ["firm-accounts", "waiting", limit],
    queryFn: async () =>
      resultOf(await api.GET("/api/portal/admin/accounts", { params: { query: { group: "AwaitingFunding", limit } } }), "the accounts waiting for you"),
    refetchInterval: 30_000,
  });
}

/** A trader's accounts at the firm, for example to see whether an email is a trader already. Not asked for an empty email. */
export function useTraderAccounts(email: string) {
  return useQuery({
    queryKey: ["firm-accounts", "trader", email],
    enabled: email.length > 0,
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/accounts", { params: { query: { search: email, limit: 20 } } }), "the trader's accounts"),
    staleTime: 30_000,
  });
}

/** How the firm is doing: its accounts, payouts, sales, pass rate, weeks and what happened lately. */
export function useAdminOverview() {
  return useQuery({
    queryKey: ["admin-overview"],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/overview"), "the overview"),
    refetchInterval: 30_000,
  });
}

/** The account's trader: their accounts at the firm, what they bought and were paid out, and the order behind the account. */
export function useTraderSummary(accountId: string) {
  return useQuery({
    queryKey: ["trader-summary", accountId],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/accounts/{accountId}/trader", accountPath(accountId)), "the trader"),
    refetchInterval: 30_000,
  });
}

/** The email the firm would send the account's trader now, so it is seen before it goes. */
export function useTraderEmailPreview(accountId: string, enabled: boolean) {
  return useQuery({
    queryKey: ["trader-email", accountId],
    enabled,
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/accounts/{accountId}/email-trader", accountPath(accountId)), "the email"),
  });
}

/** Ticks one of the firm's checks of the account's trader, such as that their ID was seen, or takes the tick away. */
export function useSetTraderCheck(accountId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (check: { item: string; checked: boolean }) =>
      resultOf(
        await api.PUT("/api/portal/admin/accounts/{accountId}/trader/checks/{item}", {
          params: { path: { accountId, item: check.item } },
          body: { checked: check.checked },
        }),
        "the check",
      ),
    // The answer has every check, so the card shows them at once.
    onSuccess: (checks) => queryClient.setQueryData<TraderSummary>(["trader-summary", accountId], (old) => (old ? { ...old, checks } : old)),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ["firm-payouts"] }),
  });
}

/** Emails the account's trader in the firm's name: an invitation to choose a password, or that the challenge has started. */
export function useEmailTrader() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (accountId: string) =>
      resultOf(await api.POST("/api/portal/admin/accounts/{accountId}/email-trader", accountPath(accountId)), "the email"),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ["trader-summary"] }),
  });
}

/** Each challenge's open accounts, those started in the last 30 days and its pass rate in the last 90. */
export function useChallengeFigures() {
  return useQuery({
    queryKey: ["challenge-figures"],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/challenges/figures"), "the challenges' figures"),
    refetchInterval: 60_000,
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
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: ["firm-accounts"] }),
        queryClient.invalidateQueries({ queryKey: ["admin-overview"] }),
        queryClient.invalidateQueries({ queryKey: ["billing"] }),
      ]),
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
        queryClient.invalidateQueries({ queryKey: ["admin-overview"] }),
        queryClient.invalidateQueries({ queryKey: ["trader-summary"] }),
      ]),
  });
}

/** The firm's payouts with the statuses, or all: the oldest first for a queue the firm works through, otherwise the newest. */
export function useAdminPayouts(statuses: PayoutStatus[], oldestFirst: boolean) {
  return useQuery({
    queryKey: ["firm-payouts", statuses, oldestFirst],
    queryFn: async () =>
      resultOf(await api.GET("/api/portal/admin/payouts", { params: { query: { status: statuses, oldestFirst, limit: 200 } } }), "the payouts"),
    refetchInterval: liveRefreshMs,
  });
}

/** The payouts to approve and to pay, and those paid in the last 30 days. */
export function usePayoutSummary() {
  return useQuery({
    queryKey: ["payout-summary"],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/payouts/summary"), "the payouts"),
    refetchInterval: 15_000,
  });
}

export type PayoutDecision =
  | { kind: "approve"; payoutId: string }
  | { kind: "mark-paid"; payoutId: string; reference: string }
  | { kind: "reject"; payoutId: string; reason: string; returnProfit: boolean };

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
            await api.POST("/api/portal/admin/payouts/{payoutId}/reject", { ...path, body: { reason: decision.reason || null, returnProfit: decision.returnProfit } }),
            "the rejection",
          );
      }
    },
    onSettled: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: ["firm-payouts"] }),
        queryClient.invalidateQueries({ queryKey: ["payout-summary"] }),
        queryClient.invalidateQueries({ queryKey: ["admin-overview"] }),
        queryClient.invalidateQueries({ queryKey: ["firm-account"] }),
        queryClient.invalidateQueries({ queryKey: ["trader-summary"] }),
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

/**
 * Whether the firm is being set up, in the sandbox or live, asked again while it is not live: every two seconds while
 * its trading server is being set up, otherwise every half minute.
 */
export function useFirmStatus(status: FirmStatus) {
  return useQuery({
    queryKey: ["firm-status"],
    enabled: status !== "Live",
    queryFn: async () => resultOf(await api.GET("/api/portal/branding"), "the firm").status,
    refetchInterval: (query) => ((query.state.data ?? status) === "Provisioning" ? 2_000 : 30_000),
  });
}

/** The firm's own settings. Asked again every few seconds while its trading server is being set up. */
export function useFirmSettings() {
  return useQuery({
    queryKey: ["firm-settings"],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/firm"), "the firm's settings"),
    refetchInterval: (query) => (query.state.data?.status === "Provisioning" ? 2_000 : false),
  });
}

/** The firm's colors. The portal shows them after the next page load. */
export function useSaveColors() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (colors: Record<string, string>) => resultOf(await api.PUT("/api/portal/admin/firm/branding", { body: { colors } }), "the colors"),
    onSuccess: (settings) => queryClient.setQueryData(["firm-settings"], settings),
  });
}

/** Uploads the firm's logo, which replaces the earlier one. The service answers with what is wrong with a file it refuses. */
export function useUploadLogo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (file: File): Promise<FirmSettings> => {
      const body = new FormData();
      body.append("file", file);
      const response = await fetch("/api/portal/admin/firm/logo", { method: "PUT", body, credentials: "same-origin" });
      const answer: unknown = await response.json().catch(() => null);
      if (!response.ok) {
        throw fieldErrorOf(answer, response.status, "the logo");
      }

      return answer as FirmSettings;
    },
    onSuccess: (settings) => queryClient.setQueryData(["firm-settings"], settings),
  });
}

/** Removes the logo, so the portal shows the firm's name. */
export function useRemoveLogo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => resultOf(await api.DELETE("/api/portal/admin/firm/logo"), "the logo"),
    onSuccess: (settings) => queryClient.setQueryData(["firm-settings"], settings),
  });
}

/** The instruments on the trading platform, with the firm's conditions for those its traders trade. */
export function useTradingConditions(enabled = true) {
  return useQuery({
    queryKey: ["trading-conditions"],
    enabled,
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/trading-conditions"), "the trading conditions"),
  });
}

/** The instruments the firm's traders trade, with their conditions. They apply at once to every account. */
export function useSaveTradingConditions() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (symbols: TradingSymbolRequest[]) =>
      resultOf(await api.PUT("/api/portal/admin/trading-conditions", { body: { symbols } }), "the trading conditions"),
    onSuccess: (conditions) => queryClient.setQueryData(["trading-conditions"], conditions),
  });
}

/** Turns notification emails on or off, by kind. The kinds left out stay as they are. */
export function useSaveEmailSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (settings: Record<string, boolean>) =>
      resultOf(await api.PUT("/api/portal/admin/firm/email-settings", { body: { settings } }), "the email settings"),
    onSuccess: (settings) => queryClient.setQueryData(["firm-settings"], settings),
  });
}

/** Where replies to the emails to the firm's traders go. Empty for nowhere. */
export function useSaveSupportEmail() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (email: string) => resultOf(await api.PUT("/api/portal/admin/firm/support-email", { body: { email } }), "the support address"),
    onSuccess: (settings) => queryClient.setQueryData(["firm-settings"], settings),
  });
}

/** A new key for the firm API. It is shown once, and the old key stops working. */
export function useNewApiKey() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => resultOf(await api.POST("/api/portal/admin/firm/api-key"), "the API key").apiKey,
    onSettled: () => queryClient.invalidateQueries({ queryKey: ["firm-settings"] }),
  });
}

/** Where webhooks go, or none. The first time, the answer has the secret that signs them. */
export function useSaveWebhook() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (url: string) => resultOf(await api.PUT("/api/portal/admin/firm/webhook", { body: { url: url.trim() || null } }), "the webhook"),
    onSettled: () => Promise.all([queryClient.invalidateQueries({ queryKey: ["firm-settings"] }), queryClient.invalidateQueries({ queryKey: ["webhook"] })]),
  });
}

/** Where webhooks go, their events and the latest deliveries. Asked again every few seconds while one is being tried. */
export function useWebhookOverview() {
  return useQuery({
    queryKey: ["webhook"],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/firm/webhook"), "the webhooks"),
    refetchInterval: (query) => (query.state.data?.deliveries.some((d) => d.status === "Pending") ? 3_000 : 30_000),
  });
}

/** Sends the event webhook.test to the firm's address. */
export function useSendTestWebhook() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => ensureOk(await api.POST("/api/portal/admin/firm/webhook/test"), "the test event"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["webhook"] }),
  });
}

/** A new secret for webhooks, shown once. */
export function useNewWebhookSecret() {
  return useMutation({
    mutationFn: async () => resultOf(await api.POST("/api/portal/admin/firm/webhook/secret"), "the webhook secret").secret,
  });
}

export function useChallengeTemplates() {
  return useQuery({
    queryKey: ["challenge-templates"],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/challenge-templates"), "the challenge templates"),
    staleTime: Infinity,
  });
}

/** Creates or replaces one of the firm's challenges. Accounts already started keep their rules. */
export function useSaveChallenge() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (definition: ChallengeDefinition) => {
      const result = await api.PUT("/api/portal/admin/challenges/{challengeId}", { params: { path: { challengeId: definition.id } }, body: definition });
      if (result.data) {
        return result.data;
      }

      // An invalid challenge comes with what is wrong with it.
      const problem = (result.error ?? {}) as { title?: unknown; errors?: unknown };
      const errors = Array.isArray(problem.errors) ? problem.errors.filter((e): e is string => typeof e === "string") : [];
      throw new ApiError(
        [typeof problem.title === "string" ? problem.title : `Could not save the challenge (HTTP ${result.response.status}).`, ...errors].join(" "),
        result.response.status,
      );
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["challenges"] }),
  });
}

export function useAdmins() {
  return useQuery({
    queryKey: ["admins"],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/admins"), "the administrators"),
  });
}

/** Emails an invitation to administer the firm. */
export function useInviteAdmin() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (email: string) => resultOf(await api.POST("/api/portal/admin/admins/invites", { body: { email } }), "the invitation"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["admins"] }),
  });
}

/** Takes back the invitation to the email, so its link stops working. */
export function useWithdrawAdminInvite() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (email: string) => ensureOk(await api.POST("/api/portal/admin/admins/invites/withdraw", { body: { email } }), "the invitation"),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ["admins"] }),
  });
}

export function useRemoveAdmin() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (adminId: string) => {
      const result = await api.DELETE("/api/portal/admin/admins/{adminId}", { params: { path: { adminId } } });
      if (!result.response.ok) {
        resultOf(result, "the removal");
      }
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ["admins"] }),
  });
}

/** Logs the new firm's administrator in with the link from signing up. */
export function useWelcome() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (token: string) => {
      const result = await api.POST("/api/portal/admin/welcome", { body: { token } });
      if (result.response.status === 401) {
        throw new ApiError("This link has expired or was already used. Log in with your email and password.", 401);
      }

      return resultOf(result, "the login");
    },
    onSuccess: (me) => {
      queryClient.clear();
      queryClient.setQueryData(meKey("admin"), me);
    },
  });
}

/** An invited administrator chooses a password, and is logged in. */
export function useAcceptAdminInvite() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { token: string; password: string }) => {
      const result = await api.POST("/api/portal/admin/invites/accept", { body });
      if (result.response.status === 401) {
        throw new ApiError("This invitation has expired or was already used. Ask for a new one.", 401);
      }

      if (result.response.status === 429) {
        throw new LoginFailedError(true);
      }

      return resultOf(result, "the invitation");
    },
    onSuccess: (me) => {
      queryClient.clear();
      queryClient.setQueryData(meKey("admin"), me);
    },
  });
}

/** Whether the short name can be chosen. Asked while it is typed. */
export function useAvailability(firmId: string) {
  return useQuery({
    queryKey: ["availability", firmId],
    enabled: firmId.length > 0,
    queryFn: async () => resultOf(await api.GET("/api/portal/signup/availability", { params: { query: { firmId } } }), "the name check"),
  });
}

/** The sign-up was refused because of one of the fields, named as in the form. */
export class SignupError extends ApiError {
  constructor(
    message: string,
    status: number,
    readonly field: string | null,
  ) {
    super(message, status);
    this.name = "SignupError";
  }
}

export type SignupForm = { firmName: string; firmId: string; email: string; password: string; acceptTerms: boolean; currency: string };

export function useSignUp() {
  return useMutation({
    mutationFn: async (body: SignupForm) => {
      const result = await api.POST("/api/portal/signup", { body });
      if (result.data) {
        return result.data;
      }

      throw signupErrorOf(result.error, result.response.status);
    },
  });
}

/** Confirms the email address and creates the firm. Answers with the link into its admin panel. */
export function useVerifySignup() {
  return useMutation({
    mutationFn: async (token: string) => {
      const result = await api.POST("/api/portal/signup/verify", { body: { token } });
      if (result.data) {
        return result.data;
      }

      if (result.response.status === 401) {
        throw new ApiError("This link has expired or was already used. Sign up again to get a new one.", 401);
      }

      throw signupErrorOf(result.error, result.response.status);
    },
  });
}

function signupErrorOf(error: unknown, status: number): SignupError {
  const problem = typeof error === "object" && error !== null ? (error as { title?: unknown; field?: unknown }) : {};
  return new SignupError(
    typeof problem.title === "string" ? problem.title : status === 429 ? "Too many attempts. Wait a minute and try again." : `Could not sign up (HTTP ${status}).`,
    status,
    typeof problem.field === "string" ? problem.field : null,
  );
}

/** What the firm's portal sells, for anyone on it. Not asked where only administrators look. */
export function useShop(enabled = true) {
  return useQuery({
    queryKey: ["shop"],
    enabled,
    queryFn: async () => resultOf(await api.GET("/api/portal/shop"), "the challenges for sale"),
  });
}

/** Buys a challenge, with a discount code when one was given. The answer says where to pay; a logged-in trader buys with their own email. */
export function useCreateOrder() {
  return useMutation({
    mutationFn: async (body: {
      challengeId: string;
      email: string | null;
      acceptTerms: boolean;
      name: string | null;
      country: string | null;
      discountCode: string | null;
    }) => resultOf(await api.POST("/api/portal/orders", { body }), "the order"),
  });
}

/** What the challenge costs with the discount code the buyer typed, or why the code does not apply. */
export function useDiscountQuote() {
  return useMutation({
    mutationFn: async (body: { code: string; challengeId: string; email: string | null }) =>
      resultOf(await api.POST("/api/portal/shop/discount", { body }), "the code"),
  });
}

/** The firm's own domain for its portal and the DNS records it needs. */
export function useDomain() {
  return useQuery({
    queryKey: ["domain"],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/domain"), "your domain"),
  });
}

export function useSaveDomain() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (domain: string) => resultOf(await api.PUT("/api/portal/admin/domain", { body: { domain } }), "your domain"),
    onSuccess: (domain) => queryClient.setQueryData(["domain"], domain),
  });
}

/** Looks the domain's records up now, instead of waiting for the next look. */
export function useCheckDomain() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => resultOf(await api.POST("/api/portal/admin/domain/check"), "your domain"),
    onSuccess: (domain) =>
      Promise.all([queryClient.setQueryData(["domain"], domain), queryClient.invalidateQueries({ queryKey: ["firm-settings"] })]),
  });
}

export function useRemoveDomain() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => resultOf(await api.DELETE("/api/portal/admin/domain"), "your domain"),
    onSuccess: (domain) =>
      Promise.all([queryClient.setQueryData(["domain"], domain), queryClient.invalidateQueries({ queryKey: ["firm-settings"] })]),
  });
}

/** The firm's discount codes, with how often each was used. */
export function useDiscounts() {
  return useQuery({
    queryKey: ["discounts"],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/discounts"), "the discount codes"),
  });
}

export type DiscountForm = {
  code: string;
  percentOff: number | null;
  amountOff: number | null;
  currency: string | null;
  challengeIds: string[] | null;
  maxUses: number | null;
  expiresAt: string | null;
  forRetries: boolean;
};

export function useCreateDiscount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: DiscountForm) => resultOf(await api.POST("/api/portal/admin/discounts", { body }), "the code"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["discounts"] }),
  });
}

/** Turns a code on or off, or removes one no order has used. */
export function useDiscountCommand() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (command: { codeId: string; kind: "active"; active: boolean } | { codeId: string; kind: "delete" }) => {
      const path = { params: { path: { codeId: command.codeId } } };
      return command.kind === "delete"
        ? ensureOk(await api.DELETE("/api/portal/admin/discounts/{codeId}", path), "the code")
        : ensureOk(await api.PUT("/api/portal/admin/discounts/{codeId}/active", { ...path, body: { active: command.active } }), "the code");
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ["discounts"] }),
  });
}

/** The buyer chooses the password right on the order's page, and is logged in. */
export function useChooseOrderPassword(orderId: string, token: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (password: string) =>
      resultOf(await api.POST("/api/portal/orders/{orderId}/password", { params: { path: { orderId } }, body: { token, password } }), "the password"),
    onSuccess: (me) => {
      queryClient.clear();
      queryClient.setQueryData(meKey("trader"), me);
    },
  });
}

/** The buyer's order, with the token from the link to it. Asked again every two seconds while it waits for the payment. */
export function useBuyerOrder(orderId: string, token: string) {
  return useQuery({
    queryKey: ["buyer-order", orderId],
    enabled: orderId.length > 0 && token.length > 0,
    queryFn: async () =>
      resultOf(await api.GET("/api/portal/orders/{orderId}", { params: { path: { orderId }, query: { token } } }), "the order"),
    refetchInterval: (query) => (query.state.data?.status === "Pending" ? 2_000 : false),
  });
}

/** Pays a test order without money. */
export function useTestPayment(orderId: string, token: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () =>
      resultOf(await api.POST("/api/portal/orders/{orderId}/test-payment", { params: { path: { orderId } }, body: { token } }), "the payment"),
    onSuccess: (order) => queryClient.setQueryData(["buyer-order", orderId], order),
  });
}

/** Emails the buyer the invitation to the portal again. */
export function useResendInvite(orderId: string, token: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const result = await api.POST("/api/portal/orders/{orderId}/invite", { params: { path: { orderId } }, body: { token } });
      if (!result.response.ok) {
        resultOf(result, "the invitation");
      }
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ["buyer-order", orderId] }),
  });
}

/** The firm's newest orders, all of them or only those with the status. */
export function useFirmOrders(status: OrderStatus | null) {
  return useQuery({
    queryKey: ["firm-orders", status],
    queryFn: async () =>
      resultOf(await api.GET("/api/portal/admin/orders", { params: { query: { status: status ?? undefined, limit: 200 } } }), "the orders"),
    refetchInterval: liveRefreshMs,
  });
}

export type OrderDecision = { kind: "mark-paid" | "mark-refunded"; orderId: string; reference: string };

/** The firm marks an order from its own checkout as paid, or a paid order as refunded. */
export function useOrderDecision() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (decision: OrderDecision) => {
      const request = { params: { path: { orderId: decision.orderId } }, body: { reference: decision.reference || null } };
      return decision.kind === "mark-paid"
        ? resultOf(await api.POST("/api/portal/admin/orders/{orderId}/mark-paid", request), "the payment")
        : resultOf(await api.POST("/api/portal/admin/orders/{orderId}/mark-refunded", request), "the refund");
    },
    onSettled: () =>
      Promise.all([queryClient.invalidateQueries({ queryKey: ["firm-orders"] }), queryClient.invalidateQueries({ queryKey: ["firm-accounts"] })]),
  });
}

/** What the firm's challenges sell for in the portal. */
export function usePrices() {
  return useQuery({
    queryKey: ["prices"],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/prices"), "the prices"),
  });
}

export function useSavePrice() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (price: { challengeId: string; amount: number; currency: string; forSale: boolean }) =>
      resultOf(
        await api.PUT("/api/portal/admin/challenges/{challengeId}/price", {
          params: { path: { challengeId: price.challengeId } },
          body: { amount: price.amount, currency: price.currency, forSale: price.forSale },
        }),
        "the price",
      ),
    onSuccess: () => Promise.all([queryClient.invalidateQueries({ queryKey: ["prices"] }), queryClient.invalidateQueries({ queryKey: ["shop"] })]),
  });
}

export type PaymentSettingsForm = {
  provider: PaymentProvider | null;
  stripeSecretKey: string;
  stripeWebhookSecret: string;
  checkoutUrl: string;
  termsUrl: string;
};

/** How the portal takes payment. Empty Stripe keys keep the saved ones. */
export function useSavePayments() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (form: PaymentSettingsForm) =>
      resultOf(
        await api.PUT("/api/portal/admin/firm/payments", {
          body: {
            provider: form.provider,
            stripeSecretKey: form.stripeSecretKey.trim() || null,
            stripeWebhookSecret: form.stripeWebhookSecret.trim() || null,
            checkoutUrl: form.checkoutUrl.trim() || null,
            termsUrl: form.termsUrl.trim() || null,
          },
        }),
        "the payment settings",
      ),
    // The terms are shared with the application.
    onSuccess: (settings) => {
      queryClient.setQueryData(["firm-settings"], settings);
      return Promise.all([queryClient.invalidateQueries({ queryKey: ["shop"] }), queryClient.invalidateQueries({ queryKey: ["verification"] })]);
    },
  });
}

/** What the firm pays us: its slots, card, charges and the prices. Asked every two seconds while a payment is being confirmed. */
export function useBilling(waitingForPayment = false) {
  return useQuery({
    queryKey: ["billing"],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/billing"), "the billing"),
    refetchInterval: waitingForPayment ? 2_000 : 15_000,
  });
}

/** What choosing this many slots would cost now and each month, and with expandBy what one round of automatic expansion adds. */
export function useBillingQuote(slots: number | null, expandBy: number | null = null) {
  return useQuery({
    queryKey: ["billing-quote", slots, expandBy],
    enabled: slots !== null,
    queryFn: async () =>
      resultOf(
        await api.GET("/api/portal/admin/billing/quote", { params: { query: { slots: slots ?? 0, ...(expandBy ? { expandBy } : {}) } } }),
        "the price",
      ),
  });
}

/** Starts going live. The answer is the page where the firm pays. */
export function useActivate() {
  return useMutation({
    mutationFn: async (body: { slots: number; autoExpandStep: number | null }) =>
      resultOf(await api.POST("/api/portal/admin/billing/activate", { body }), "the payment page"),
  });
}

/** More slots are paid now, fewer apply from the next unpaid month. */
export function useSetSlots() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (slots: number) => resultOf(await api.PUT("/api/portal/admin/billing/slots", { body: { slots } }), "the slots"),
    onSuccess: (billing) => queryClient.setQueryData(["billing"], billing),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ["billing-quote"] }),
  });
}

/** How many slots are bought when the last free one is taken, or null for none. */
export function useSetAutoExpand() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (step: number | null) => resultOf(await api.PUT("/api/portal/admin/billing/auto-expand", { body: { step } }), "the automatic expansion"),
    onSuccess: (billing) => queryClient.setQueryData(["billing"], billing),
  });
}

/** A page that saves a new card. */
export function useChangeCard() {
  return useMutation({
    mutationFn: async () => resultOf(await api.POST("/api/portal/admin/billing/card"), "the card page"),
  });
}

/** A page where an unpaid month is paid, with any card. */
export function usePayCharge() {
  return useMutation({
    mutationFn: async (chargeId: string) =>
      resultOf(await api.POST("/api/portal/admin/billing/charges/{chargeId}/checkout", { params: { path: { chargeId } } }), "the payment page"),
  });
}

/** Tries an unpaid month on the saved card now. */
export function useRetryCharge() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (chargeId: string) =>
      resultOf(await api.POST("/api/portal/admin/billing/charges/{chargeId}/retry", { params: { path: { chargeId } } }), "the payment"),
    onSuccess: (billing) => queryClient.setQueryData(["billing"], billing),
    onError: () => queryClient.invalidateQueries({ queryKey: ["billing"] }),
  });
}

/** A test page for paying the platform or saving a card, while payments to the platform are test payments. */
export function useTestBillingCheckout(checkoutId: string) {
  return useQuery({
    queryKey: ["billing-checkout", checkoutId],
    queryFn: async () =>
      resultOf(await api.GET("/api/portal/admin/billing/checkouts/{checkoutId}", { params: { path: { checkoutId } } }), "the payment page"),
  });
}

/** Pays or saves a card on a test page, with a test card that pays or one that declines. */
export function useCompleteTestBillingCheckout(checkoutId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (declines: boolean) => {
      const result = await api.POST("/api/portal/admin/billing/checkouts/{checkoutId}/complete", {
        params: { path: { checkoutId } },
        body: { declines },
      });
      if (!result.response.ok) {
        resultOf(result, "the payment");
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["billing"] }),
  });
}

/**
 * Our review of the firm: its application, documents and deposit. Asked every two seconds while the deposit is being
 * confirmed, and every half minute while the application waits for our answer.
 */
export function useVerification(waitingForPayment = false) {
  return useQuery({
    queryKey: ["verification"],
    queryFn: async () => verificationOf(resultOf(await api.GET("/api/portal/admin/verification"), "the review")),
    refetchInterval: (query) =>
      waitingForPayment && query.state.data?.deposit.paid === false ? 2_000 : query.state.data?.status === "Submitted" ? 30_000 : false,
  });
}

/** The service said no because of one of the application's fields, named as in the application. */
export class FieldError extends ApiError {
  constructor(
    message: string,
    status: number,
    readonly field: string | null,
  ) {
    super(message, status);
    this.name = "FieldError";
  }
}

/** Saves the application as a draft. */
export function useSaveApplication() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (application: FirmApplication) => saveApplication(application),
    // The terms are the firm's, shared with the shop.
    onSuccess: (verification) => {
      queryClient.setQueryData(["verification"], verification);
      return queryClient.invalidateQueries({ queryKey: ["firm-settings"] });
    },
  });
}

/**
 * Saves the application and sends it, so what is sent is what the form shows. The answer has the page where the
 * firm pays the deposit, or none when the application was sent at once.
 */
export function useSubmitApplication() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (application: FirmApplication) => {
      queryClient.setQueryData(["verification"], await saveApplication(application));
      const result = await api.POST("/api/portal/admin/verification/submit");
      if (result.data) {
        return result.data;
      }

      throw fieldErrorOf(result.error, result.response.status, "the application");
    },
    onSuccess: async (submitted) => {
      if (!submitted.checkoutUrl) {
        await Promise.all([queryClient.invalidateQueries({ queryKey: ["verification"] }), queryClient.invalidateQueries({ queryKey: ["billing"] })]);
      }
    },
  });
}

/** Adds a document to the application. The service knows its format from its content. */
export function useUploadDocument() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (file: File): Promise<FirmDocument> => {
      const body = new FormData();
      body.append("file", file);
      const response = await fetch("/api/portal/admin/verification/documents", { method: "POST", body, credentials: "same-origin" });
      const answer: unknown = await response.json().catch(() => null);
      if (!response.ok) {
        throw fieldErrorOf(answer, response.status, "the document");
      }

      return answer as FirmDocument;
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ["verification"] }),
  });
}

export function useRemoveDocument() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (documentId: string) => {
      const result = await api.DELETE("/api/portal/admin/verification/documents/{documentId}", { params: { path: { documentId } } });
      if (!result.response.ok) {
        resultOf(result, "the removal");
      }
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ["verification"] }),
  });
}

// A firm always has a review status here; the schema allows none only because it is shared.
function verificationOf(response: VerificationResponse): Verification {
  return { ...response, status: response.status ?? "Draft" };
}

async function saveApplication(application: FirmApplication): Promise<Verification> {
  const result = await api.PUT("/api/portal/admin/verification/application", { body: application });
  if (result.data) {
    return verificationOf(result.data);
  }

  throw fieldErrorOf(result.error, result.response.status, "the application");
}

export function fieldErrorOf(error: unknown, status: number, what: string): FieldError {
  const problem = typeof error === "object" && error !== null ? (error as { title?: unknown; field?: unknown }) : {};
  return new FieldError(
    typeof problem.title === "string"
      ? problem.title
      : status === 413
        ? "The file is too large. A document can be at most 10 MB."
        : `Could not save ${what} (HTTP ${status}).`,
    status,
    typeof problem.field === "string" ? problem.field : null,
  );
}

// Support tickets (ADR 0041). The trader's are under "mine" and the firm's under "firm", so a change to one refreshes
// only the lists, counts and ticket that show it.
const supportScope = (viewer: SupportViewer) => (viewer === "admin" ? "firm" : "mine");
const ticketKey = (viewer: SupportViewer, ticketId: string) => ["support", supportScope(viewer), "ticket", ticketId];

const ticketsPerPage = 50;

function refreshTickets(queryClient: QueryClient, viewer: SupportViewer) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: ["support", supportScope(viewer), "tickets"] }),
    queryClient.invalidateQueries({ queryKey: ["support", supportScope(viewer), "summary"] }),
  ]);
}

/** The trader's tickets that are not closed, and those with an answer the trader has not read, for the portal's menu. */
export function useMySupportSummary() {
  return useQuery({
    queryKey: ["support", "mine", "summary"],
    queryFn: async () => resultOf(await api.GET("/api/portal/support/summary"), "your tickets"),
    refetchInterval: 30_000,
  });
}

/** The trader's tickets, the latest written in first, a page at a time. */
export function useMyTickets() {
  return useInfiniteQuery({
    queryKey: ["support", "mine", "tickets"],
    queryFn: async ({ pageParam }) =>
      resultOf(await api.GET("/api/portal/support/tickets", { params: { query: { cursor: pageParam ?? undefined, limit: ticketsPerPage } } }), "your tickets"),
    initialPageParam: null as string | null,
    getNextPageParam: (page) => page.next,
    refetchInterval: 30_000,
  });
}

/** One of the trader's tickets with every message. Asked again while it is shown, so an answer appears by itself. */
export function useMyTicket(ticketId: string) {
  return useQuery({
    queryKey: ticketKey("trader", ticketId),
    queryFn: async () => resultOf(await api.GET("/api/portal/support/tickets/{ticketId}", { params: { path: { ticketId } } }), "the ticket"),
    refetchInterval: 15_000,
  });
}

/** The firm's tickets that wait for it, since when the oldest has waited, and those that wait for the trader. */
export function useFirmSupportSummary() {
  return useQuery({
    queryKey: ["support", "firm", "summary"],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/support/summary"), "the support tickets"),
    refetchInterval: 30_000,
  });
}

/** The firm's tickets in a group, found by the search, a page at a time, with the counts in every group. */
export function useFirmTickets(query: { group: SupportTicketGroup; search: string }) {
  return useInfiniteQuery({
    queryKey: ["support", "firm", "tickets", query],
    queryFn: async ({ pageParam }) =>
      resultOf(
        await api.GET("/api/portal/admin/support/tickets", {
          params: { query: { group: query.group, search: query.search.trim() || undefined, cursor: pageParam ?? undefined, limit: ticketsPerPage } },
        }),
        "the support tickets",
      ),
    initialPageParam: null as string | null,
    getNextPageParam: (page) => page.next,
    placeholderData: keepPreviousData,
    refetchInterval: 30_000,
  });
}

/** One of the firm's tickets with every message and who answered. */
export function useFirmTicket(ticketId: string) {
  return useQuery({
    queryKey: ticketKey("admin", ticketId),
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/support/tickets/{ticketId}", { params: { path: { ticketId } } }), "the ticket"),
    refetchInterval: 15_000,
  });
}

/** The trader opens a ticket. The answer is the new ticket. */
export function useOpenTicket() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (ticket: { subject: string; body: string; accountId: string | null; files: File[] }) =>
      sendSupportForm("/api/portal/support/tickets", { subject: ticket.subject, body: ticket.body, accountId: ticket.accountId ?? undefined }, ticket.files, "the ticket"),
    onSuccess: (ticket) => {
      queryClient.setQueryData(ticketKey("trader", ticket.id), ticket);
      return refreshTickets(queryClient, "trader");
    },
  });
}

/** The trader writes in a ticket, or an administrator answers it and with close closes it too. */
export function useWriteInTicket(viewer: SupportViewer, ticketId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (message: { body: string; files: File[]; close?: boolean }) =>
      sendSupportForm(
        viewer === "admin" ? `/api/portal/admin/support/tickets/${ticketId}/messages` : `/api/portal/support/tickets/${ticketId}/messages`,
        { body: message.body, close: message.close ? "true" : undefined },
        message.files,
        "the message",
      ),
    onSuccess: (ticket) => {
      queryClient.setQueryData(ticketKey(viewer, ticketId), ticket);
      return refreshTickets(queryClient, viewer);
    },
  });
}

export function useCloseTicket(viewer: SupportViewer, ticketId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const path = { params: { path: { ticketId } } };
      return viewer === "admin"
        ? resultOf(await api.POST("/api/portal/admin/support/tickets/{ticketId}/close", path), "the ticket")
        : resultOf(await api.POST("/api/portal/support/tickets/{ticketId}/close", path), "the ticket");
    },
    onSuccess: (ticket) => {
      queryClient.setQueryData(ticketKey(viewer, ticketId), ticket);
      return refreshTickets(queryClient, viewer);
    },
  });
}

/** The trader has read the ticket's answers, so the menu no longer counts it. */
export function useMarkTicketRead(ticketId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => ensureOk(await api.POST("/api/portal/support/tickets/{ticketId}/read", { params: { path: { ticketId } } }), "that you read the ticket"),
    onSuccess: () => refreshTickets(queryClient, "trader"),
  });
}

// A message and its files go as a form, in one request. The service names the field a problem is about.
async function sendSupportForm(path: string, fields: Record<string, string | undefined>, files: File[], what: string): Promise<SupportTicket> {
  const body = new FormData();
  for (const [name, value] of Object.entries(fields)) {
    if (value !== undefined) {
      body.append(name, value);
    }
  }

  for (const file of files) {
    body.append("files", file);
  }

  const response = await fetch(path, { method: "POST", body, credentials: "same-origin" });
  const answer: unknown = await response.json().catch(() => null);
  if (!response.ok) {
    // A request far too large is refused before the service sees it, without a problem to read.
    const isProblem = typeof answer === "object" && answer !== null && "title" in answer;
    throw response.status === 413 && !isProblem
      ? new FieldError("The files are too large. A file can be at most 5 MB.", 413, "files")
      : fieldErrorOf(answer, response.status, what);
  }

  return answer as SupportTicket;
}
