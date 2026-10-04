import { describe, expect, it } from "vitest";

import type { Payout, PayoutStatus } from "./api/types";
import { canApprove, canMarkPaid, canReject, payoutNote, waitingStatuses } from "./payouts";

const payout: Payout = {
  id: "0199a000-0000-7000-8000-000000000101",
  accountId: "0199a000-0000-7000-8000-000000000001",
  accountNumber: 1001,
  email: "anna@test.example",
  tradingAccountId: "demo-firm-1001-3",
  status: "Pending",
  profit: 8_000,
  profitSplitPercent: 80,
  amount: 6_400,
  currency: "USD",
  requestedAt: "2026-10-19T08:00:00Z",
  withdrawnAt: "2026-10-19T08:00:01Z",
  approvedAt: null,
  paidAt: null,
  rejectedAt: null,
  failedAt: null,
  reason: null,
  reference: null,
  payTo: null,
  profitReturned: false,
  timeZone: "UTC",
};

const inStatus = (status: PayoutStatus): Payout => ({ ...payout, status });

describe("the firm's decisions", () => {
  it("approves a payout waiting for approval, then marks it as paid", () => {
    expect([canApprove(inStatus("Pending")), canMarkPaid(inStatus("Pending"))]).toEqual([true, false]);
    expect([canApprove(inStatus("Approved")), canMarkPaid(inStatus("Approved"))]).toEqual([false, true]);
  });

  it("rejects only a payout waiting for the firm", () => {
    const statuses: PayoutStatus[] = ["Withdrawing", "Pending", "Approved", "Paid", "Rejected", "Failed"];

    expect(statuses.filter((s) => canReject(inStatus(s)))).toEqual(waitingStatuses);
  });

  it("decides nothing while the profit is being withdrawn or once the payout has ended", () => {
    for (const status of ["Withdrawing", "Paid", "Rejected", "Failed"] as PayoutStatus[]) {
      const p = inStatus(status);
      expect([canApprove(p), canMarkPaid(p), canReject(p)]).toEqual([false, false, false]);
    }
  });
});

describe("payoutNote", () => {
  it("names the firm's payment reference and the reason for a rejection", () => {
    expect(payoutNote({ ...payout, status: "Paid", reference: "wire-17" })).toBe("Paid. Reference wire-17.");
    expect(payoutNote({ ...payout, status: "Rejected", reason: "Copy trading is not allowed." })).toBe("Rejected: Copy trading is not allowed.");
    expect(payoutNote({ ...payout, status: "Rejected", reason: "Send your ID first.", profitReturned: true })).toBe(
      "Rejected: Send your ID first. The profit went back on the account.",
    );
  });

  it("explains that a failed payout paid nothing", () => {
    expect(payoutNote(inStatus("Failed"))).toBe("The balance changed before the profit could be taken off, so nothing was paid out.");
  });
});
