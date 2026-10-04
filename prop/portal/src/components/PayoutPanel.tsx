"use client";

import Link from "next/link";
import { useState } from "react";

import type { AccountDetails, Payout, PayoutStatus } from "@/lib/api/types";
import { formatDate, formatDateTime, formatMoney } from "@/lib/format";
import { payoutNote, payoutStatusLabels } from "@/lib/payouts";
import { useMyPayoutMethod, useRequestPayout } from "@/lib/queries";

import { ConfirmDialog } from "./Dialog";
import { PayoutBadge } from "./Payouts";
import { buttonClass, ErrorText, Panel, StepBar } from "./ui";

const inProgress: PayoutStatus[] = ["Withdrawing", "Pending", "Approved"];

/**
 * The funded trader's next payout. When one can be asked for, the trader's share and the button lead; while one is
 * on its way, how far it has come; otherwise what is still missing.
 */
export function PayoutPanel({ details }: { details: AccountDetails }) {
  const { account, payouts } = details;
  const request = useRequestPayout(account.id);
  const method = useMyPayoutMethod();
  const [asking, setAsking] = useState(false);
  const quote = account.nextPayout;
  if (!quote || account.status !== "Active") {
    return null;
  }

  const current = payouts.find((p) => inProgress.includes(p.status));
  if (current) {
    return (
      <section aria-labelledby="payout-heading" className="flex flex-col gap-4 rounded-xl border border-border bg-panel p-6">
        <div className="flex flex-wrap items-baseline justify-between gap-3">
          <div className="flex flex-col gap-1">
            <h2 id="payout-heading" className="text-sm font-medium text-muted">
              Payout on its way
            </h2>
            <p className="font-mono text-3xl font-medium">
              {formatMoney(current.amount)} <span className="text-base text-muted">{current.currency}</span>
            </p>
          </div>
          <PayoutBadge status={current.status} />
        </div>
        <PayoutSteps payout={current} />
        <p className="text-sm text-muted">{payoutNote(current)}</p>
      </section>
    );
  }


  return (
    <section
      aria-labelledby="payout-heading"
      className={`flex flex-wrap items-center justify-between gap-6 rounded-xl border bg-panel p-6 ${quote.canRequest ? "border-profit/50" : "border-border"}`}
    >
      <div className="flex min-w-0 flex-col gap-1.5">
        <h2 id="payout-heading" className={`text-sm font-medium ${quote.canRequest ? "text-profit" : "text-muted"}`}>
          {quote.canRequest ? "Payout ready" : "Next payout"}
        </h2>
        <p className="font-mono text-3xl font-medium">
          {formatMoney(quote.amount)} <span className="text-base text-muted">{account.currency}</span>
        </p>
        <p className="text-sm text-muted">
          Your {quote.profitSplitPercent}% of the profit of <span className="font-mono text-foreground">{formatMoney(quote.profit)}</span>.
          {quote.minTradingDays > 0 && ` ${quote.tradingDays} of ${quote.minTradingDays} trading days since your last payout.`}
        </p>
        {quote.minTradingDays > 0 && (
          <div className="max-w-64 pt-1">
            <StepBar filled={Math.min(quote.tradingDays, quote.minTradingDays)} total={quote.minTradingDays} label={`${quote.tradingDays} of ${quote.minTradingDays} trading days`} />
          </div>
        )}
        {quote.consistencyPercent != null && quote.bestDayProfit != null && (
          <p className="text-sm text-muted">
            Best day <span className="font-mono text-foreground">{formatMoney(quote.bestDayProfit)}</span>, at most {quote.consistencyPercent}% of the profit.
          </p>
        )}
      </div>
      <div className="flex max-w-sm flex-col items-start gap-2 sm:items-end">
        <button
          type="button"
          disabled={!quote.canRequest || method.data === null || request.isPending}
          onClick={() => setAsking(true)}
          className={`${buttonClass} px-5 py-2.5`}
        >
          {request.isPending ? "Asking..." : "Request payout"}
        </button>
        <ConfirmDialog
          open={asking}
          onClose={() => setAsking(false)}
          onConfirm={() => request.mutate(undefined, { onSuccess: () => setAsking(false) })}
          title={`Ask for a payout of ${formatMoney(quote.amount)} ${account.currency}?`}
          description={`The profit of ${formatMoney(quote.profit)} is taken off the trading account, which starts again from ${formatMoney(account.initialBalance)}. Your firm checks and pays it.`}
          confirmLabel="Request payout"
          pendingLabel="Asking..."
          pending={request.isPending}
        >
          <ErrorText error={request.error} />
        </ConfirmDialog>
        {quote.canRequest && method.data === null ? (
          <span className="text-xs sm:text-right">
            <Link href="/payouts#payout-method" className="text-accent hover:underline">
              Add how you want to be paid
            </Link>{" "}
            first, so your firm knows where to send the money.
          </span>
        ) : (
          <span className="text-xs text-muted sm:text-right">
            {quote.refusal ??
              `The whole profit leaves the trading account at once, and it starts again from ${formatMoney(account.initialBalance)}. Your firm checks and pays it.`}
          </span>
        )}
        <ErrorText error={request.error} />
      </div>
    </section>
  );
}

const steps: { label: string; reached: (p: Payout) => string | null }[] = [
  { label: "Requested", reached: (p) => p.requestedAt },
  { label: "Approved", reached: (p) => p.approvedAt },
  { label: "Paid", reached: (p) => p.paidAt },
];

/** Requested, approved and paid, with the dates so far. A rejected or failed payout says so instead. */
export function PayoutSteps({ payout }: { payout: Payout }) {
  if (payout.status === "Rejected" || payout.status === "Failed") {
    return <span className={payout.status === "Rejected" ? "text-loss" : "text-muted"}>{payoutStatusLabels[payout.status]}</span>;
  }

  return (
    <ol aria-label="Payout progress" className="flex flex-wrap items-center gap-2 text-xs">
      {steps.map((step, index) => {
        const at = step.reached(payout);
        return (
          <li key={step.label} className={`flex items-center gap-2 ${at ? "text-profit" : "text-muted"}`}>
            {index > 0 && <span aria-hidden="true" className={`h-px w-4 ${at ? "bg-profit" : "bg-border"}`} />}
            <span>
              {step.label}
              {at && ` ${formatDate(at, payout.timeZone)}`}
            </span>
          </li>
        );
      })}
    </ol>
  );
}

/** The account's payouts, newest first, with how far each has come and the firm's note. */
export function PayoutHistory({ payouts }: { payouts: Payout[] }) {
  return (
    <Panel title="Payouts">
      <div className="overflow-x-auto">
        <table className="w-full min-w-[36rem] text-sm">
          <thead className="text-left text-muted">
            <tr>
              <th scope="col" className="py-2 font-normal">
                Requested
              </th>
              <th scope="col" className="py-2 pl-6 text-right font-normal">
                Profit
              </th>
              <th scope="col" className="py-2 pl-6 text-right font-normal">
                To you
              </th>
              <th scope="col" className="py-2 pl-6 font-normal">
                Status
              </th>
              <th scope="col" className="py-2 pl-6 font-normal">
                Progress
              </th>
            </tr>
          </thead>
          <tbody>
            {payouts.map((payout) => (
              <tr key={payout.id} className="border-t border-border align-top">
                <td className="whitespace-nowrap py-3 text-muted">{formatDateTime(payout.requestedAt, payout.timeZone)}</td>
                <td className="py-3 pl-6 text-right font-mono tabular-nums">{formatMoney(payout.profit)}</td>
                <td className="whitespace-nowrap py-3 pl-6 text-right font-mono tabular-nums">
                  {formatMoney(payout.amount)} {payout.currency}
                </td>
                <td className="py-3 pl-6">
                  <PayoutBadge status={payout.status} />
                </td>
                <td className="py-3 pl-6">
                  <div className="flex flex-col gap-1">
                    <PayoutSteps payout={payout} />
                    <span className="text-xs text-muted">{payoutNote(payout)}</span>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Panel>
  );
}
