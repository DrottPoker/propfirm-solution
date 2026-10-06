"use client";

import Link from "next/link";

import { formatTotals, whenText } from "@/lib/admin";
import type { OpsActivity, OpsOverview } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { needsUsItems, opsActivityView, type NeedsUsIcon, type NeedsUsItem, type OpsActivityIcon } from "@/lib/ops";
import { useOpsOverview } from "@/lib/opsQueries";

import { toneMarks } from "./ActivityList";
import {
  CardIcon,
  CheckIcon,
  ClockIcon,
  CrossIcon,
  FileIcon,
  PauseIcon,
  PencilIcon,
  PlayIcon,
  PlusIcon,
  RocketIcon,
  ServerIcon,
} from "./icons";
import { AdminPage, buttonClass, Loading, Message, PageHeader, Panel, ProgressBar, secondaryButtonClass, StatTile } from "./ui";
import { WeeklyChart } from "./WeeklyChart";

/** Our own admin view's start page (ADR 0024): what needs us first, then how the platform is doing. */
export function OpsHome() {
  const overview = useOpsOverview();
  if (overview.isError) {
    return <Message text={overview.error.message} />;
  }

  if (!overview.data) {
    return <Loading />;
  }

  const data = overview.data;
  const now = overview.dataUpdatedAt;
  return (
    <AdminPage>
      <PageHeader title="Overview" description="What needs us first, then how the platform is doing." />
      <NeedsUs items={needsUsItems(data.needsUs, now)} />
      <Figures overview={data} />
      <div className="grid grid-cols-1 gap-5 lg:grid-cols-[minmax(0,2fr)_minmax(18rem,1fr)]">
        <Panel title="Paid to us per week">
          <WeeklyChart
            weeks={data.weeks.map((week) => ({ start: week.start, first: week.months, second: week.goingLive }))}
            currency={data.currency}
            series={["Months and slots", "Going live and deposits"]}
            caption="Charges paid, before VAT. A month is charged a few days before it starts, so most of it lands in one week."
          />
        </Panel>
        <Funnel overview={data} />
      </div>
      <Panel
        title="Recent activity"
        actions={
          <Link href="/ops/firms" className="text-sm text-accent hover:underline">
            All firms
          </Link>
        }
      >
        <OpsActivityList activity={data.activity} now={now} />
      </Panel>
    </AdminPage>
  );
}

const needsUsIcons: Record<NeedsUsIcon, (props: { className?: string }) => React.ReactNode> = {
  review: FileIcon,
  card: CardIcon,
  clock: ClockIcon,
  server: ServerIcon,
};

/** What we have to do, the most urgent first, each with a way to do it. */
function NeedsUs({ items }: { items: NeedsUsItem[] }) {
  return (
    <section aria-labelledby="needs-us" className="flex flex-col rounded-lg border border-border bg-panel">
      <div className="flex items-center gap-2.5 px-5 py-4">
        <h2 id="needs-us" className="font-semibold">
          Needs us
        </h2>
        <span className="rounded-full bg-background px-2 text-xs text-muted">{items.length}</span>
      </div>
      {items.length === 0 ? (
        <p className="flex items-center gap-2.5 border-t border-border px-5 py-4 text-sm text-muted">
          <CheckIcon className="size-4 text-profit" />
          Nothing needs us right now.
        </p>
      ) : (
        <ul>
          {items.map((item, index) => {
            const Icon = needsUsIcons[item.icon];
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

/** The platform in four figures: firms live, what they pay each month, what they paid lately and their challenges. */
function Figures({ overview }: { overview: OpsOverview }) {
  const { firms, monthly, paidLast30Days: paid, challenges, currency } = overview;
  const paidFor = [
    count(paid.months, "month"),
    paid.slots > 0 ? `${paid.slots} for more slots` : null,
    paid.goingLive > 0 ? `${paid.goingLive} going live` : null,
    count(paid.deposits, "deposit"),
  ].filter(Boolean);
  return (
    <dl className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
      <StatTile label="Live firms" value={String(firms.live)} note={`${firms.sandbox} in the sandbox · ${firms.suspended} suspended`} />
      <StatTile label="Paid each month" value={formatMoney(monthly.amount)} unit={currency} note={`${count(monthly.firms, "firm") ?? "No firm"} on a plan, before VAT`} />
      <StatTile label="Paid to us, last 30 days" value={formatTotals(paid.totals, currency)} note={paidFor.length > 0 ? paidFor.join(", ") : "Nothing paid yet"} />
      <StatTile
        label="Open challenges"
        value={String(challenges.open)}
        note={`At live firms, in ${monthly.slots} paid slots${challenges.paused > 0 ? ` · ${challenges.paused} paused` : ""}`}
      />
    </dl>
  );
}

/** How far the firms that signed up in the last 90 days got, and how quickly we decide. */
function Funnel({ overview }: { overview: OpsOverview }) {
  const { funnel } = overview;
  const steps = [
    { label: "Signed up", value: funnel.signedUp },
    { label: "Sent an application", value: funnel.sent },
    { label: "Approved", value: funnel.approved },
    { label: "Went live", value: funnel.live },
  ];
  return (
    <Panel title="From sign-up to live">
      <p className="-mt-2 text-xs text-muted">Firms that signed up in the last 90 days</p>
      <ol className="flex flex-col gap-3.5">
        {steps.map((step) => {
          const share = funnel.signedUp === 0 ? 0 : Math.round((step.value / funnel.signedUp) * 100);
          return (
            <li key={step.label} className="flex flex-col gap-1.5 text-sm">
              <span className="flex justify-between gap-3">
                <span>{step.label}</span>
                <span className="tabular-nums">
                  {step.value}
                  {step !== steps[0] && funnel.signedUp > 0 && <span className="text-muted"> · {share}%</span>}
                </span>
              </span>
              <ProgressBar value={funnel.signedUp === 0 ? 0 : (step.value / funnel.signedUp) * 100} label={`${step.label}: ${step.value} of ${funnel.signedUp}`} tone="accent" />
            </li>
          );
        })}
      </ol>
      <dl className="flex flex-col gap-2.5 border-t border-border pt-4 text-sm">
        <div className="flex justify-between gap-3">
          <dt className="text-muted">Time to our decision</dt>
          <dd className="text-right">{funnel.averageHoursToDecision === null ? "No decision yet" : `${hoursText(funnel.averageHoursToDecision)} on average`}</dd>
        </div>
        <div className="flex justify-between gap-3">
          <dt className="text-muted">Approved, not live yet</dt>
          <dd className="text-right">
            {funnel.approvedNotLive === 0 ? (
              "None"
            ) : (
              <Link href="/ops/firms?group=Sandbox" className="text-accent hover:underline">
                {count(funnel.approvedNotLive, "firm")}
              </Link>
            )}
          </dd>
        </div>
      </dl>
    </Panel>
  );
}

const activityIcons: Record<OpsActivityIcon, (props: { className?: string }) => React.ReactNode> = {
  plus: PlusIcon,
  file: FileIcon,
  check: CheckIcon,
  pencil: PencilIcon,
  cross: CrossIcon,
  pause: PauseIcon,
  play: PlayIcon,
  rocket: RocketIcon,
  card: CardIcon,
  declined: CardIcon,
};

/** What happened to the firms lately, the newest first, each with its firm. */
function OpsActivityList({ activity, now }: { activity: OpsActivity[]; now: number }) {
  if (activity.length === 0) {
    return <p className="text-sm text-muted">Nothing has happened in the last 30 days.</p>;
  }

  return (
    <ol className="flex flex-col">
      {activity.map((item) => {
        const view = opsActivityView(item);
        const Icon = activityIcons[view.icon];
        return (
          <li key={`${item.kind}-${item.firmId}-${item.time}`} className="flex items-start gap-3.5 border-t border-border py-3 first:border-t-0">
            <span aria-hidden="true" className={`grid size-7 shrink-0 place-items-center rounded-full ${toneMarks[view.tone]}`}>
              <Icon className="size-3.5" />
            </span>
            <span className="flex min-w-0 flex-1 flex-col gap-0.5">
              <span>
                <span className="font-medium">{view.title}</span>
                {view.note && <span className="text-muted"> · {view.note}</span>}
              </span>
              <span className="truncate text-xs text-muted">
                <Link href={`/ops/firms/${encodeURIComponent(item.firmId)}`} className="text-accent hover:underline">
                  {item.firmName}
                </Link>{" "}
                · {item.firmId}
              </span>
            </span>
            <time dateTime={item.time} className="shrink-0 text-xs text-muted">
              {whenText(item.time, now)}
            </time>
          </li>
        );
      })}
    </ol>
  );
}

/** A count with its noun, for example "3 deposits". Null for none. */
function count(value: number, noun: string): string | null {
  return value === 0 ? null : `${value} ${value === 1 ? noun : `${noun}s`}`;
}

/** Hours, or days when it is longer, for example "Under an hour", "19 hours" or "2.5 days". */
function hoursText(hours: number): string {
  if (hours < 1) {
    return "Under an hour";
  }

  return hours < 48 ? `${Math.round(hours)} ${Math.round(hours) === 1 ? "hour" : "hours"}` : `${Math.round((hours / 24) * 10) / 10} days`;
}
