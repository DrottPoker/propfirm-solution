import { describe, expect, it } from "vitest";

import type { BuyerOrder, Order } from "./api/types";
import { buyerStage, canMarkPaid, canMarkRefunded, orderNote } from "./orders";

const order: Order = {
  id: "0199a000-0000-7000-8000-000000000001",
  number: 1001,
  status: "Pending",
  email: "buyer@test.example",
  challengeId: "two-step-100k",
  amount: 99,
  currency: "USD",
  provider: "External",
  paymentReference: null,
  accountId: null,
  problem: null,
  createdAt: "2026-10-05T08:00:00Z",
  expiresAt: "2026-10-05T09:00:00Z",
  paidAt: null,
  refundedAt: null,
  disputedAt: null,
};

const buyerOrder: BuyerOrder = {
  id: order.id,
  number: 1001,
  status: "Paid",
  email: "buyer@test.example",
  challengeId: "two-step-100k",
  challengeName: "Two-step 100000 USD",
  amount: 99,
  currency: "USD",
  provider: "Test",
  checkoutUrl: null,
  accountId: "0199a000-0000-7000-8000-000000000002",
  problem: null,
  canLogIn: false,
  inviteSentAt: "2026-10-05T08:01:00Z",
};

describe("canMarkPaid", () => {
  it("is only for orders from the firm's own checkout that are not paid yet", () => {
    expect(canMarkPaid(order)).toBe(true);
    expect(canMarkPaid({ ...order, status: "Expired" })).toBe(true);
    expect(canMarkPaid({ ...order, status: "Paid" })).toBe(false);
    expect(canMarkPaid({ ...order, provider: "Stripe" })).toBe(false);
    expect(canMarkPaid({ ...order, provider: "Test" })).toBe(false);
  });
});

describe("canMarkRefunded", () => {
  it("is for paid orders that Stripe does not report on, once", () => {
    expect(canMarkRefunded({ ...order, status: "Paid" })).toBe(true);
    expect(canMarkRefunded({ ...order, status: "Paid", provider: "Test" })).toBe(true);
    expect(canMarkRefunded({ ...order, status: "Paid", provider: "Stripe" })).toBe(false);
    expect(canMarkRefunded({ ...order, status: "Paid", refundedAt: "2026-10-06T08:00:00Z" })).toBe(false);
    expect(canMarkRefunded(order)).toBe(false);
  });
});

describe("orderNote", () => {
  it("tells the firm about problems, disputes, refunds and the payment", () => {
    expect(orderNote(order)).toBe("");
    expect(orderNote({ ...order, disputedAt: "x", refundedAt: "y", paymentReference: "pi_1" })).toBe(
      "The buyer disputed the payment. Refunded. Payment pi_1.",
    );
    expect(orderNote({ ...order, problem: "The sandbox had no room." })).toBe("The sandbox had no room.");
  });
});

describe("buyerStage", () => {
  it("waits for the payment, and tells when the order expired", () => {
    expect(buyerStage({ ...buyerOrder, status: "Pending" })).toEqual({ kind: "waiting" });
    expect(buyerStage({ ...buyerOrder, status: "Expired" })).toEqual({ kind: "expired" });
  });

  it("sends a trader with a password to log in", () => {
    expect(buyerStage({ ...buyerOrder, canLogIn: true })).toEqual({ kind: "log-in" });
  });

  it("tells a new trader about the invitation, or lets them ask for it", () => {
    expect(buyerStage(buyerOrder)).toEqual({ kind: "invited", email: "buyer@test.example" });
    expect(buyerStage({ ...buyerOrder, inviteSentAt: null })).toEqual({ kind: "get-invite", email: "buyer@test.example" });
  });

  it("explains a paid order without an account", () => {
    expect(buyerStage({ ...buyerOrder, accountId: null, problem: "The sandbox had no room." })).toEqual({
      kind: "problem",
      problem: "The sandbox had no room.",
    });
  });
});
