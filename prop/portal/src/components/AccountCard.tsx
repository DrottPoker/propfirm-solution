import Link from "next/link";

import type { AccountDetails } from "@/lib/api/types";
import { deadlineOf, floorState, isTrading, liveFloor, resultTone, statusText, toneText } from "@/lib/dashboard";
import { formatDate, formatMoney, formatSignedPercent } from "@/lib/format";

import { TrophyIcon } from "./icons";
import { AnimatedMoney, RoomMeter } from "./Live";
import { OpenTerminalButton } from "./OpenTerminalButton";
import { StageStepper } from "./StageSteps";
import { Badge, buttonClass, ProgressBar, secondaryButtonClass, type BadgeTone } from "./ui";

/**
 * An account on the start page: where it stands now, how much room is left to the loss limits, and the ways in. A
 * featured card, for a trader with one account, spreads the same over the full width.
 */
export function AccountCard({ details, featured = false }: { details: AccountDetails; featured?: boolean }) {
  const { account, results } = details;
  const trading = isTrading(details);
  const daily = liveFloor(details, "daily");
  const maxLoss = liveFloor(details, "max-loss");
  const atRisk = [daily, maxLoss].some((f) => f && floorState(f) !== "ok");
  const funded = account.funded && account.status === "Active";
  const payoutReady = funded && account.nextPayout?.canRequest === true;

  return (
    <article
      className={`relative isolate flex w-full flex-col gap-5 overflow-hidden rounded-2xl border bg-panel p-5 shadow-card transition-[translate,box-shadow] duration-300 ease-out-soft hover:-translate-y-0.5 hover:shadow-raised ${featured ? "sm:p-7" : ""} ${atRisk ? "border-warning/50" : funded ? "border-accent/40" : "border-border"}`}
    >
      {funded && (
        <div
          aria-hidden="true"
          className="absolute inset-0 -z-10 bg-[radial-gradient(60%_120%_at_0%_0%,color-mix(in_oklab,var(--accent)_14%,transparent),transparent)]"
        />
      )}
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-0.5">
          <h3 className={featured ? "truncate font-serif text-3xl leading-tight" : "truncate font-semibold"}>{details.challenge.name}</h3>
          <span className="text-sm text-muted">
            #{account.number}, {formatMoney(account.initialBalance)} {account.currency}
          </span>
        </div>
        <Badge tone={badgeTone(details)}>
          {funded && <TrophyIcon className="mr-1 size-3.5" />}
          {statusText(details)}
        </Badge>
      </div>

      <StageStepper details={details} />

      <div className={featured ? "grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.25fr)] lg:items-center" : "flex flex-col gap-5"}>
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div className="flex flex-col gap-1">
            <span className="text-xs text-muted">{results.equity == null ? "Balance" : trading && details.live ? "Live equity" : "Equity"}</span>
            <span className={`font-medium tracking-tight ${featured ? "text-4xl sm:text-5xl" : "text-3xl"}`}>
              <AnimatedMoney value={results.equity ?? results.balance} />
            </span>
          </div>
          <Result details={details} />
        </div>

        {(trading || hasProgress(details)) && (
          <div className={`flex flex-col gap-4 ${featured ? "lg:border-l lg:border-border lg:pl-6" : ""}`}>
            <Progress details={details} />
            {trading && (
              <>
                <RoomMeter label="Daily loss limit" floor={daily} />
                <RoomMeter label="Max loss limit" floor={maxLoss} />
              </>
            )}
          </div>
        )}
        {!trading && !hasProgress(details) && <Waiting details={details} />}
      </div>

      <div className="mt-auto flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
        <span className="flex flex-col gap-0.5 text-xs text-muted">
          {footnotes(details).map((line) => (
            <span key={line}>{line}</span>
          ))}
        </span>
        <div className="flex flex-wrap items-start gap-2">
          <Link href={`/accounts/${account.id}`} className={secondaryButtonClass} aria-label={`Details of account #${account.number}`}>
            Details
          </Link>
          {payoutReady && (
            <Link href={`/accounts/${account.id}#payout`} className={buttonClass}>
              Request payout
            </Link>
          )}
          {trading && <OpenTerminalButton account={account} />}
        </div>
      </div>
    </article>
  );
}

function badgeTone(details: AccountDetails): BadgeTone {
  const { account } = details;
  if (account.paused || account.status === "OpeningAccount") {
    return "warning";
  }

  return account.funded || account.status === "AwaitingFunding" ? "profit" : "accent";
}

/** The phase's result, or for a funded account the trader's share of the profit right now. */
function Result({ details }: { details: AccountDetails }) {
  const { account, results } = details;
  if (account.funded && account.nextPayout && isTrading(details)) {
    return (
      <div className="flex flex-col items-end gap-1">
        <span className="text-xs text-muted">Your {account.nextPayout.profitSplitPercent}% share now</span>
        <span className={`text-lg font-medium ${account.nextPayout.amount > 0 ? "text-profit" : "text-muted"}`}>
          <AnimatedMoney value={account.nextPayout.amount} />
        </span>
      </div>
    );
  }

  if (results.stageResult == null) {
    return null;
  }

  return (
    <div className="flex flex-col items-end gap-1">
      <span className="text-xs text-muted">This phase</span>
      <span className={`text-lg font-medium ${toneText[resultTone(results.stageResult)]}`}>
        <AnimatedMoney value={results.stageResult} signed />
        {results.stageResultPercent != null && <span className="ml-1.5 text-sm font-normal text-muted">{formatSignedPercent(results.stageResultPercent)}</span>}
      </span>
    </div>
  );
}

function hasProgress(details: AccountDetails): boolean {
  const { account, results } = details;
  return results.targetPercent != null || (account.funded && (account.nextPayout?.minTradingDays ?? 0) > 0);
}

/** What the account waits for, while it does not trade. */
function Waiting({ details }: { details: AccountDetails }) {
  const { account } = details;
  if (account.status === "OpeningAccount") {
    return <p className="text-sm text-muted">The trading account for {account.stageName} is being opened.</p>;
  }

  if (account.status === "AwaitingFunding") {
    return <p className="text-sm text-muted">Every evaluation stage is passed. The firm is reviewing your funded account.</p>;
  }

  return null;
}

/** How far the phase has come: the profit target, or the trading days towards the next payout. */
function Progress({ details }: { details: AccountDetails }) {
  const { account, results } = details;
  if (results.targetPercent != null && results.targetGained != null && results.targetRequired != null) {
    return (
      <div className="flex flex-col gap-1.5">
        <div className="flex justify-between gap-2 text-xs whitespace-nowrap">
          <span className="text-muted">Profit target</span>
          <span>
            {formatMoney(results.targetGained)} <span className="text-muted">of {formatMoney(results.targetRequired)}</span>
          </span>
        </div>
        <ProgressBar value={results.targetPercent} label="Progress to the profit target" />
      </div>
    );
  }

  const quote = account.nextPayout;
  if (account.funded && quote && quote.minTradingDays > 0) {
    return (
      <div className="flex flex-col gap-1.5">
        <div className="flex justify-between gap-2 text-xs">
          <span className="text-muted">Trading days since last payout</span>
          <span>
            {quote.tradingDays} of {quote.minTradingDays}
          </span>
        </div>
        <ProgressBar value={(100 * quote.tradingDays) / quote.minTradingDays} label="Trading days towards the next payout" />
      </div>
    );
  }

  return null;
}

/** The trading days and the deadline that comes first, a line each, for example "2 of 4 trading days" and "Open a trade by 2 Nov 2026". */
function footnotes(details: AccountDetails): string[] {
  const { account, results } = details;
  const parts: string[] = [];
  if (account.funded) {
    if (isTrading(details)) {
      parts.push(`Paid out ${formatMoney(results.paidOut)}`);
    }
  } else if (account.status === "Active") {
    parts.push(account.minTradingDays > 0 ? `${account.tradingDays} of ${account.minTradingDays} trading days` : `${account.tradingDays} trading days`);
  }

  const timeLimit = deadlineOf(details, account.stageDeadline);
  const inactivity = deadlineOf(details, account.inactivityDeadline);
  if (timeLimit && (!inactivity || timeLimit.lastDay <= inactivity.lastDay)) {
    parts.push(`Pass by ${formatDate(timeLimit.lastDay)}`);
  } else if (inactivity) {
    parts.push(`Open a trade by ${formatDate(inactivity.lastDay)}`);
  }

  return parts;
}
