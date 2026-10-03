"use client";

import { useState } from "react";

import type { PayoutStatus } from "@/lib/api/types";
import { waitingStatuses } from "@/lib/payouts";
import { useFirmPayouts } from "@/lib/queries";

import { PayoutTable } from "./Payouts";
import { ErrorText, Panel } from "./ui";

const views: { label: string; statuses: PayoutStatus[] }[] = [
  { label: "Waiting for the firm", statuses: waitingStatuses },
  { label: "All", statuses: [] },
];

/** The firm's payouts: those waiting to be approved or paid first, and all of them on request. */
export function AdminPayouts() {
  const [view, setView] = useState(views[0]);
  const payouts = useFirmPayouts(view.statuses);

  return (
    <main className="mx-auto flex w-full max-w-6xl flex-col gap-6 p-6">
      <Panel
        title="Payouts"
        actions={
          <nav aria-label="Payouts to show" className="flex gap-2 text-sm">
            {views.map((v) => (
              <button
                key={v.label}
                type="button"
                aria-pressed={v === view}
                onClick={() => setView(v)}
                className={`rounded border px-3 py-1 ${v === view ? "border-accent" : "border-border hover:border-muted"}`}
              >
                {v.label}
              </button>
            ))}
          </nav>
        }
      >
        <p className="text-sm text-muted">
          A funded trader&apos;s profit is taken off the trading account when the payout is asked for. Approve it when your checks, such
          as KYC, are done, send the money yourself and then mark it as paid.
        </p>
        <ErrorText error={payouts.error} />
        {payouts.data && payouts.data.length === 0 && <p className="text-sm text-muted">No payouts here.</p>}
        {payouts.data && payouts.data.length > 0 && <PayoutTable payouts={payouts.data} showTrader decisions />}
      </Panel>
    </main>
  );
}
