import Link from "next/link";

import type { AccountDetails } from "@/lib/api/types";
import { deadlineOf, floorState, floorStateTone, isTrading, liveFloor, resultTone, statusText, toneText } from "@/lib/dashboard";
import { formatDate, formatMoney, formatSignedMoney, formatSignedPercent } from "@/lib/format";

import { OpenTerminalButton } from "./OpenTerminalButton";
import { StageStepper } from "./StageSteps";
import { Badge, ProgressBar, secondaryButtonClass, type BadgeTone } from "./ui";

/** An account on the start page: where it stands now, how close the limits are, and the ways in. */
export function AccountCard({ details }: { details: AccountDetails }) {
  const { account, results } = details;
  const trading = isTrading(details);
  const daily = liveFloor(details, "daily");
  const maxLoss = liveFloor(details, "max-loss");
  const atRisk = [daily, maxLoss].some((f) => f && floorState(f) !== "ok");

  return (
    <article className={`flex w-full flex-col gap-4 rounded-xl border bg-panel p-5 ${atRisk ? "border-warning/50" : "border-border"}`}>
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-0.5">
          <h3 className="truncate font-semibold">{details.challenge.name}</h3>
          <span className="text-sm text-muted">
            #{account.number} · {formatMoney(account.initialBalance)} {account.currency}
          </span>
        </div>
        <Badge tone={badgeTone(details)}>{statusText(details)}</Badge>
      </div>

      <StageStepper details={details} />

      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-col gap-0.5">
          <span className="text-xs text-muted">{results.equity != null ? "Equity" : "Balance"}</span>
          <span className="font-mono text-2xl font-medium">{formatMoney(results.equity ?? results.balance)}</span>
        </div>
        {trading && account.funded && account.nextPayout ? (
          <div className="flex flex-col items-end gap-0.5">
            <span className="text-xs text-muted">Your share now · {account.nextPayout.profitSplitPercent}%</span>
            <span className={`font-mono ${account.nextPayout.amount > 0 ? "text-profit" : "text-muted"}`}>
              {formatMoney(account.nextPayout.amount)} {account.currency}
            </span>
          </div>
        ) : (
          results.stageResult != null && (
            <div className="flex flex-col items-end gap-0.5">
              <span className="text-xs text-muted">Result this stage</span>
              <span className={`font-mono ${toneText[resultTone(results.stageResult)]}`}>
                {formatSignedMoney(results.stageResult)} · {formatSignedPercent(results.stageResultPercent)}
              </span>
            </div>
          )
        )}
      </div>

      <Progress details={details} />

      {trading && (
        <dl className="grid grid-cols-3 gap-3 border-t border-border pt-4 text-xs">
          <Room label="Daily loss limit" floor={daily} />
          <Room label="Max loss limit" floor={maxLoss} />
          {account.funded ? (
            <div className="flex flex-col gap-0.5">
              <dt className="text-muted">Paid out</dt>
              <dd className="font-mono text-sm">{formatMoney(results.paidOut)}</dd>
            </div>
          ) : (
            <div className="flex flex-col gap-0.5">
              <dt className="text-muted">Trading days</dt>
              <dd className="font-mono text-sm">{account.minTradingDays > 0 ? `${account.tradingDays} of ${account.minTradingDays}` : account.tradingDays}</dd>
            </div>
          )}
        </dl>
      )}

      <div className="mt-auto flex flex-wrap items-center justify-between gap-3">
        <span className="text-xs text-muted">{nextDeadline(details)}</span>
        <div className="flex items-start gap-2">
          <Link href={`/accounts/${account.id}`} className={secondaryButtonClass} aria-label={`Details of account #${account.number}`}>
            Details
          </Link>
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

/** How far the stage has come: the profit target, or the trading days towards the next payout. */
function Progress({ details }: { details: AccountDetails }) {
  const { account, results } = details;
  if (account.status === "OpeningAccount") {
    return <p className="text-sm text-muted">The trading account for {account.stageName} is being opened.</p>;
  }

  if (account.status === "AwaitingFunding") {
    return <p className="text-sm text-muted">Every evaluation stage is passed. The firm is reviewing your funded account.</p>;
  }

  if (results.targetPercent != null && results.targetGained != null && results.targetRequired != null) {
    return (
      <div className="flex flex-col gap-1.5">
        <div className="flex justify-between gap-2 text-xs">
          <span className="text-muted">Profit target</span>
          <span className="font-mono">
            {formatMoney(results.targetGained)} of {formatMoney(results.targetRequired)}
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
          <span className="font-mono">
            {quote.tradingDays} of {quote.minTradingDays}
          </span>
        </div>
        <ProgressBar value={(100 * quote.tradingDays) / quote.minTradingDays} label="Trading days towards the next payout" />
      </div>
    );
  }

  return null;
}

// What is left before the limit, as in the terminal.
function Room({ label, floor }: { label: string; floor: ReturnType<typeof liveFloor> }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-muted">{label}</dt>
      <dd className={`font-mono text-sm ${floor ? toneText[floorStateTone[floorState(floor)]] : ""}`}>{floor ? `${formatMoney(Math.max(floor.headroom, 0))} left` : "-"}</dd>
    </div>
  );
}

/** The deadline that comes first, for example "Open a trade by 2 Nov 2026". */
function nextDeadline(details: AccountDetails): string {
  const { account } = details;
  const timeLimit = deadlineOf(details, account.stageDeadline);
  const inactivity = deadlineOf(details, account.inactivityDeadline);
  if (timeLimit && (!inactivity || timeLimit.lastDay <= inactivity.lastDay)) {
    return `Pass by ${formatDate(timeLimit.lastDay)}`;
  }

  return inactivity ? `Open a trade by ${formatDate(inactivity.lastDay)}` : "";
}
