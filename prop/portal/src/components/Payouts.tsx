"use client";

import Link from "next/link";

import type { Payout, PayoutStatus } from "@/lib/api/types";
import { formatDateTime, formatMoney } from "@/lib/format";
import { canApprove, canMarkPaid, canReject, payoutNote, payoutStatusLabels } from "@/lib/payouts";
import { usePayoutDecision } from "@/lib/queries";

import { buttonClass, ErrorText, secondaryButtonClass } from "./ui";

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

/** Payouts, newest first, optionally with their traders and the firm's decisions: approve, mark as paid or reject. */
export function PayoutTable({ payouts, showTrader = false, decisions = false }: { payouts: Payout[]; showTrader?: boolean; decisions?: boolean }) {
  // Every column after the first keeps its distance, also when a right-aligned amount meets a left-aligned column.
  const cell = "py-2 pl-6";
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="text-left text-muted">
          <tr>
            <th className="py-2 font-normal">Requested</th>
            {showTrader && <th className={`${cell} font-normal`}>Trader</th>}
            <th className={`${cell} text-right font-normal`}>Profit</th>
            <th className={`${cell} text-right font-normal`}>Payout</th>
            <th className={`${cell} font-normal`}>Status</th>
            <th className={`${cell} font-normal`}>Note</th>
            {decisions && <th className={`${cell} font-normal`} />}
          </tr>
        </thead>
        <tbody>
          {payouts.map((payout) => (
            <tr key={payout.id} className="border-t border-border align-top">
              <td className="whitespace-nowrap py-2 text-muted">{formatDateTime(payout.requestedAt)}</td>
              {showTrader && (
                <td className={cell}>
                  <Link href={`/admin/accounts/${payout.accountId}`} className="text-accent hover:underline">
                    #{payout.accountNumber}
                  </Link>{" "}
                  {payout.email}
                </td>
              )}
              <td className={`${cell} text-right font-mono tabular-nums`}>{formatMoney(payout.profit)}</td>
              <td className={`${cell} whitespace-nowrap text-right font-mono tabular-nums`}>
                {formatMoney(payout.amount)} {payout.currency}
                <span className="block font-sans text-xs text-muted">{payout.profitSplitPercent}% of the profit</span>
              </td>
              <td className={cell}>
                <PayoutBadge status={payout.status} />
              </td>
              <td className={`${cell} text-muted`}>{payoutNote(payout)}</td>
              {decisions && (
                <td className={cell}>
                  <PayoutDecisions payout={payout} />
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Approve after the firm's own checks, such as KYC, mark as paid once the money is sent, or reject. */
function PayoutDecisions({ payout }: { payout: Payout }) {
  const decision = usePayoutDecision();
  const amount = `${formatMoney(payout.amount)} ${payout.currency}`;

  const markPaid = () => {
    const reference = window.prompt(`Mark the payout of ${amount} as paid. Your reference for the payment, for example a bank transfer id (optional):`);
    if (reference !== null) {
      decision.mutate({ kind: "mark-paid", payoutId: payout.id, reference: reference.trim() });
    }
  };

  const reject = () => {
    const reason = window.prompt(`Reject the payout of ${amount}? The withdrawn profit is not returned to the account. Reason, shown to the trader:`);
    if (reason !== null) {
      decision.mutate({ kind: "reject", payoutId: payout.id, reason: reason.trim() });
    }
  };

  if (!canApprove(payout) && !canMarkPaid(payout) && !canReject(payout)) {
    return null;
  }

  return (
    <div className="flex flex-col items-end gap-2">
      <div className="flex justify-end gap-2">
        {canApprove(payout) && (
          <button
            type="button"
            disabled={decision.isPending}
            onClick={() => decision.mutate({ kind: "approve", payoutId: payout.id })}
            className={buttonClass}
          >
            Approve
          </button>
        )}
        {canMarkPaid(payout) && (
          <button type="button" disabled={decision.isPending} onClick={markPaid} className={buttonClass}>
            Mark as paid
          </button>
        )}
        {canReject(payout) && (
          <button type="button" disabled={decision.isPending} onClick={reject} className={`${secondaryButtonClass} text-loss`}>
            Reject
          </button>
        )}
      </div>
      <ErrorText error={decision.error} />
    </div>
  );
}
