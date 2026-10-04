"use client";

import type { Payout, PayoutStatus } from "@/lib/api/types";
import { formatDateTime, formatMoney } from "@/lib/format";
import { payoutNote, payoutStatusLabels } from "@/lib/payouts";

import { PayoutDecisions } from "./PayoutDecisions";

const statusStyles: Record<PayoutStatus, string> = {
  Withdrawing: "bg-warning/20 text-warning",
  Pending: "bg-warning/20 text-warning",
  Approved: "bg-accent/20 text-accent",
  Paid: "bg-profit/20 text-profit",
  Rejected: "bg-loss/20 text-loss",
  Failed: "bg-muted/20 text-muted",
};

export function PayoutBadge({ status }: { status: PayoutStatus }) {
  return <span className={`whitespace-nowrap rounded px-2 py-0.5 text-sm ${statusStyles[status]}`}>{payoutStatusLabels[status]}</span>;
}

/** An account's payouts, newest first, with the firm's decisions: approve, mark as paid or reject. */
export function PayoutTable({ payouts }: { payouts: Payout[] }) {
  // Every column after the first keeps its distance, also when a right-aligned amount meets a left-aligned column.
  const cell = "py-2.5 pl-6";
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[44rem] text-sm">
        <thead className="text-left text-muted">
          <tr>
            <th className="py-2 font-normal">Asked for</th>
            <th className={`${cell} text-right font-normal`}>Profit</th>
            <th className={`${cell} text-right font-normal`}>Payout</th>
            <th className={`${cell} font-normal`}>Status</th>
            <th className={`${cell} font-normal`}>
              <span className="sr-only">Decision</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {payouts.map((payout) => (
            <tr key={payout.id} className="border-t border-border align-top">
              <td className="whitespace-nowrap py-2.5 text-muted">{formatDateTime(payout.requestedAt)}</td>
              <td className={`${cell} text-right font-mono tabular-nums`}>{formatMoney(payout.profit)}</td>
              <td className={`${cell} whitespace-nowrap text-right font-mono tabular-nums`}>
                {formatMoney(payout.amount)} {payout.currency}
                <span className="block font-sans text-xs text-muted">{payout.profitSplitPercent}% of the profit</span>
              </td>
              <td className={cell}>
                <PayoutBadge status={payout.status} />
                <span className="mt-1 block max-w-64 text-xs text-muted">{payoutNote(payout)}</span>
              </td>
              <td className={cell}>
                <PayoutDecisions payout={payout} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
