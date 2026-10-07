"use client";

import Link from "next/link";
import { useEffect, useRef, useState, useSyncExternalStore } from "react";
import { toast } from "sonner";

import { useBranding } from "@/app/providers";
import type { AccountDetails } from "@/lib/api/types";
import { certificatesOf } from "@/lib/certificate";
import { expiryLabels } from "@/lib/challenge";
import {
  breachStory,
  hasEnded,
  isTrading,
  latestMilestone,
  nextStepsOf,
  objectivesOf,
  resultTone,
  retryOf,
  statusText,
  toneText,
  type Milestone,
  type NextStep,
  type Objective,
} from "@/lib/dashboard";
import { formatDate, formatMoney, formatSignedPercent, timeZoneName } from "@/lib/format";
import { useMyAccount } from "@/lib/queries";

import { AccountHistory } from "./AccountHistory";
import { useConfetti } from "./Celebrate";
import { Certificates } from "./Certificates";
import { ChallengeRules } from "./ChallengeRules";
import { AlertIcon, ChartIcon, ClockIcon, CrossIcon, InfoIcon, PauseIcon, ShieldCheckIcon, TerminalIcon, TrophyIcon } from "./icons";
import { AnimatedMoney, LiveDot, RoomBar } from "./Live";
import { OpenTerminalButton } from "./OpenTerminalButton";
import { PayoutHistory, PayoutPanel } from "./PayoutPanel";
import { StageStepper, StageTiles } from "./StageSteps";
import { Badge, type BadgeTone, buttonClass, Loading, Message, ProgressBar, secondaryButtonClass, StepBar, TraderPage } from "./ui";

type Section = { id: string; shows: (details: AccountDetails) => boolean; render: (details: AccountDetails, now: number) => React.ReactNode };

/**
 * The parts of the account page, in order. Kept as a list so that a firm's own design can later choose which parts
 * its traders see and in which order.
 */
const sections: Section[] = [
  { id: "milestone", shows: () => true, render: (d, now) => <MilestoneCard details={d} now={now} /> },
  { id: "welcome", shows: (d) => d.account.status === "Active" && d.account.stage === 0 && d.account.tradingDays === 0, render: () => <Welcome /> },
  { id: "notices", shows: () => true, render: (d) => <Notices details={d} /> },
  { id: "what-happened", shows: (d) => d.account.status === "Failed", render: (d) => <WhatHappened details={d} /> },
  { id: "stages", shows: () => true, render: (d) => <Stages details={d} /> },
  { id: "figures", shows: () => true, render: (d) => <KeyFigures details={d} footer={<NextSteps steps={nextStepsOf(d)} />} /> },
  { id: "payout", shows: (d) => d.account.funded, render: (d) => <PayoutPanel details={d} /> },
  { id: "objectives", shows: () => true, render: (d) => <Objectives details={d} /> },
  { id: "payouts", shows: (d) => d.payouts.length > 0, render: (d) => <PayoutHistory payouts={d.payouts} /> },
  { id: "certificates", shows: (d) => certificatesOf(d).length > 0, render: (d) => <Certificates details={d} /> },
  { id: "history", shows: () => true, render: (d, now) => <AccountHistory details={d} now={now} /> },
  { id: "rules", shows: () => true, render: (d) => <ChallengeRules details={d} /> },
];

/** One of the trader's accounts: where it stands, what it must keep, how it has gone and its rules. */
export function TraderAccount({ accountId }: { accountId: string }) {
  const account = useMyAccount(accountId);

  if (account.isError) {
    return <Message text={account.error.message} />;
  }

  if (!account.data) {
    return <Loading />;
  }

  const details = account.data;
  return (
    <TraderPage>
      <TradingDayToast details={details} />
      <nav aria-label="Breadcrumb" className="text-sm text-muted">
        <Link href="/" className="hover:text-foreground">
          Accounts
        </Link>{" "}
        <span aria-hidden="true">/</span> <span className="text-foreground">#{details.account.number}</span>
      </nav>
      <Header details={details} />
      {sections
        .filter((section) => section.shows(details))
        .map((section) => (
          <div key={section.id} className="contents">
            {section.render(details, account.dataUpdatedAt)}
          </div>
        ))}
    </TraderPage>
  );
}

function Header({ details }: { details: AccountDetails }) {
  const { account } = details;
  // Every time on the page is in the challenge's zone, which its trading days follow, so the page says which.
  const timeZone = details.challenge.tradingDay.timeZone;
  const retry = retryOf(details);
  return (
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div className="flex min-w-0 flex-col gap-2">
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="font-serif text-[2.35rem] leading-[1.05] tracking-tight">{details.challenge.name}</h1>
          <Badge tone={headerTone(details)}>
            {account.funded && account.status === "Active" && <TrophyIcon className="mr-1 size-3.5" />}
            {statusText(details)}
          </Badge>
        </div>
        <p className="text-sm text-muted">
          Account #{account.number} · {formatMoney(account.initialBalance)} {account.currency} · Started {formatDate(account.createdAt, timeZone)} · Times shown
          in {timeZoneName(timeZone)}
        </p>
      </div>
      <div className="flex flex-wrap items-center gap-2.5">
        <Link href={`/support/new?account=${account.id}`} className={`${secondaryButtonClass} py-2.5`}>
          Ask about this account
        </Link>
        {isTrading(details) && <OpenTerminalButton account={account} className="px-5 py-2.5" />}
        {retry && (
          <Link href={retry.href} className={`${buttonClass} px-5 py-2.5`}>
            {retry.label}
          </Link>
        )}
      </div>
    </div>
  );
}

function headerTone(details: AccountDetails): BadgeTone {
  const { account } = details;
  switch (account.status) {
    case "Failed":
      return details.breach ? "loss" : "muted";
    case "Cancelled":
      return "muted";
    case "OpeningAccount":
      return "warning";
    case "AwaitingFunding":
      return "profit";
    default:
      return account.paused ? "warning" : account.funded ? "profit" : "accent";
  }
}

const noticeStyles = {
  warning: { box: "border-warning/40 bg-warning/10", icon: "text-warning" },
  profit: { box: "border-profit/40 bg-profit/10", icon: "text-profit" },
  muted: { box: "border-border bg-panel", icon: "text-muted" },
};

/** What is happening to the account right now, when it is not simply trading. Why a failed one ended is told below. */
function Notices({ details }: { details: AccountDetails }) {
  const { account } = details;
  const notices: { tone: keyof typeof noticeStyles; icon: React.ReactNode; text: string }[] = [];
  switch (account.status) {
    case "OpeningAccount":
      notices.push({ tone: "warning", icon: <ClockIcon />, text: `The trading account for ${account.stageName} is being opened.` });
      break;
    case "AwaitingFunding":
      notices.push({ tone: "profit", icon: <TrophyIcon />, text: "Every evaluation stage is passed. The firm is reviewing your funded account." });
      break;
    case "Cancelled":
      notices.push({ tone: "muted", icon: <InfoIcon />, text: "Cancelled by the firm." });
      break;
    default:
      break;
  }

  if (account.paused && account.status !== "Failed" && account.status !== "Cancelled") {
    notices.push({
      tone: "warning",
      icon: <PauseIcon />,
      text: "Paused by the firm. You cannot open new trades until it goes on, but you can close the ones you have. The days do not count while it is paused.",
    });
  }

  if (isTrading(details) && !details.live) {
    notices.push({
      tone: "muted",
      icon: <AlertIcon />,
      text: "The account cannot be valued right now. The figures are as the trading platform last reported them.",
    });
  }

  if (notices.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-col gap-2">
      {notices.map((notice) => (
        <p key={notice.text} role="status" className={`flex items-start gap-3 rounded-xl border px-4 py-3 text-sm ${noticeStyles[notice.tone].box}`}>
          <span className={`mt-0.5 ${noticeStyles[notice.tone].icon}`}>{notice.icon}</span>
          {notice.text}
        </p>
      ))}
    </div>
  );
}

/**
 * How a failed challenge ended, calmly: a short timeline of the breach, the positions it closed and the balance it
 * ended with, or the deadline that ran out, and a way to try again.
 */
function WhatHappened({ details }: { details: AccountDetails }) {
  const { expiry } = details;
  const story = breachStory(details);
  const retry = retryOf(details);
  return (
    <section aria-labelledby="what-happened-heading" className="flex flex-col gap-5 rounded-2xl border border-border bg-panel p-6 shadow-card">
      <div className="flex items-start gap-4">
        <span aria-hidden="true" className="grid size-10 shrink-0 place-items-center rounded-full bg-loss/10 text-loss ring-1 ring-loss/25 ring-inset">
          {story.length > 0 ? <CrossIcon /> : <ClockIcon />}
        </span>
        <div className="flex flex-col gap-1">
          <h2 id="what-happened-heading" className="font-serif text-2xl leading-tight">
            What happened
          </h2>
          <p className="text-sm text-muted">
            {story.length > 0
              ? "The challenge ended when equity went below a loss limit. This is how it went."
              : expiry
                ? `The challenge ended on ${formatDate(expiry.day)}: ${expiryLabels[expiry.reason]}`
                : "The challenge has ended."}
          </p>
        </div>
      </div>
      {story.length > 0 && (
        <ol className="flex flex-col">
          {story.map((step, index) => (
            <li key={step.when} className="relative flex gap-4 pb-4 pl-1 last:pb-0">
              {index < story.length - 1 && <span aria-hidden="true" className="absolute top-4 bottom-0 left-[8.5px] w-px bg-border" />}
              <span aria-hidden="true" className={`relative mt-1.5 size-2.5 shrink-0 rounded-full ring-4 ring-panel ${index === 0 ? "bg-loss" : "bg-muted"}`} />
              <div className="flex min-w-0 flex-col gap-0.5 text-sm">
                <span className="text-xs text-muted">{step.when}</span>
                <span>{step.text}</span>
              </div>
            </li>
          ))}
        </ol>
      )}
      {story.length > 0 && (
        <Link href={`/accounts/${details.account.id}/breach-report`} className={`${secondaryButtonClass} self-start text-sm`}>
          See the breach report, price by price
        </Link>
      )}
      {retry && (
        <div className="flex flex-wrap items-center gap-3 border-t border-border pt-4">
          <Link href={retry.href} className={buttonClass}>
            {retry.label}
          </Link>
          <span className="text-sm text-muted">Every new challenge starts afresh, with the same rules.</span>
        </div>
      )}
    </section>
  );
}

/** The phases: tiles with dates and results on wider screens, and a compact row on a phone. */
function Stages({ details }: { details: AccountDetails }) {
  return (
    <>
      <div className="rounded-xl border border-border bg-panel px-4 py-3 sm:hidden">
        <StageStepper details={details} />
      </div>
      <div className="hidden sm:block">
        <StageTiles details={details} />
      </div>
    </>
  );
}

// What the trader has seen and closed, such as a milestone or the welcome, kept in the browser so each shows once.
// Memory stands in when storage is off.
const seenKey = "kronant.milestones";
let seenMemory = "";
const seenListeners = new Set<() => void>();

function readSeen(): string {
  try {
    return localStorage.getItem(seenKey) ?? seenMemory;
  } catch {
    return seenMemory;
  }
}

function markSeen(key: string) {
  const list = [...readSeen().split("\n").filter(Boolean).slice(-49), key].join("\n");
  seenMemory = list;
  try {
    localStorage.setItem(seenKey, list);
  } catch {
    // Storage is off, so memory keeps it for this visit.
  }

  seenListeners.forEach((listener) => listener());
}

function subscribeSeen(listener: () => void) {
  seenListeners.add(listener);
  window.addEventListener("storage", listener);
  return () => {
    seenListeners.delete(listener);
    window.removeEventListener("storage", listener);
  };
}

/** The keys the trader has closed, or null before the browser's storage can be read. */
function useSeen(): string[] | null {
  const seen = useSyncExternalStore(subscribeSeen, readSeen, () => null);
  return seen === null ? null : seen.split("\n");
}

/** A milestone is celebrated for a week after it happened, so a trader who comes back later still sees it once. */
const celebrateFor = 7 * 24 * 60 * 60 * 1000;

/** A passed phase or a funded account, celebrated once with confetti in the firm's colors, until the trader goes on. */
function MilestoneCard({ details, now }: { details: AccountDetails; now: number }) {
  const branding = useBranding();
  const milestone = latestMilestone(details, branding.name);
  const seen = useSeen();
  const show = milestone !== null && seen !== null && !seen.includes(milestone.key) && now - Date.parse(milestone.at) < celebrateFor;
  useConfetti(show);
  return show ? <Celebration milestone={milestone} onDone={() => markSeen(milestone.key)} /> : null;
}

function Celebration({ milestone, onDone }: { milestone: Milestone; onDone: () => void }) {
  // A paid amount rolls up from nothing once the card is on screen.
  const [shown, setShown] = useState(0);
  useEffect(() => {
    const frame = requestAnimationFrame(() => setShown(milestone.amount ?? 0));
    return () => cancelAnimationFrame(frame);
  }, [milestone.amount]);

  return (
    <section
      aria-labelledby="milestone-heading"
      className="relative isolate flex animate-pop flex-wrap items-center gap-5 overflow-hidden rounded-2xl border border-accent/40 bg-panel p-6 shadow-raised"
    >
      <div
        aria-hidden="true"
        className="absolute inset-0 -z-10 bg-[radial-gradient(70%_140%_at_0%_0%,color-mix(in_oklab,var(--accent)_22%,transparent),transparent)]"
      />
      <span aria-hidden="true" className="grid size-14 shrink-0 place-items-center rounded-full bg-accent/15 text-accent ring-1 ring-accent/30 ring-inset">
        <TrophyIcon className="size-7" />
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <h2 id="milestone-heading" className="font-serif text-3xl leading-tight">
          {milestone.title}
        </h2>
        {milestone.amount != null && (
          <span className="text-3xl font-semibold tracking-tight text-profit">
            <AnimatedMoney value={shown} />
          </span>
        )}
        <p className="text-muted">{milestone.detail}</p>
      </div>
      <div className="flex flex-wrap items-center gap-2.5">
        {milestone.kind === "paid" && (
          <a href="#certificates" className={secondaryButtonClass}>
            Your certificate
          </a>
        )}
        <button type="button" onClick={onDone} className={buttonClass}>
          Continue
        </button>
      </div>
    </section>
  );
}

const welcomeKey = "welcome";

/** The first time a trader opens an account: how to read the page, where the limits are, and where to trade. */
function Welcome() {
  const seen = useSeen();
  if (seen === null || seen.includes(welcomeKey)) {
    return null;
  }

  const tips = [
    { icon: <ChartIcon className="size-5" />, title: "Your figures", text: "Equity and today's result update by themselves. The line under them says what to do next." },
    { icon: <ShieldCheckIcon className="size-5" />, title: "Your limits", text: "The bars under the objectives show how much room is left before each loss limit. They turn amber, then red." },
    { icon: <TerminalIcon className="size-5" />, title: "Trade", text: "Open the terminal with the button at the top. It runs in your browser, with nothing to install." },
  ];
  return (
    <section aria-labelledby="welcome-heading" className="flex animate-pop flex-col gap-5 rounded-2xl border border-accent/30 bg-panel p-6 shadow-raised">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-1">
          <h2 id="welcome-heading" className="font-serif text-2xl leading-tight">
            Welcome to your account
          </h2>
          <p className="text-sm text-muted">Three things to know before your first trade.</p>
        </div>
        <button type="button" onClick={() => markSeen(welcomeKey)} className={secondaryButtonClass}>
          Got it
        </button>
      </div>
      <ul className="stagger grid gap-4 sm:grid-cols-3">
        {tips.map((tip) => (
          <li key={tip.title} className="flex gap-3">
            <span aria-hidden="true" className="grid size-9 shrink-0 place-items-center rounded-xl bg-accent/12 text-accent ring-1 ring-accent/25 ring-inset">
              {tip.icon}
            </span>
            <span className="flex flex-col gap-0.5 text-sm">
              <span className="font-medium">{tip.title}</span>
              <span className="text-muted">{tip.text}</span>
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** A short note each time a new trading day is counted while the page is open, such as after the first trade. */
function TradingDayToast({ details }: { details: AccountDetails }) {
  const { account } = details;
  const days = account.funded ? (account.nextPayout?.tradingDays ?? null) : account.tradingDays;
  const before = useRef(days);
  useEffect(() => {
    if (before.current !== null && days !== null && days > before.current) {
      toast.success(days === 1 && !account.funded ? "Your first trading day is counted." : `Trading day ${days} is counted.`);
    }

    before.current = days;
  }, [days, account.funded]);
  return null;
}

const stepDots: Record<NextStep["tone"], string> = { info: "bg-accent", warning: "bg-warning", profit: "bg-profit" };

/** What the trader can do next, under the figures. */
function NextSteps({ steps }: { steps: NextStep[] }) {
  if (steps.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-wrap items-center gap-x-6 gap-y-2 border-t border-border bg-background/30 px-6 py-3.5 text-sm">
      <span className="text-muted">Next</span>
      {steps.map((step) => (
        <span key={step.key} className={`flex items-center gap-2 ${step.tone === "warning" ? "text-warning" : step.tone === "profit" ? "text-profit" : ""}`}>
          <span aria-hidden="true" className={`size-1.5 rounded-full ${stepDots[step.tone]}`} />
          {step.key === "payout" ? (
            <a href="#payout" className="font-medium hover:underline">
              {step.text}
            </a>
          ) : (
            step.text
          )}
        </span>
      ))}
    </div>
  );
}

/**
 * The account's figures in one panel: equity large and rolling to each new value, with balance, today's result and the
 * phase's result beside it, or for a funded account what has been paid out. Shared with the admin panel.
 */
export function KeyFigures({
  details,
  paidOutLabel = "Paid out to you",
  footer,
}: {
  details: AccountDetails;
  paidOutLabel?: string;
  footer?: React.ReactNode;
}) {
  const { account, results, payouts } = details;
  const paid = payouts.filter((p) => p.status === "Paid").length;
  const valued = results.equity != null;
  const live = isTrading(details) && details.live != null;
  const funded = account.funded && !hasEnded(account.status);
  return (
    <section
      aria-label="Figures"
      className={`relative isolate overflow-hidden rounded-2xl border bg-panel shadow-raised ${funded ? "border-accent/40" : "border-border"}`}
    >
      {funded && (
        <div
          aria-hidden="true"
          className="absolute inset-0 -z-10 bg-[radial-gradient(55%_130%_at_0%_0%,color-mix(in_oklab,var(--accent)_14%,transparent),transparent)]"
        />
      )}
      <div className="grid gap-6 p-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.5fr)] lg:items-center">
        <dl className="flex flex-col gap-1.5">
          <dt className="flex items-center gap-3 text-sm text-muted">
            {valued ? "Equity" : "Balance"}
            {live && <LiveDot />}
          </dt>
          <dd className="text-4xl font-medium tracking-tight sm:text-5xl">
            <AnimatedMoney value={results.equity ?? results.balance} />{" "}
            <span className="text-base font-normal tracking-normal text-muted">{account.currency}</span>
          </dd>
          <dd className="text-sm text-muted">
            {!valued ? (
              hasEnded(account.status) ? (
                "The account is closed"
              ) : (
                "Not valued right now"
              )
            ) : account.openPositions === 0 ? (
              "No open positions"
            ) : (
              <>
                {account.openPositions} open {account.openPositions === 1 ? "position" : "positions"} ·{" "}
                <AnimatedMoney value={results.floating} signed className={toneText[resultTone(results.floating)]} />
              </>
            )}
          </dd>
        </dl>
        <dl className="grid grid-cols-2 gap-x-6 gap-y-5 border-t border-border pt-5 sm:grid-cols-3 lg:border-t-0 lg:border-l lg:pt-0 lg:pl-6">
          {valued && (
            <Figure
              label="Balance"
              value={<AnimatedMoney value={results.balance} />}
              note={
                account.funded
                  ? `Starts from ${formatMoney(account.initialBalance)} after each payout`
                  : `The phase started at ${formatMoney(account.initialBalance)}`
              }
            />
          )}
          <Figure
            label="Today"
            value={<AnimatedMoney value={results.today} signed />}
            tone={toneText[resultTone(results.today)]}
            note={
              results.dayStartBalance != null && results.dayStartedAt
                ? `From ${formatMoney(results.dayStartBalance)} when the day started`
                : "Shown while the account is traded"
            }
          />
          {account.funded ? (
            <Figure
              label={paidOutLabel}
              value={<AnimatedMoney value={results.paidOut} />}
              note={paid === 1 ? "1 payout on this account" : `${paid} payouts on this account`}
            />
          ) : (
            <Figure
              label="This phase"
              value={<AnimatedMoney value={results.stageResult} signed />}
              tone={toneText[resultTone(results.stageResult)]}
              note={results.stageResultPercent == null ? "" : `${formatSignedPercent(results.stageResultPercent)} of the starting balance`}
            />
          )}
        </dl>
      </div>
      {footer}
    </section>
  );
}

function Figure({ label, value, note, tone = "" }: { label: string; value: React.ReactNode; note: React.ReactNode; tone?: string }) {
  return (
    <div className="flex min-w-0 flex-col gap-1">
      <dt className="text-xs text-muted">{label}</dt>
      <dd className={`text-lg font-medium sm:text-xl ${tone}`}>{value}</dd>
      <dd className="text-xs text-muted">{note}</dd>
    </div>
  );
}

const objectiveStyles: Record<Objective["state"], { text: string; ring: string }> = {
  reached: { text: "text-profit", ring: "border-border" },
  progress: { text: "text-accent", ring: "border-border" },
  kept: { text: "text-profit", ring: "border-border" },
  warning: { text: "text-warning", ring: "border-warning/50" },
  danger: { text: "text-loss", ring: "border-loss/60" },
  broken: { text: "text-loss", ring: "border-loss/60" },
  info: { text: "text-muted", ring: "border-border" },
};

/** What the current stage must reach and keep, each with how it stands. */
export function Objectives({ details, columns = 3 }: { details: AccountDetails; columns?: 2 | 3 }) {
  const objectives = objectivesOf(details);
  return (
    <section aria-labelledby="objectives-heading" className="flex flex-col gap-3">
      <h2 id="objectives-heading" className="text-lg font-semibold">
        {details.account.funded ? "Rules to keep" : `Objectives for ${details.account.stageName}`}
      </h2>
      <ul className={`grid grid-cols-1 gap-3 sm:grid-cols-2 ${columns === 3 ? "lg:grid-cols-3" : ""}`}>
        {objectives.map((objective) => {
          const style = objectiveStyles[objective.state];
          return (
            <li key={objective.key} className={`flex flex-col gap-2.5 rounded-xl border bg-panel p-4 shadow-card transition-colors ${style.ring}`}>
              <div className="flex items-baseline justify-between gap-3">
                <span className="font-medium">{objective.title}</span>
                {objective.stateText && <span className={`whitespace-nowrap text-xs font-medium ${style.text}`}>{objective.stateText}</span>}
              </div>
              {objective.progress != null && (
                <ProgressBar value={objective.progress} ghost={objective.ghost} label={`${objective.title}, ${objective.progress}%`} />
              )}
              {objective.room && <RoomBar share={objective.room.share} state={objective.room.state} label={`${objective.title}: ${objective.stateText}`} />}
              {objective.segments && (
                <StepBar
                  filled={objective.segments.filled}
                  total={objective.segments.total}
                  label={`${objective.segments.filled} of ${objective.segments.total}`}
                />
              )}
              <p className="text-xs text-muted">{objective.detail}</p>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
