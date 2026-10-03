import type { Payout, PayoutStatus } from "./api/types";

export const payoutStatusLabels: Record<PayoutStatus, string> = {
  Withdrawing: "Processing",
  Pending: "Waiting for approval",
  Approved: "Approved",
  Paid: "Paid",
  Rejected: "Rejected",
  Failed: "Failed",
};

/** The payouts the firm has to act on: approve and pay, or reject. */
export const waitingStatuses: PayoutStatus[] = ["Pending", "Approved"];

export function canApprove(payout: Payout): boolean {
  return payout.status === "Pending";
}

export function canMarkPaid(payout: Payout): boolean {
  return payout.status === "Approved";
}

export function canReject(payout: Payout): boolean {
  return payout.status === "Pending" || payout.status === "Approved";
}

/** What happened to the payout, in a few words for the trader and the firm. */
export function payoutNote(payout: Payout): string {
  switch (payout.status) {
    case "Withdrawing":
      return "The profit is being taken off the trading account.";
    case "Pending":
      return "The firm is reviewing the payout.";
    case "Approved":
      return "The firm is sending the money.";
    case "Paid":
      return payout.reference ? `Paid. Reference ${payout.reference}.` : "Paid.";
    case "Rejected":
      return payout.reason ? `Rejected: ${payout.reason}` : "Rejected.";
    case "Failed":
      return "The balance changed before the profit could be taken off, so nothing was paid out.";
  }
}
