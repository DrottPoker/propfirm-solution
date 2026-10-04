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
    checkoutUrl: null,
    termsUrl: null,
    testPaymentsAllowed: true,
  },
};

const billing = {
  status: "Sandbox",
  review: null,
  depositPaid: 0,
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
    expect(list[1].detail).toBe("Two-step 100000 USD. Change the rules or add more whenever you like.");
    expect(list[6].detail).toBe("Company details, owners and links. You pay a 200.00 USD deposit when you send it, taken off the startup fee. We usually answer within a day.");
  });

  it("counts test payments as a start, but not as a way to take payment for real", () => {
    const list = steps({ prices: [price], settings: { payments: { ...settings.payments, provider: "Test", active: true } } });

    expect(list.find((s) => s.key === "prices")?.status).toBe("done");
    expect(list.find((s) => s.key === "checkout")).toMatchObject({ status: "current", detail: expect.stringContaining("Test payments are on now.") });
  });

  it("waits for our review, and opens going live once we have approved the firm", () => {
    const done = { prices: [price], settings: { logoUrl: "/api/portal/logo/ab", payments: { ...settings.payments, provider: "Stripe" as const, active: true } }, accounts: 1 };

    const waiting = steps({ ...done, billing: { review: "Submitted", depositPaid: 200 } });
    const approved = steps({ ...done, billing: { review: "Approved", depositPaid: 200 } });

    expect(waiting.slice(-2).map((s) => s.status)).toEqual(["waiting", "locked"]);
    expect(approved.slice(-2).map((s) => s.status)).toEqual(["done", "current"]);
    expect(approved[7].action).toEqual({ label: "Go live", href: "/admin/billing" });
  });

  it("asks for the changes we requested", () => {
    expect(steps({ billing: { review: "ChangesRequested" } }).find((s) => s.key === "review")).toMatchObject({
      status: "todo",
      action: { label: "Make the changes", href: "/admin/verification" },
    });
  });
});
