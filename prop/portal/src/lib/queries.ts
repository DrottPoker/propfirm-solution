import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { ApiError, api, resultOf } from "./api/client";
import type {
  ChallengeDefinition,
  ChallengeStatus,
  FirmApplication,
  FirmDocument,
  OrderStatus,
  PaymentProvider,
  PayoutStatus,
  Verification,
  VerificationResponse,
} from "./api/types";

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

/** The firm's challenges. Not asked while the firm's trading server is being set up, since its first challenge comes with it. */
export function useChallenges(enabled = true) {
  return useQuery({
    queryKey: ["challenges"],
    enabled,
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

/** The firm's own settings. Asked again every few seconds while its trading server is being set up. */
export function useFirmSettings() {
  return useQuery({
    queryKey: ["firm-settings"],
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/firm"), "the firm's settings"),
    refetchInterval: (query) => (query.state.data?.status === "Provisioning" ? 2_000 : false),
  });
}

/** The firm's logo and colors. The portal shows them after the next page load. */
export function useSaveBranding() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { logoUrl: string | null; colors: Record<string, string> }) =>
      resultOf(await api.PUT("/api/portal/admin/firm/branding", { body }), "the look"),
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
    onSettled: () => queryClient.invalidateQueries({ queryKey: ["firm-settings"] }),
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

export type SignupForm = { firmName: string; firmId: string; email: string; password: string; acceptTerms: boolean };

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

/** Buys a challenge. The answer says where to pay; a logged-in trader buys with their own email. */
export function useCreateOrder() {
  return useMutation({
    mutationFn: async (body: { challengeId: string; email: string | null; acceptTerms: boolean }) =>
      resultOf(await api.POST("/api/portal/orders", { body }), "the order"),
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
    onSuccess: (settings) => {
      queryClient.setQueryData(["firm-settings"], settings);
      return queryClient.invalidateQueries({ queryKey: ["shop"] });
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

/** What choosing this many slots would cost now and each month. */
export function useBillingQuote(slots: number | null) {
  return useQuery({
    queryKey: ["billing-quote", slots],
    enabled: slots !== null,
    queryFn: async () => resultOf(await api.GET("/api/portal/admin/billing/quote", { params: { query: { slots: slots ?? 0 } } }), "the price"),
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

/** Our review of the firm: its application, documents and deposit. Asked every two seconds while the deposit is being confirmed. */
export function useVerification(waitingForPayment = false) {
  return useQuery({
    queryKey: ["verification"],
    queryFn: async () => verificationOf(resultOf(await api.GET("/api/portal/admin/verification"), "the review")),
    refetchInterval: (query) => (waitingForPayment && query.state.data?.deposit.paid === false ? 2_000 : false),
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
    onSuccess: (verification) => queryClient.setQueryData(["verification"], verification),
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
