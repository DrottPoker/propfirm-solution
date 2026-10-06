"use client";

import Link from "next/link";

import { useBranding } from "@/app/providers";

import type { PayoutTotal } from "@/lib/api/types";
import { formatDateTime, formatMoney } from "@/lib/format";
import { payoutNote } from "@/lib/payouts";
import { useMyAccounts, useMyPayouts } from "@/lib/queries";

import { PayoutMethodPanel } from "./PayoutMethodPanel";
import { IdentityPanel } from "./TraderIdentity";
import { PayoutSteps } from "./PayoutPanel";
import { PayoutBadge } from "./Payouts";
import { Loading, Message, TraderPage } from "./ui";

/** Every payout from the trader's funded accounts, with what is paid, on its way and ready to ask for. */
export function TraderPayouts() {
  const payouts = useMyPayouts();
  const accounts = useMyAccounts();
  const branding = useBranding();

  if (payouts.isError) {
    return <Message text="Your payouts cannot be loaded right now. Try again shortly." />;
  }

  if (!payouts.data) {
    return <Loading />;
  }

  const { totals } = payouts.data;
  const ready = (accounts.data ?? []).filter((a) => a.account.nextPayout?.canRequest);
  return (
    <TraderPage>
      <div className="flex flex-col gap-1">
        <h1 className="font-serif text-[2.35rem] leading-[1.05] tracking-tight">Payouts</h1>
        <p className="text-muted">Every payout from your funded accounts. {branding.name} sends the money and marks it as paid here.</p>
      </div>

      <PayoutMethodPanel funded={(accounts.data ?? []).some((a) => a.account.funded)} />
      <IdentityPanel />

      {totals.length > 0 && (
        <div className="flex flex-col gap-3">
          {totals.map((total) => (
            <Totals key={total.currency} total={total} readyOn={ready.filter((a) => a.account.currency === total.currency).map((a) => ({ id: a.account.id, number: a.account.number }))} />
          ))}
        </div>
      )}

      {payouts.data.payouts.length === 0 ? (
        <p className="rounded-lg border border-border bg-panel px-5 py-8 text-center text-muted">
          No payouts yet. A funded account can ask for one when it has a profit and enough trading days.
        </p>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-border bg-panel">
          <table className="w-full min-w-[48rem] text-sm">
            <thead className="text-left text-muted">
              <tr>
                <th scope="col" className="px-5 py-3 font-normal">
                  Requested
                </th>
                <th scope="col" className="px-5 py-3 font-normal">
                  Account
                </th>
                <th scope="col" className="px-5 py-3 text-right font-normal">
                  Profit
                </th>
                <th scope="col" className="px-5 py-3 text-right font-normal">
                  To you
                </th>
                <th scope="col" className="px-5 py-3 font-normal">
                  Status
                </th>
                <th scope="col" className="px-5 py-3 font-normal">
                  Progress
                </th>
              </tr>
            </thead>
            <tbody>
              {payouts.data.payouts.map((payout) => (
                <tr key={payout.id} className="border-t border-border align-top">
                  <td className="whitespace-nowrap px-5 py-3 text-muted">{formatDateTime(payout.requestedAt, payout.timeZone)}</td>
                  <td className="px-5 py-3">
                    <Link href={`/accounts/${payout.accountId}`} className="text-accent hover:underline">
                      #{payout.accountNumber}
                    </Link>
                  </td>
                  <td className="px-5 py-3 text-right tabular-nums">{formatMoney(payout.profit)}</td>
                  <td className="whitespace-nowrap px-5 py-3 text-right tabular-nums">
                    {formatMoney(payout.amount)} {payout.currency}
                    <span className="block font-sans text-xs text-muted">{payout.profitSplitPercent}% of the profit</span>
                  </td>
                  <td className="px-5 py-3">
                    <PayoutBadge status={payout.status} />
                  </td>
                  <td className="px-5 py-3">
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
      )}
    </TraderPage>
  );
}

function Totals({ total, readyOn }: { total: PayoutTotal; readyOn: { id: string; number: number }[] }) {
  return (
    <dl className="grid grid-cols-1 gap-3 sm:grid-cols-3">
      <div className="flex flex-col gap-1 rounded-lg border border-border bg-panel p-4">
        <dt className="text-xs text-muted">Paid to you</dt>
        <dd className="text-xl font-medium">
          {formatMoney(total.paid)} <span className="text-sm text-muted">{total.currency}</span>
        </dd>
      </div>
      <div className="flex flex-col gap-1 rounded-lg border border-border bg-panel p-4">
        <dt className="text-xs text-muted">On its way</dt>
        <dd className="text-xl font-medium">
          {formatMoney(total.onTheWay)} <span className="text-sm text-muted">{total.currency}</span>
        </dd>
      </div>
      <div className="flex flex-col gap-1 rounded-lg border border-border bg-panel p-4">
        <dt className="text-xs text-muted">Ready to ask for</dt>
        <dd className={`text-xl font-medium ${total.readyToRequest > 0 ? "text-profit" : ""}`}>
          {formatMoney(total.readyToRequest)} <span className="text-sm text-muted">{total.currency}</span>
        </dd>
        {readyOn.length > 0 && (
          <dd className="flex flex-wrap gap-2 text-xs">
            {readyOn.map((account) => (
              <Link key={account.id} href={`/accounts/${account.id}`} className="text-accent hover:underline">
                Ask on #{account.number}
              </Link>
            ))}
          </dd>
        )}
      </div>
    </dl>
  );
}
