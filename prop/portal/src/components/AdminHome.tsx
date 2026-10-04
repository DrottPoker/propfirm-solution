"use client";

import Link from "next/link";

import { formatTotals, namedWaitingAccounts, needsYouItems, passRateText, type NeedsYouIcon, type NeedsYouItem } from "@/lib/admin";
import type { AdminOverview, Billing, FirmSettings } from "@/lib/api/types";
import { monthName, monthlyPrices } from "@/lib/billing";
import { formatDate, formatMoney } from "@/lib/format";
import { goLiveSteps, type GoLiveStep, type StepStatus } from "@/lib/goLive";
import { useAdminOverview, useBilling, useChallenges, useFirmSettings, usePrices, useWaitingAccounts } from "@/lib/queries";

import { ActivityList, toneMarks } from "./ActivityList";
import { AlertIcon, CheckIcon, ExternalIcon, LockIcon, PayoutIcon, ShieldCheckIcon } from "./icons";
import { StartChallengeButton } from "./StartChallenge";
import { AdminPage, buttonClass, Message, PageHeader, Panel, ProgressBar, secondaryButtonClass, StatTile } from "./ui";
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
    return <Message text="Loading..." />;
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

  if (overview.isError) {
    return <Message text={overview.error.message} />;
  }

  if (!overview.data) {
    return <Message text="Loading..." />;
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
  });

  return (
    <AdminPage>
      <PageHeader title="Overview" description="What needs you first, then how the firm is doing." actions={<StartChallengeButton />} />
      <NeedsYou items={items} />
      <Figures overview={data} currency={currency} sells={settings.payments.active} />
      <div className="grid grid-cols-1 gap-5 lg:grid-cols-[minmax(0,2fr)_minmax(18rem,1fr)]">
        <Panel title="Sales and payouts per week">
          <WeeklyChart weeks={data.weeks} currency={currency} />
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
};

/** What the firm has to do, the most urgent first, each with a way to do it. */
function NeedsYou({ items }: { items: NeedsYouItem[] }) {
  return (
    <section aria-labelledby="needs-you" className="flex flex-col rounded-lg border border-border bg-panel">
      <div className="flex items-center gap-2.5 px-5 py-4">
        <h2 id="needs-you" className="font-semibold">
          Needs you
        </h2>
        <span className="rounded-full bg-background px-2 font-mono text-xs text-muted">{items.length}</span>
      </div>
      {items.length === 0 ? (
        <p className="flex items-center gap-2.5 border-t border-border px-5 py-4 text-sm text-muted">
          <CheckIcon className="size-4 text-profit" />
          Nothing needs you right now.
        </p>
      ) : (
        <ul>
          {items.map((item, index) => {
            const Icon = needsYouIcons[item.icon];
            return (
              <li key={item.key} className="flex flex-wrap items-center gap-x-4 gap-y-3 border-t border-border px-5 py-3.5">
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
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

/** The firm in four figures: what trades now, what it sold, what it paid out and how many pass. */
function Figures({ overview, currency, sells }: { overview: AdminOverview; currency: string; sells: boolean }) {
  const { accounts, payouts, sales, passRate } = overview;
  return (
    <dl className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
      <StatTile label="Trading now" value={String(accounts.evaluation + accounts.funded)} note={`${accounts.evaluation} in evaluation · ${accounts.funded} funded`} />
      <StatTile
        label="Sales, last 30 days"
        value={formatTotals(sales.totals, currency)}
        note={sales.orders > 0 || sells ? `${sales.orders} ${sales.orders === 1 ? "challenge" : "challenges"} bought in your portal` : "Your portal does not sell yet"}
      />
      <StatTile
        label="Payouts, last 30 days"
        value={formatTotals(payouts.paidLast30Days.totals, currency)}
        note={`${payouts.paidLast30Days.count} paid · ${formatTotals([...payouts.toApprove.totals, ...payouts.toPay.totals].reduce(sumByCurrency, []), currency)} waiting`}
      />
      <StatTile
        label="Pass rate, last 90 days"
        value={passRateText(passRate)}
        note={passRate.ended === 0 ? "No evaluation has ended yet" : `${passRate.passed} of ${passRate.ended} ended evaluations reached funded`}
      />
    </dl>
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
        <Link href="/admin/billing" className="text-sm text-accent hover:underline">
          Plan and billing
        </Link>
      }
    >
      <div className="flex items-baseline gap-2">
        <span className="font-mono text-2xl font-medium tabular-nums">{taken}</span>
        <span className="text-muted">{slots.slots === null ? (taken === 1 ? "open challenge, no limit" : "open challenges, no limit") : `of ${slots.slots} taken`}</span>
      </div>
      {slots.slots !== null && (
        <>
          <div role="img" aria-label={`${slots.used} used by open challenges, ${slots.reserved} held for orders, ${slots.free} free`} className="flex h-2 overflow-hidden rounded-full bg-background">
            <span className={slots.warning ? "bg-warning" : "bg-accent"} style={{ width: `${(slots.used / Math.max(slots.slots, 1)) * 100}%` }} />
            <span className={slots.warning ? "bg-warning/45" : "bg-accent/45"} style={{ width: `${(slots.reserved / Math.max(slots.slots, 1)) * 100}%` }} />
          </div>
          <ul className="flex flex-col gap-1.5 text-sm">
            <SlotRow label="Open challenges" value={slots.used} mark={slots.warning ? "bg-warning" : "bg-accent"} />
            <SlotRow label="Held for orders waiting for payment" value={slots.reserved} mark={slots.warning ? "bg-warning/45" : "bg-accent/45"} />
            <SlotRow label="Free" value={slots.free ?? 0} mark="border border-border bg-background" />
          </ul>
        </>
      )}
      <dl className="flex flex-col gap-2.5 border-t border-border pt-4 text-sm">
        {billing.plan === "Paid" ? (
          <>
            <PlanRow label="Your plan" value={slots.slots !== null && slots.slots > prices.packageSlots ? `${prices.packageSlots} in the package + ${slots.slots - prices.packageSlots} more` : `${prices.packageSlots} in the package`} />
            {next && <PlanRow label="Next payment" value={`${formatMoney(next.amount)} ${prices.currency} on ${formatDate(next.chargeAt)}, for ${monthName(next.month)}`} />}
            <PlanRow label="More when full" value={billing.autoExpandStep === null ? "Off" : `${billing.autoExpandStep} at a time`} />
          </>
        ) : (
          <PlanRow label="Your plan" value="Complimentary slots" />
        )}
      </dl>
    </Panel>
  );
}

function SlotRow({ label, value, mark }: { label: string; value: number; mark: string }) {
  return (
    <li className="flex items-center gap-2">
      <span aria-hidden="true" className={`size-2 rounded-sm ${mark}`} />
      <span className="flex-1">{label}</span>
      <span className="font-mono tabular-nums">{value}</span>
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
  const error = challenges.error ?? prices.error ?? billing.error ?? overview.error;
  if (error) {
    return <Message text={error.message} />;
  }

  if (!challenges.data || !prices.data || !billing.data || !overview.data) {
    return <Message text="Loading..." />;
  }

  const steps = goLiveSteps({ settings, challenges: challenges.data, prices: prices.data, billing: billing.data, accounts: overview.data.accounts.all });
  const done = steps.filter((s) => s.status === "done").length;
  return (
    <AdminPage>
      <PageHeader
        title={`Get ${settings.name} live`}
        description={`Everything already works here, with up to ${settings.sandboxMaxOpenAccounts ?? 5} open test accounts. Do the steps in any order. Going live comes last, after we have reviewed your firm.`}
        actions={<StartChallengeButton secondary />}
      />
      <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-[minmax(0,2fr)_minmax(18rem,1fr)]">
        <section aria-labelledby="steps" className="flex flex-col rounded-lg border border-border bg-panel">
          <div className="flex flex-wrap items-center gap-x-5 gap-y-3 px-5 py-4">
            <h2 id="steps" className="font-semibold">
              Steps to live
            </h2>
            <div className="flex min-w-48 flex-1 items-center gap-3">
              <div className="flex-1">
                <ProgressBar value={(done / steps.length) * 100} label={`${done} of ${steps.length} steps done`} />
              </div>
              <span className="font-mono text-sm text-muted">
                {done} of {steps.length} done
              </span>
            </div>
          </div>
          <ol>
            {steps.map((step, index) => (
              <StepRow key={step.key} step={step} number={index + 1} />
            ))}
          </ol>
        </section>
        <div className="flex flex-col gap-5">
          <Panel title="Your sandbox">
            <div className="flex flex-col gap-1.5">
              <div className="flex justify-between text-sm">
                <span className="text-muted">Open test accounts</span>
                <span className="font-mono">
                  {billing.data.slots.used + billing.data.slots.reserved}
                  {billing.data.slots.slots !== null && ` of ${billing.data.slots.slots}`}
                </span>
              </div>
              {billing.data.slots.slots !== null && (
                <ProgressBar value={((billing.data.slots.used + billing.data.slots.reserved) / Math.max(billing.data.slots.slots, 1)) * 100} label="Open test accounts" tone="accent" />
              )}
            </div>
            <ul className="flex list-disc flex-col gap-1.5 pl-5 text-sm text-muted">
              <li>Test payments move no money.</li>
              <li>Stripe works with test keys only.</li>
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

