"use client";

import { AnimatePresence, motion } from "motion/react";
import Link from "next/link";
import { useState } from "react";

import { formatTotals, fourWeekChange, namedWaitingAccounts, needsYouItems, passRateText, salesAndPayouts, weeklySeries, type NeedsYouIcon, type NeedsYouItem } from "@/lib/admin";
import type { AdminOverview, Billing, FirmSettings } from "@/lib/api/types";
import { chargeAmountText, monthName, monthlyPrices } from "@/lib/billing";
import { formatDate, formatMoney } from "@/lib/format";
import { goLiveSteps, type GoLiveStep, type StepStatus } from "@/lib/goLive";
import { useAdminOverview, useBilling, useChallenges, useFirmSettings, useFirmSupportSummary, useIdentitySettings, usePrices, useWaitingAccounts } from "@/lib/queries";

import { ActivityList, toneMarks } from "./ActivityList";
import { AlertIcon, BagIcon, CheckIcon, ExternalIcon, LockIcon, PayoutIcon, ShieldCheckIcon, SupportIcon } from "./icons";
import { StartChallengeButton } from "./StartChallenge";
import { AdminPage, buttonClass, Loading, Message, PageHeader, Panel, ProgressRing, secondaryButtonClass, Sparkline, StatTile } from "./ui";
import { WeeklyChart } from "./WeeklyChart";

/**
 * The admin panel's start page (ADR 0023). A firm in the sandbox sees its steps to go live; a live firm what needs it
 * first, then how it is doing.
 */
export function AdminHome() {
  const settings = useFirmSettings();
  if (settings.isError) {
    return <Message text={settings.error.message} />;
  }

  if (!settings.data) {
    return <Loading />;
  }

  if (settings.data.status === "Provisioning") {
    return (
      <AdminPage>
        <PageHeader title="Overview" />
        <Panel>
          <p role="status" className="text-sm text-muted">
            Your trading server is being set up. This takes a few seconds.
          </p>
        </Panel>
      </AdminPage>
    );
  }

  return settings.data.status === "Sandbox" ? <SandboxOverview settings={settings.data} /> : <LiveOverview settings={settings.data} />;
}

function LiveOverview({ settings }: { settings: FirmSettings }) {
  const overview = useAdminOverview();
  const billing = useBilling();
  const waiting = useWaitingAccounts(namedWaitingAccounts);
  const challenges = useChallenges();
  const support = useFirmSupportSummary();

  if (overview.isError) {
    return <Message text={overview.error.message} />;
  }

  if (!overview.data) {
    return <Loading />;
  }

  const data = overview.data;
  const currency = settings.currency ?? "USD";
  const now = overview.dataUpdatedAt;
  const items = needsYouItems({
    payouts: data.payouts,
    waitingAccounts: waiting.data?.accounts ?? [],
    waitingCount: waiting.data?.counts.awaitingFunding ?? 0,
    challengeName: (id) => challenges.data?.find((c) => c.id === id)?.name ?? id,
    slots: billing.data?.slots ?? null,
    currency,
    now,
    shopProblem: settings.status === "Live" ? (billing.data?.shopProblem ?? null) : null,
    support: support.data ?? null,
  });

  return (
    <AdminPage>
      <PageHeader title="Overview" description="What needs you first, then how the firm is doing." actions={<StartChallengeButton />} />
      <NeedsYou items={items} />
      <Figures overview={data} currency={currency} sells={settings.payments.active} />
      <div className="grid grid-cols-1 gap-5 lg:grid-cols-[minmax(0,2fr)_minmax(18rem,1fr)]">
        <Panel title="Sales and payouts per week">
          <WeeklyChart
            weeks={salesAndPayouts(data.weeks)}
            currency={currency}
            series={["Sales", "Payouts"]}
            caption="Sales are challenges bought in your portal, without refunds. Payouts are those you marked as paid."
          />
        </Panel>
        {billing.data && <SlotsCard billing={billing.data} />}
      </div>
      <Panel
        title="Recent activity"
        actions={
          <Link href="/admin/accounts" className="text-sm text-accent hover:underline">
            All accounts
          </Link>
        }
      >
        <ActivityList activity={data.activity} now={now} />
      </Panel>
    </AdminPage>
  );
}

const needsYouIcons: Record<NeedsYouIcon, (props: { className?: string }) => React.ReactNode> = {
  payout: PayoutIcon,
  approve: ShieldCheckIcon,
  slots: AlertIcon,
  shop: BagIcon,
  support: SupportIcon,
};

/** What the firm has to do, the most urgent first, each with a way to do it. */
function NeedsYou({ items }: { items: NeedsYouItem[] }) {
  return (
    <section aria-labelledby="needs-you" className="flex flex-col rounded-xl border border-border bg-panel shadow-card">
      <div className="flex items-center gap-2.5 px-5 py-4">
        <h2 id="needs-you" className="font-semibold">
          Needs you
        </h2>
        <span className={`rounded-full px-2 text-xs font-medium ${items.length > 0 ? "bg-accent text-accent-foreground" : "bg-background text-muted"}`}>{items.length}</span>
      </div>
      {items.length === 0 ? (
        <p className="flex items-center gap-2.5 border-t border-border px-5 py-4 text-sm text-muted">
          <span className="grid size-6 place-items-center rounded-full bg-profit/15 text-profit">
            <CheckIcon className="size-3.5" />
          </span>
          Nothing needs you right now.
        </p>
      ) : (
        <ul className="stagger">
          <AnimatePresence initial={false}>
            {items.map((item, index) => {
              const Icon = needsYouIcons[item.icon];
              return (
                <motion.li
                  key={item.key}
                  layout="position"
                  initial={{ opacity: 0, y: -8 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, x: 32, height: 0, paddingTop: 0, paddingBottom: 0, transition: { duration: 0.35 } }}
                  transition={{ duration: 0.4, ease: [0.22, 1, 0.36, 1] }}
                  className="flex flex-wrap items-center gap-x-4 gap-y-3 overflow-hidden border-t border-border px-5 py-3.5 transition-colors hover:bg-raised/40"
                >
                  <span aria-hidden="true" className={`grid size-9 shrink-0 place-items-center rounded-lg ${toneMarks[item.tone]}`}>
                    <Icon className="size-[18px]" />
                  </span>
                  <span className="flex min-w-0 flex-[1_1_20rem] flex-col gap-0.5">
                    <strong className="font-semibold">{item.title}</strong>
                    <span className="text-sm text-muted">{item.detail}</span>
                  </span>
                  <Link href={item.href} className={index === 0 ? buttonClass : secondaryButtonClass}>
                    {item.action}
                  </Link>
                </motion.li>
              );
            })}
          </AnimatePresence>
        </ul>
      )}
    </section>
  );
}

/**
 * The firm in four figures: what trades now, what it sold, what it paid out and how many pass. Sales and payouts have
 * their curve over the weeks beside them, and how the last four weeks compare with the four before once there are eight.
 */
function Figures({ overview, currency, sells }: { overview: AdminOverview; currency: string; sells: boolean }) {
  const { accounts, payouts, sales, passRate } = overview;
  const salesWeeks = weeklySeries(overview.weeks, "sales", currency);
  const payoutWeeks = weeklySeries(overview.weeks, "payouts", currency);
  return (
    <dl className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
      <StatTile label="Trading now" value={String(accounts.evaluation + accounts.funded)} note={`${accounts.evaluation} in evaluation · ${accounts.funded} funded`} />
      <StatTile
        label="Sales, last 30 days"
        value={formatTotals(sales.totals, currency)}
        note={
          <TrendNote change={fourWeekChange(salesWeeks)}>
            {sales.orders > 0 || sells ? `${sales.orders} ${sales.orders === 1 ? "challenge" : "challenges"} bought in your portal` : "Your portal does not sell yet"}
          </TrendNote>
        }
        aside={salesWeeks.some((v) => v > 0) ? <Sparkline values={salesWeeks} /> : undefined}
      />
      <StatTile
        label="Payouts, last 30 days"
        value={formatTotals(payouts.paidLast30Days.totals, currency)}
        note={`${payouts.paidLast30Days.count} paid · ${formatTotals([...payouts.toApprove.totals, ...payouts.toPay.totals].reduce(sumByCurrency, []), currency)} waiting`}
        aside={payoutWeeks.some((v) => v > 0) ? <Sparkline values={payoutWeeks} tone="profit" /> : undefined}
      />
      <StatTile
        label="Pass rate, last 90 days"
        value={passRateText(passRate)}
        note={passRate.ended === 0 ? "No evaluation has ended yet" : `${passRate.passed} of ${passRate.ended} ended evaluations reached funded`}
      />
    </dl>
  );
}

/** A figure's note, with how the last four weeks compare with the four before when that is known. */
function TrendNote({ change, children }: { change: number | null; children: React.ReactNode }) {
  return (
    <span className="flex flex-wrap items-center gap-x-1.5">
      {change !== null && (
        <span className={`rounded-full px-1.5 font-medium ${change >= 0 ? "bg-profit/15 text-profit" : "bg-loss/15 text-loss"}`}>
          {change >= 0 ? "+" : ""}
          {Math.round(change * 100)}% <span className="sr-only">compared with the four weeks before</span>
        </span>
      )}
      <span>{children}</span>
    </span>
  );
}

function sumByCurrency(sums: { currency: string; amount: number }[], total: { currency: string; amount: number }) {
  const same = sums.find((s) => s.currency === total.currency);
  return same ? sums.map((s) => (s === same ? { ...s, amount: s.amount + total.amount } : s)) : [...sums, total];
}

/** How many slots are taken and free, the firm's plan and its next payment. */
function SlotsCard({ billing }: { billing: Billing }) {
  const { slots, prices } = billing;
  const taken = slots.used + slots.reserved;
  const next = billing.nextCharge;
  return (
    <Panel
      title="Slots"
      actions={
        <Link href={billing.status === "Live" ? "/admin/billing" : "/admin/go-live"} className="text-sm text-accent hover:underline">
          {billing.status === "Live" ? "Plan and billing" : "Go live"}
        </Link>
      }
    >
      <div className="flex items-baseline gap-2">
        <span className="text-2xl font-medium tabular-nums">{taken}</span>
        <span className="text-muted">{slots.slots === null ? (taken === 1 ? "open challenge, no limit" : "open challenges, no limit") : `of ${slots.slots} taken`}</span>
      </div>
      {slots.slots !== null && <SlotBreakdown slots={slots} usedLabel="Open challenges" />}
      <dl className="flex flex-col gap-2.5 border-t border-border pt-4 text-sm">
        {billing.plan === "Paid" ? (
          <>
            <PlanRow label="Your plan" value={slots.slots !== null && slots.slots > prices.packageSlots ? `${prices.packageSlots} in the package + ${slots.slots - prices.packageSlots} more` : `${prices.packageSlots} in the package`} />
            {next && <PlanRow label="Next payment" value={`${chargeAmountText(next, prices.currency)} on ${formatDate(next.chargeAt)}, for ${monthName(next.month)}`} />}
            <PlanRow label="More when full" value={billing.autoExpandStep === null ? "Off" : `${billing.autoExpandStep} at a time`} />
          </>
        ) : (
          <PlanRow label="Your plan" value="Complimentary slots" />
        )}
      </dl>
    </Panel>
  );
}

/**
 * The slots taken by open challenges, held for orders waiting for payment, and free, as a bar and a list, so a slot
 * held by an order nobody paid is not taken for an account.
 */
function SlotBreakdown({ slots, usedLabel }: { slots: Billing["slots"]; usedLabel: string }) {
  const total = Math.max(slots.slots ?? 0, 1);
  return (
    <>
      <div role="img" aria-label={`${slots.used} used by open challenges, ${slots.reserved} held for orders, ${slots.free ?? 0} free`} className="flex h-2 overflow-hidden rounded-full bg-background">
        <span className={slots.warning ? "bg-warning" : "bg-accent"} style={{ width: `${(slots.used / total) * 100}%` }} />
        <span className={slots.warning ? "bg-warning/45" : "bg-accent/45"} style={{ width: `${(slots.reserved / total) * 100}%` }} />
      </div>
      <ul className="flex flex-col gap-1.5 text-sm">
        <SlotRow label={usedLabel} value={slots.used} mark={slots.warning ? "bg-warning" : "bg-accent"} />
        <SlotRow label="Held for orders waiting for payment" value={slots.reserved} mark={slots.warning ? "bg-warning/45" : "bg-accent/45"} />
        <SlotRow label="Free" value={slots.free ?? 0} mark="border border-border bg-background" />
      </ul>
    </>
  );
}

function SlotRow({ label, value, mark }: { label: string; value: number; mark: string }) {
  return (
    <li className="flex items-center gap-2">
      <span aria-hidden="true" className={`size-2 rounded-sm ${mark}`} />
      <span className="flex-1">{label}</span>
      <span className="tabular-nums">{value}</span>
    </li>
  );
}

function PlanRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-muted">{label}</dt>
      <dd className="text-right">{value}</dd>
    </div>
  );
}

/** A firm in the sandbox: its steps to go live, its sandbox and what live costs. */
function SandboxOverview({ settings }: { settings: FirmSettings }) {
  const challenges = useChallenges();
  const prices = usePrices();
  const billing = useBilling();
  const overview = useAdminOverview();
  const identity = useIdentitySettings();
  const waiting = useWaitingAccounts(namedWaitingAccounts);
  const support = useFirmSupportSummary();
  const [showDone, setShowDone] = useState(false);
  const error = challenges.error ?? prices.error ?? billing.error ?? overview.error ?? identity.error;
  if (error) {
    return <Message text={error.message} />;
  }

  if (!challenges.data || !prices.data || !billing.data || !overview.data || !identity.data) {
    return <Loading />;
  }

  const steps = goLiveSteps({
    settings,
    challenges: challenges.data,
    prices: prices.data,
    billing: billing.data,
    accounts: overview.data.accounts.all,
    identity: identity.data.readiness,
  });
  const done = steps.filter((s) => s.status === "done").length;
  const items = needsYouItems({
    payouts: overview.data.payouts,
    waitingAccounts: waiting.data?.accounts ?? [],
    waitingCount: waiting.data?.counts.awaitingFunding ?? 0,
    challengeName: (id) => challenges.data.find((c) => c.id === id)?.name ?? id,
    slots: billing.data.slots,
    currency: settings.currency ?? "USD",
    now: overview.dataUpdatedAt,
    support: support.data ?? null,
  });
  const shown = steps.map((step, index) => ({ step, number: index + 1 })).filter(({ step }) => showDone || step.status !== "done");
  return (
    <AdminPage>
      <PageHeader
        title={`Get ${settings.name} live`}
        description={`Everything already works here, with up to ${settings.sandboxMaxOpenAccounts ?? 5} open test accounts. Do the steps in any order. Going live comes last, after we have reviewed your firm.`}
        actions={
          <div className="flex flex-wrap gap-2">
            <Link href="/admin/get-started" className={secondaryButtonClass}>
              Open the guide
            </Link>
            <StartChallengeButton secondary />
          </div>
        }
      />
      {items.length > 0 && <NeedsYou items={items} />}
      <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-[minmax(0,2fr)_minmax(18rem,1fr)]">
        <section aria-labelledby="steps" className="flex flex-col rounded-xl border border-border bg-panel shadow-card">
          <div className="flex flex-wrap items-center gap-x-4 gap-y-3 px-5 py-4">
            <ProgressRing value={(done / steps.length) * 100} label={`${done} of ${steps.length} steps done`} size={52}>
              {done}/{steps.length}
            </ProgressRing>
            <div className="flex min-w-0 flex-1 flex-col">
              <h2 id="steps" className="font-semibold">
                Steps to live
              </h2>
              <span className="text-sm text-muted">
                {done} of {steps.length} done{done < steps.length ? ". The next one is at the top." : "."}
              </span>
            </div>
            {done > 0 && (
              <button type="button" aria-expanded={showDone} onClick={() => setShowDone(!showDone)} className="text-sm text-accent hover:underline">
                {showDone ? "Hide the done steps" : `Show ${done} done`}
              </button>
            )}
          </div>
          <ol className="stagger">
            {shown.map(({ step, number }) => (
              <StepRow key={step.key} step={step} number={number} />
            ))}
          </ol>
        </section>
        <div className="flex flex-col gap-5">
          <Panel title="Your sandbox">
            <div className="flex flex-col gap-2.5">
              <div className="flex justify-between text-sm">
                <span className="text-muted">Test accounts</span>
                <span className="tabular-nums">
                  {billing.data.slots.used + billing.data.slots.reserved}
                  {billing.data.slots.slots !== null && ` of ${billing.data.slots.slots} taken`}
                </span>
              </div>
              {billing.data.slots.slots !== null && <SlotBreakdown slots={billing.data.slots} usedLabel="Open test accounts" />}
            </div>
            <ul className="flex list-disc flex-col gap-1.5 pl-5 text-sm text-muted">
              <li>Test payments move no money.</li>
              <li>Stripe takes test payments with test keys, and real ones with live keys from when you go live.</li>
              <li>Your test accounts end when you go live.</li>
            </ul>
          </Panel>
          <Panel title="What live costs">
            <dl className="flex flex-col gap-2.5 text-sm">
              <PlanRow label="Startup fee, once" value={`${formatMoney(billing.data.prices.startupFee)} ${billing.data.prices.currency}`} />
              {billing.data.prices.reviewDeposit > 0 && (
                <PlanRow label="of which the deposit at review" value={`${formatMoney(billing.data.prices.reviewDeposit)} ${billing.data.prices.currency}`} />
              )}
            </dl>
            <ul className="flex flex-col gap-1.5 border-t border-border pt-3 text-sm">
              {monthlyPrices(billing.data.prices).map((price) => (
                <li key={price}>{price}</li>
              ))}
            </ul>
            <p className="text-xs text-muted">A slot is one open challenge, from when it starts until it ends. Prices are excluding VAT.</p>
          </Panel>
        </div>
      </div>
      {overview.data.activity.length > 0 && (
        <Panel title="Recent activity">
          <ActivityList activity={overview.data.activity} now={overview.dataUpdatedAt} />
        </Panel>
      )}
    </AdminPage>
  );
}

const stepMarks: Record<StepStatus, string> = {
  done: "bg-profit/15 text-profit",
  current: "border-2 border-accent text-accent",
  todo: "border border-muted text-muted",
  waiting: "bg-warning/15 text-warning",
  locked: "bg-background text-muted",
};

function StepRow({ step, number }: { step: GoLiveStep; number: number }) {
  const current = step.status === "current";
  return (
    <li aria-current={current ? "step" : undefined} className={`flex flex-wrap items-center gap-x-4 gap-y-3 border-t border-border px-5 py-3.5 ${current ? "bg-accent/5" : ""}`}>
      <span aria-hidden="true" className={`grid size-7 shrink-0 place-items-center rounded-full text-xs font-semibold ${stepMarks[step.status]}`}>
        {step.status === "done" ? <CheckIcon className="size-3.5" /> : step.status === "locked" ? <LockIcon className="size-3.5" /> : number}
      </span>
      <span className="flex min-w-0 flex-[1_1_20rem] flex-col gap-0.5">
        <strong className={`font-semibold ${step.status === "done" ? "font-medium text-muted" : ""}`}>
          {step.title}
          <span className="sr-only">, {step.status === "done" ? "done" : step.status === "waiting" ? "waiting" : step.status === "locked" ? "not possible yet" : "to do"}</span>
        </strong>
        <span className="text-sm text-muted">{step.detail}</span>
      </span>
      {step.action &&
        (step.action.external ? (
          <a href={step.action.href} target="_blank" rel="noopener" className={`${secondaryButtonClass} flex items-center gap-1.5 text-sm`}>
            {step.action.label}
            <ExternalIcon className="size-3.5" />
          </a>
        ) : (
          <Link href={step.action.href} className={`${current ? buttonClass : secondaryButtonClass} text-sm`}>
            {step.action.label}
          </Link>
        ))}
    </li>
  );
}

