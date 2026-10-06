"use client";

import Link from "next/link";
import { useState } from "react";

import { formatTotals } from "@/lib/admin";
import type { OpsBillingOverview, OpsCharge } from "@/lib/api/types";
import { cardLabel, chargeLabel, chargeStatusLabels, monthlyPrices, monthName } from "@/lib/billing";
import { formatDate, formatDateTime, formatMoney } from "@/lib/format";
import { useOpsBilling } from "@/lib/opsQueries";

import { AdminPage, Badge, FilterTabs, Loading, Message, PageHeader, Panel, StatTile } from "./ui";

type View = "unpaid" | "next" | "paid";

/** What firms pay us (ADR 0024): each month together, what is not paid, the next month firm by firm, and what was paid. */
export function OpsBilling() {
  const billing = useOpsBilling();
  const [view, setView] = useState<View>("unpaid");
  if (billing.isError) {
    return <Message text={billing.error.message} />;
  }

  if (!billing.data) {
    return <Loading />;
  }

  const data = billing.data;
  return (
    <AdminPage>
      <PageHeader title="Billing" description="What firms pay us, and what is not paid. Amounts are before VAT." />
      <Figures billing={data} />
      <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-[minmax(0,2fr)_minmax(17rem,1fr)]">
        <div className="flex min-w-0 flex-col gap-4">
          <FilterTabs
            label="Charges to show"
            options={[
              { value: "unpaid" as const, label: "Not paid", count: data.unpaid.length, highlight: true },
              { value: "next" as const, label: "Next month", count: data.nextMonth.firms.length },
              { value: "paid" as const, label: "Paid", count: data.paid.length },
            ]}
            value={view}
            onChange={setView}
          />
          <section aria-label="Charges" className="overflow-hidden rounded-lg border border-border bg-panel">
            {view === "unpaid" && <Unpaid charges={data.unpaid} />}
            {view === "next" && <NextMonth billing={data} />}
            {view === "paid" && <Paid charges={data.paid} />}
          </section>
        </div>
        <Prices billing={data} />
      </div>
    </AdminPage>
  );
}

function Figures({ billing }: { billing: OpsBillingOverview }) {
  const { monthly, nextMonth, unpaid, paidLast30Days: paid, currency } = billing;
  const unpaidTotals = new Map<string, number>();
  for (const c of unpaid) {
    unpaidTotals.set(c.charge.currency, (unpaidTotals.get(c.charge.currency) ?? 0) + c.charge.amount);
  }

  return (
    <dl className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
      <StatTile label="Paid each month" value={formatMoney(monthly.amount)} unit={currency} note={`${monthly.firms} ${monthly.firms === 1 ? "firm" : "firms"}, ${monthly.slots} slots`} />
      <StatTile label="Charged next" value={formatMoney(nextMonth.amount)} unit={currency} note={`${monthName(nextMonth.month)}, on ${formatDate(nextMonth.chargeAt)}`} />
      <StatTile
        label="Not paid"
        value={formatTotals([...unpaidTotals].map(([c, amount]) => ({ currency: c, amount })), currency)}
        tone={unpaid.length > 0 ? "text-loss" : ""}
        note={unpaid.length === 0 ? "Every charge is paid" : `${unpaid.length} ${unpaid.length === 1 ? "charge" : "charges"} declined`}
      />
      <StatTile
        label="Paid to us, last 30 days"
        value={formatTotals(paid.totals, currency)}
        note={paid.deposits > 0 ? `${paid.deposits} ${paid.deposits === 1 ? "deposit" : "deposits"} among them` : "No deposits among them"}
      />
    </dl>
  );
}

function FirmLink({ id, name }: { id: string; name: string }) {
  return (
    <Link href={`/ops/firms/${encodeURIComponent(id)}?tab=billing`} className="flex flex-col">
      <span className="font-medium text-accent">{name}</span>
      <span className="text-xs text-muted">{id}</span>
    </Link>
  );
}

/** Charges whose card was declined, the longest declined first. */
function Unpaid({ charges }: { charges: OpsCharge[] }) {
  if (charges.length === 0) {
    return <p className="px-4 py-6 text-sm text-muted">Every charge is paid.</p>;
  }

  return (
    <Table headings={["Firm", "For", "Amount", "Status"]}>
      {charges.map(({ firmId, firmName, charge, failedAt, unpaidSince, pausedChallenges }) => (
        <tr key={charge.id} className="border-t border-border align-top">
          <td className="px-4 py-3">
            <FirmLink id={firmId} name={firmName} />
          </td>
          <td className="px-4 py-3">
            {chargeLabel(charge)}
            <span className="block text-xs text-muted">#{charge.number}</span>
          </td>
          <td className="px-4 py-3 text-right tabular-nums">
            {formatMoney(charge.amount)} {charge.currency}
          </td>
          <td className="px-4 py-3">
            <Badge tone="loss">{chargeStatusLabels[charge.status]}</Badge>
            <span className="mt-1 block text-xs text-muted">
              {charge.failure} Tried {charge.attempts} {charge.attempts === 1 ? "time" : "times"}
              {failedAt && `, last ${formatDateTime(failedAt)}`}.
              {charge.nextAttemptAt && ` Tried again ${formatDateTime(charge.nextAttemptAt)}.`}
              {unpaidSince && ` Unpaid since ${formatDate(unpaidSince)}, ${pausedChallenges} ${pausedChallenges === 1 ? "challenge" : "challenges"} paused.`}
            </span>
          </td>
        </tr>
      ))}
    </Table>
  );
}

/** What each paying firm is charged for the next month, the largest first. */
function NextMonth({ billing }: { billing: OpsBillingOverview }) {
  const { nextMonth } = billing;
  if (nextMonth.firms.length === 0) {
    return <p className="px-4 py-6 text-sm text-muted">No firm pays by card yet.</p>;
  }

  return (
    <>
      <p className="px-4 pt-3.5 text-sm text-muted">
        For {monthName(nextMonth.month)}, charged on {formatDate(nextMonth.chargeAt)}. A firm is charged for the slots it chose, or as many as its open challenges take.
      </p>
      <NextMonthRows billing={billing} />
    </>
  );
}

function NextMonthRows({ billing }: { billing: OpsBillingOverview }) {
  const { nextMonth, currency } = billing;
  return (
    <Table headings={["Firm", "Slots", "Amount", "Card"]}>
      {nextMonth.firms.map((firm) => (
        <tr key={firm.firmId} className="border-t border-border align-top">
          <td className="px-4 py-3">
            <FirmLink id={firm.firmId} name={firm.firmName} />
          </td>
          <td className="px-4 py-3 tabular-nums">{firm.slots}</td>
          <td className="px-4 py-3 text-right tabular-nums">
            {formatMoney(firm.amount)} {currency}
          </td>
          <td className="px-4 py-3">
            {firm.card ? cardLabel(firm.card) : <span className="text-muted">No card saved</span>}
            {(firm.suspended || firm.unpaid) && (
              <span className="mt-1 flex flex-wrap gap-1.5">
                {firm.suspended && <Badge tone="loss">Suspended</Badge>}
                {firm.unpaid && <Badge tone="loss">Has not paid</Badge>}
              </span>
            )}
          </td>
        </tr>
      ))}
    </Table>
  );
}

/** The newest charges that were paid. */
function Paid({ charges }: { charges: OpsCharge[] }) {
  if (charges.length === 0) {
    return <p className="px-4 py-6 text-sm text-muted">Nothing paid yet.</p>;
  }

  return (
    <Table headings={["Firm", "For", "Amount", "Paid"]}>
      {charges.map(({ firmId, firmName, charge }) => (
        <tr key={charge.id} className="border-t border-border align-top">
          <td className="px-4 py-3">
            <FirmLink id={firmId} name={firmName} />
          </td>
          <td className="px-4 py-3">
            {chargeLabel(charge)}
            <span className="block text-xs text-muted">#{charge.number}</span>
          </td>
          <td className="px-4 py-3 text-right tabular-nums">
            {formatMoney(charge.amount)} {charge.currency}
          </td>
          <td className="px-4 py-3 text-muted">{charge.paidAt ? formatDate(charge.paidAt) : "-"}</td>
        </tr>
      ))}
    </Table>
  );
}

function Table({ headings, children }: { headings: [string, string, string, string]; children: React.ReactNode }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[40rem] text-sm">
        <thead className="text-left text-muted">
          <tr>
            {headings.map((heading, i) => (
              <th key={heading} scope="col" className={`px-4 py-3 font-normal ${i === 2 ? "text-right" : ""}`}>
                {heading}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}

/** Our prices now, as firms see them. */
function Prices({ billing }: { billing: OpsBillingOverview }) {
  const { prices } = billing;
  return (
    <Panel title="Prices now">
      <dl className="flex flex-col gap-2.5 text-sm">
        <div className="flex justify-between gap-3">
          <dt className="text-muted">Startup fee</dt>
          <dd className="tabular-nums">
            {formatMoney(prices.startupFee)} {prices.currency}
          </dd>
        </div>
        {prices.reviewDeposit > 0 && (
          <div className="flex justify-between gap-3">
            <dt className="text-muted">Of it, the deposit at review</dt>
            <dd className="tabular-nums">
              {formatMoney(prices.reviewDeposit)} {prices.currency}
            </dd>
          </div>
        )}
      </dl>
      <ul className="flex flex-col gap-1.5 border-t border-border pt-3 text-sm">
        {monthlyPrices(prices).map((price) => (
          <li key={price}>{price}</li>
        ))}
      </ul>
      <p className="text-xs text-muted">Before VAT. A month is charged {prices.chargeDaysBeforeMonth} days before it starts. The prices are set in the service&apos;s settings.</p>
    </Panel>
  );
}
