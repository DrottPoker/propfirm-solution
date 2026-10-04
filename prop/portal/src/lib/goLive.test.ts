import { describe, expect, it } from "vitest";

import type { Billing, ChallengePrice, FirmSettings } from "./api/types";
import { goLiveSteps } from "./goLive";
import { testChallenge } from "./testAccounts";

const settings: FirmSettings = {
  id: "acme",
  name: "Acme Trading",
  status: "Sandbox",
  portalUrl: "http://acme.localhost:3002/",
  logoUrl: null,
  colors: {},
  tradingServer: "acme",
  currency: "USD",
  hasApiKey: false,
  webhookUrl: null,
  sandboxMaxOpenAccounts: 5,
  payments: {
    provider: null,
    active: false,
    hasStripeKeys: false,
    stripeTestMode: null,
    stripeWebhookUrl: "http://localhost:5201/api/payments/stripe/acme",
    stripeWebhookEvents: ["checkout.session.completed"],
    checkoutUrl: null,
    termsUrl: null,
    testPaymentsAllowed: true,
  },
  emailSettings: {},
  firmApiUrl: "http://localhost:5201/api/firm/v1/",
  openApiUrl: "http://localhost:5201/openapi/v1.json",
  supportEmail: null,
};

const billing = {
  status: "Sandbox",
  review: null,
  depositPaid: 0,
  shopProblem: null,
  prices: { currency: "USD", startupFee: 700, reviewDeposit: 200, packagePrice: 500, packageSlots: 25, slotPrices: [], maxSlots: 10_000, chargeDaysBeforeMonth: 5, warningPercent: 80 },
} as unknown as Billing;

const price: ChallengePrice = { challengeId: testChallenge.id, amount: 99, currency: "USD", forSale: true };

function steps(changes: { settings?: Partial<FirmSettings>; billing?: Partial<Billing>; prices?: ChallengePrice[]; accounts?: number } = {}) {
  return goLiveSteps({
    settings: { ...settings, ...changes.settings },
    challenges: [testChallenge],
    prices: changes.prices ?? [],
    billing: { ...billing, ...changes.billing },
    accounts: changes.accounts ?? 0,
  });
}

describe("goLiveSteps", () => {
  it("has eight steps with the first one still to do as the current one", () => {
    const list = steps();

    expect(list.map((s) => [s.key, s.status])).toEqual([
      ["server", "done"],
      ["challenges", "done"],
      ["prices", "current"],
      ["checkout", "todo"],
      ["design", "todo"],
      ["try", "todo"],
      ["review", "todo"],
      ["live", "locked"],
    ]);
    expect(list[1].detail).toBe("Two-step 100K. Change the rules or add more whenever you like.");
    expect(list[6].detail).toBe("Company details, owners and links. You pay a 200.00 USD deposit when you send it, taken off the startup fee. We usually answer within a day.");
  });

  it("counts test payments as enough to try, and says at going live what it needs", () => {
    const problem = "Your shop takes test payments, which stop when you go live.";
    const testPayments = { payments: { ...settings.payments, provider: "Test" as const, active: true } };

    const trying = steps({ prices: [price], settings: testPayments, billing: { shopProblem: problem } });
    const approved = steps({ prices: [price], settings: testPayments, billing: { shopProblem: problem, review: "Approved" } });

    expect(trying.find((s) => s.key === "checkout")).toMatchObject({ status: "done", detail: expect.stringContaining("enough to try") });
    expect(trying.find((s) => s.key === "live")).toMatchObject({ status: "locked", detail: expect.stringContaining("takes real payments") });
    expect(approved.find((s) => s.key === "live")).toMatchObject({ detail: problem, action: { label: "Set up checkout", href: "/admin/checkout" } });
  });

  it("counts test payments where they go on working when the firm is live", () => {
    const list = steps({ prices: [price], settings: { payments: { ...settings.payments, provider: "Test", active: true } } });

    expect(list.find((s) => s.key === "checkout")).toMatchObject({ status: "done", detail: expect.stringContaining("go on working") });
  });

  it("waits for our review, and opens going live once we have approved the firm", () => {
    const done = { prices: [price], settings: { logoUrl: "/api/portal/logo/ab", payments: { ...settings.payments, provider: "Stripe" as const, active: true } }, accounts: 1 };

    const waiting = steps({ ...done, billing: { review: "Submitted", depositPaid: 200 } });
    const approved = steps({ ...done, billing: { review: "Approved", depositPaid: 200 } });

    expect(waiting.slice(-2).map((s) => s.status)).toEqual(["waiting", "locked"]);
    expect(approved.slice(-2).map((s) => s.status)).toEqual(["done", "current"]);
    expect(approved[7].action).toEqual({ label: "Go live", href: "/admin/go-live?step=payment" });
  });

  it("asks for the changes we requested", () => {
    expect(steps({ billing: { review: "ChangesRequested" } }).find((s) => s.key === "review")).toMatchObject({
      status: "todo",
      action: { label: "Make the changes", href: "/admin/go-live?step=details" },
    });
  });
});
