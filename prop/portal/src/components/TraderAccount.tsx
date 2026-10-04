"use client";

import Link from "next/link";

import type { AccountDetails } from "@/lib/api/types";
import { expiryLabels, failureLabels } from "@/lib/challenge";
import { hasEnded, isTrading, objectivesOf, resultTone, statusText, toneText, type Objective } from "@/lib/dashboard";
import { formatDate, formatDateTime, formatMoney, formatSignedMoney, formatSignedPercent } from "@/lib/format";
import { useMyAccount } from "@/lib/queries";

import { AccountHistory } from "./AccountHistory";
import { ChallengeRules } from "./ChallengeRules";
import { OpenTerminalButton } from "./OpenTerminalButton";
import { PayoutHistory, PayoutPanel } from "./PayoutPanel";
import { StageTiles } from "./StageSteps";
import { Badge, Message, ProgressBar, StepBar, type BadgeTone } from "./ui";

type Section = { id: string; shows: (details: AccountDetails) => boolean; render: (details: AccountDetails, now: number) => React.ReactNode };

/**
 * The parts of the account page, in order. Kept as a list so that a firm's own design can later choose which parts
 * its traders see and in which order.
 */
const sections: Section[] = [
  { id: "notices", shows: () => true, render: (d) => <Notices details={d} /> },
  { id: "stages", shows: () => true, render: (d) => <StageTiles details={d} /> },
  { id: "payout", shows: (d) => d.account.funded, render: (d) => <PayoutPanel details={d} /> },
  { id: "figures", shows: () => true, render: (d) => <KeyFigures details={d} /> },
  { id: "objectives", shows: () => true, render: (d) => <Objectives details={d} /> },
  { id: "payouts", shows: (d) => d.payouts.length > 0, render: (d) => <PayoutHistory payouts={d.payouts} /> },
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
    return <Message text="Loading..." />;
  }

  const details = account.data;
  return (
    <main className="mx-auto flex w-full max-w-6xl flex-col gap-6 px-4 py-6 sm:px-6">
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
    </main>
  );
}

function Header({ details }: { details: AccountDetails }) {
  const { account } = details;
  return (
    <div className="flex flex-wrap items-start justify-between gap-4">
      <div className="flex min-w-0 flex-col gap-1.5">
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-semibold tracking-tight">{details.challenge.name}</h1>
          <Badge tone={headerTone(details)}>{statusText(details)}</Badge>
        </div>
        <p className="text-sm text-muted">
          Account #{account.number}
          {account.tradingAccountId && <> · Trading account {account.tradingAccountId}</>} · Started {formatDate(account.createdAt)}
        </p>
      </div>
      {isTrading(details) && <OpenTerminalButton account={account} className="px-5 py-2.5" />}
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

/** Why the account stopped, or what is happening to it right now. */
function Notices({ details }: { details: AccountDetails }) {
  const { account, breach, expiry } = details;
  const notices: { tone: string; text: string }[] = [];
  switch (account.status) {
    case "OpeningAccount":
      notices.push({ tone: "text-warning", text: `The trading account for ${account.stageName} is being opened.` });
      break;
    case "AwaitingFunding":
      notices.push({ tone: "text-profit", text: "Every evaluation stage is passed. The firm is reviewing your funded account." });
      break;
    case "Failed":
      notices.push({
        tone: "text-loss",
        text: breach
          ? `Failed on ${formatDateTime(breach.time)}: equity ${formatMoney(breach.equity)} fell below the ${failureLabels[breach.reason]} at ${formatMoney(breach.level)}.`
          : expiry
            ? `Ended on ${formatDate(expiry.day)}: ${expiryLabels[expiry.reason]}`
            : "Failed.",
      });
      break;
    case "Cancelled":
      notices.push({ tone: "text-muted", text: "Cancelled by the firm." });
      break;
    default:
      break;
  }

  if (account.paused && account.status !== "Failed" && account.status !== "Cancelled") {
    notices.push({
      tone: "text-warning",
      text: "Paused by the firm. You cannot open new trades until it goes on, but you can close the ones you have. The days do not count while it is paused.",
    });
  }

  if (isTrading(details) && !details.live) {
    notices.push({ tone: "text-muted", text: "The account cannot be valued right now. The figures are as the trading platform last reported them." });
  }

  if (notices.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-col gap-2">
      {notices.map((notice) => (
        <p key={notice.text} role="status" className={`rounded-lg border border-border bg-panel px-4 py-3 text-sm ${notice.tone}`}>
          {notice.text}
        </p>
      ))}
    </div>
  );
}

/** Balance, equity, today's result and the stage's result, or for a funded account what has been paid out. */
function KeyFigures({ details }: { details: AccountDetails }) {
  const { account, results, payouts } = details;
  const paid = payouts.filter((p) => p.status === "Paid").length;
  return (
    <dl className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      <Figure
        label="Balance"
        value={`${formatMoney(results.balance)}`}
        unit={account.currency}
        note={account.funded ? `Starts from ${formatMoney(account.initialBalance)} after each payout` : `The stage started at ${formatMoney(account.initialBalance)}`}
      />
      <Figure
        label="Equity"
        value={formatMoney(results.equity)}
        note={
          results.equity == null ? (
            hasEnded(account.status) ? "The account is closed" : "Not valued right now"
          ) : account.openPositions === 0 ? (
            "No open positions"
          ) : (
            <>
              {account.openPositions} open {account.openPositions === 1 ? "position" : "positions"} ·{" "}
              <span className={toneText[resultTone(results.floating)]}>{formatSignedMoney(results.floating)}</span>
            </>
          )
        }
      />
      <Figure
        label="Today"
        value={formatSignedMoney(results.today)}
        tone={toneText[resultTone(results.today)]}
        note={
          results.dayStartBalance != null && results.dayStartedAt
            ? `From ${formatMoney(results.dayStartBalance)} when the day started, ${formatDateTime(results.dayStartedAt)}`
            : "Shown while the account is traded"
        }
      />
      {account.funded ? (
        <Figure label="Paid out to you" value={formatMoney(results.paidOut)} unit={account.currency} note={paid === 1 ? "1 payout on this account" : `${paid} payouts on this account`} />
      ) : (
        <Figure
          label="This stage"
          value={formatSignedMoney(results.stageResult)}
          tone={toneText[resultTone(results.stageResult)]}
          note={results.stageResultPercent == null ? "" : `${formatSignedPercent(results.stageResultPercent)} of the starting balance`}
        />
      )}
    </dl>
  );
}

function Figure({ label, value, unit, note, tone = "" }: { label: string; value: string; unit?: string; note: React.ReactNode; tone?: string }) {
  return (
    <div className="flex flex-col gap-1 rounded-lg border border-border bg-panel p-4">
      <dt className="text-xs text-muted">{label}</dt>
      <dd className={`font-mono text-lg font-medium sm:text-xl ${tone}`}>
        {value} {unit && value !== "-" && <span className="text-xs text-muted sm:text-sm">{unit}</span>}
      </dd>
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
function Objectives({ details }: { details: AccountDetails }) {
  const objectives = objectivesOf(details);
  return (
    <section aria-labelledby="objectives-heading" className="flex flex-col gap-3">
      <h2 id="objectives-heading" className="text-lg font-semibold">
        {details.account.funded ? "Rules to keep" : `Objectives for ${details.account.stageName}`}
      </h2>
      <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {objectives.map((objective) => {
          const style = objectiveStyles[objective.state];
          return (
            <li key={objective.key} className={`flex flex-col gap-2 rounded-lg border bg-panel p-4 ${style.ring}`}>
              <div className="flex items-baseline justify-between gap-3">
                <span className="font-medium">{objective.title}</span>
                {objective.stateText && <span className={`whitespace-nowrap text-xs font-medium ${style.text}`}>{objective.stateText}</span>}
              </div>
              {objective.progress != null && <ProgressBar value={objective.progress} label={`${objective.title}, ${objective.progress}%`} />}
              {objective.segments && (
                <StepBar filled={objective.segments.filled} total={objective.segments.total} label={`${objective.segments.filled} of ${objective.segments.total}`} />
              )}
              <p className="text-xs text-muted">{objective.detail}</p>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
