"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import { ageText, formatTotals } from "@/lib/admin";
import { formatDateTime, formatMoney } from "@/lib/format";
import { namesDiffer } from "@/lib/identity";
import { payoutNote, payoutViews, type PayoutView } from "@/lib/payouts";
import { useAdminPayouts, useFirmSettings, usePayoutSummary } from "@/lib/queries";

import { PayoutDecisions } from "./PayoutDecisions";
import { PayTo } from "./PayTo";
import { PayoutBadge } from "./Payouts";
import { AdminPage, ErrorText, FilterTabs, PageHeader, StatTile } from "./ui";

/** How long a payout may wait for approval before it is shown as late. */
const lateAfterDays = 2;

/**
 * The firm's payouts: the queues it works through, the oldest first, and the payouts paid or rejected. The firm
 * approves after its own checks, sends the money itself and then marks the payout as paid.
 */
export function AdminPayouts({ initialView }: { initialView: PayoutView }) {
  const [viewId, setViewId] = useState(initialView);
  const view = payoutViews.find((v) => v.id === viewId)!;
  const payouts = useAdminPayouts([...view.statuses], view.oldestFirst);
  const summary = usePayoutSummary();
  const settings = useFirmSettings();
  const currency = settings.data?.currency ?? "USD";

  // The address keeps the view, so a link from the overview opens the payouts to pay.
  useEffect(() => {
    window.history.replaceState(null, "", viewId === "to-approve" ? "/admin/payouts" : `/admin/payouts?view=${viewId}`);
  }, [viewId]);

  const now = payouts.dataUpdatedAt;
  const queue = view.oldestFirst;
  const data = summary.data;

  return (
    <AdminPage>
      <PageHeader
        title="Payouts"
        description="The profit leaves the trading account when a funded trader asks. Approve after your checks of the trader, which you tick on the trader card, send the money yourself, then mark the payout as paid."
      />

      {data && (
        <dl className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatTile
            label="To approve"
            value={formatTotals(data.toApprove.totals, currency)}
            note={data.toApprove.count === 0 ? "Nothing waits" : `${data.toApprove.count} ${data.toApprove.count === 1 ? "payout" : "payouts"}${data.toApprove.oldest ? ` · the oldest ${ageText(data.toApprove.oldest, summary.dataUpdatedAt)}` : ""}`}
            highlight={data.toApprove.count > 0}
          />
          <StatTile
            label="Approved, to pay"
            value={formatTotals(data.toPay.totals, currency)}
            note={data.toPay.count === 0 ? "Nothing waits" : `${data.toPay.count} ${data.toPay.count === 1 ? "payout" : "payouts"}`}
          />
          <StatTile label="Paid, last 30 days" value={formatTotals(data.paidLast30Days.totals, currency)} note={`${data.paidLast30Days.count} ${data.paidLast30Days.count === 1 ? "payout" : "payouts"}`} />
          <StatTile
            label="Time to pay, last 30 days"
            value={data.averageDaysToPay === null ? "-" : `${data.averageDaysToPay}`}
            unit={data.averageDaysToPay === null ? undefined : data.averageDaysToPay === 1 ? "day" : "days"}
            note="From asked for to paid, on average"
          />
        </dl>
      )}

      <section aria-label="Payouts" className="flex flex-col rounded-lg border border-border bg-panel">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border px-4 py-3">
          <FilterTabs
            label="Payouts to show"
            options={payoutViews.map((v) => ({
              value: v.id,
              label: v.label,
              count: v.id === "to-approve" ? data?.toApprove.count : v.id === "to-pay" ? data?.toPay.count : undefined,
            }))}
            value={viewId}
            onChange={setViewId}
          />
          <span className="text-xs text-muted">{queue ? "Oldest first" : "Newest first"}</span>
        </div>

        {payouts.error && (
          <div className="px-4 py-3">
            <ErrorText error={payouts.error} />
          </div>
        )}
        {payouts.isPending ? (
          <p className="px-4 py-6 text-sm text-muted">Loading...</p>
        ) : (payouts.data ?? []).length === 0 ? (
          <p className="px-4 py-6 text-sm text-muted">{viewId === "to-approve" ? "No payout waits for your approval." : viewId === "to-pay" ? "No approved payout waits to be paid." : "No payouts here."}</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[68rem] text-sm">
              <thead className="text-left text-muted">
                <tr>
                  <th scope="col" className="px-4 py-3 font-normal">
                    Asked for
                  </th>
                  <th scope="col" className="px-4 py-3 font-normal">
                    Trader
                  </th>
                  <th scope="col" className="px-4 py-3 text-right font-normal">
                    Payout
                  </th>
                  <th scope="col" className="px-4 py-3 text-right font-normal">
                    Profit
                  </th>
                  <th scope="col" className="px-4 py-3 font-normal">
                    Pay to
                  </th>
                  <th scope="col" className="px-4 py-3 font-normal">
                    Paid before
                  </th>
                  <th scope="col" className="px-4 py-3 font-normal">
                    Status
                  </th>
                  <th scope="col" className="px-4 py-3 font-normal">
                    <span className="sr-only">Decision</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {(payouts.data ?? []).map(({ payout, challengeName, paidBefore, paidBeforeAmount, traderChecked, identityName }) => {
                  const late = payout.status === "Pending" && now - Date.parse(payout.requestedAt) > lateAfterDays * 86_400_000;
                  return (
                    <tr key={payout.id} className="border-t border-border align-top">
                      <td className="whitespace-nowrap px-4 py-3.5">
                        {formatDateTime(payout.requestedAt)}
                        <span className={`block text-xs ${late ? "text-warning" : "text-muted"}`}>{ageText(payout.requestedAt, now)}</span>
                      </td>
                      <td className="px-4 py-3.5">
                        {payout.email}
                        <span className="block text-xs text-muted">
                          <Link href={`/admin/accounts/${payout.accountId}`} className="font-mono text-accent hover:underline">
                            #{payout.accountNumber}
                          </Link>{" "}
                          {challengeName}
                        </span>
                      </td>
                      <td className="whitespace-nowrap px-4 py-3.5 text-right">
                        <span className="font-mono font-medium tabular-nums">
                          {formatMoney(payout.amount)} {payout.currency}
                        </span>
                        <span className="block text-xs text-muted">{payout.profitSplitPercent}% of the profit</span>
                      </td>
                      <td className="px-4 py-3.5 text-right font-mono tabular-nums">{formatMoney(payout.profit)}</td>
                      <td className="max-w-64 px-4 py-3.5">
                        <PayTo method={payout.payTo} />
                        {namesDiffer(identityName, payout.payTo?.accountHolder) && (
                          <span className="mt-1.5 block text-xs text-warning">The name on the trader&apos;s ID is {identityName}, not the account holder&apos;s.</span>
                        )}
                      </td>
                      <td className="px-4 py-3.5">
                        {paidBefore === 0 ? (
                          <span className="text-muted">First payout</span>
                        ) : (
                          <>
                            {paidBefore === 1 ? "1 payout" : `${paidBefore} payouts`}
                            <span className="block font-mono text-xs text-muted">
                              {formatMoney(paidBeforeAmount)} {payout.currency}
                            </span>
                          </>
                        )}
                      </td>
                      <td className="px-4 py-3.5">
                        <PayoutBadge status={payout.status} />
                        {(payout.status === "Paid" || payout.status === "Rejected" || payout.status === "Failed") && (
                          <span className="mt-1 block max-w-60 text-xs text-muted">{payoutNote(payout)}</span>
                        )}
                      </td>
                      <td className="px-4 py-3.5">
                        <PayoutDecisions payout={payout} traderChecked={traderChecked} />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
        <p className="border-t border-border px-4 py-3 text-xs text-muted">
          Approving tells the trader that the money is on its way. Once you have sent it, mark the payout as paid under To pay, with your reference for the transfer.
        </p>
      </section>
    </AdminPage>
  );
}
