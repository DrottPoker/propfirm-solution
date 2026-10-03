import type { AccountDetails, ChallengeStatus } from "@/lib/api/types";
import { failureLabels, floorLabel, floorsOf, statusLabels, targetProgress } from "@/lib/challenge";
import { formatDateTime, formatMoney } from "@/lib/format";

import { Figure, Panel } from "./ui";

const statusStyles: Record<ChallengeStatus, string> = {
  OpeningAccount: "bg-warning/20 text-warning",
  Active: "bg-accent/20 text-accent",
  AwaitingFunding: "bg-profit/20 text-profit",
  Failed: "bg-loss/20 text-loss",
  Cancelled: "bg-muted/20 text-muted",
};

export function StatusBadge({ status }: { status: ChallengeStatus }) {
  return <span className={`rounded px-2 py-0.5 text-sm ${statusStyles[status]}`}>{statusLabels[status]}</span>;
}

/**
 * A challenge account as the trader and the firm see it: where it stands against its target and its loss
 * limits, valued at the latest prices when the trading platform answers.
 */
export function AccountOverview({ details, actions }: { details: AccountDetails; actions?: React.ReactNode }) {
  const { account, live } = details;
  const balance = live?.balance ?? account.balance;
  const progress = targetProgress(account, balance);
  const floors = floorsOf(details);

  return (
    <Panel>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-col gap-1">
          <div className="flex items-center gap-3">
            <h2 className="text-lg font-semibold">Account #{account.number}</h2>
            <StatusBadge status={account.status} />
          </div>
          <p className="text-sm text-muted">
            {account.challengeId} · {account.stageName}
            {account.tradingAccountId && <> · Trading account {account.tradingAccountId}</>}
          </p>
        </div>
        {actions}
      </div>

      <StatusNotice details={details} />

      <dl className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Figure label="Balance" value={`${formatMoney(balance)} ${account.currency}`} />
        <Figure label="Equity" value={live ? formatMoney(live.equity) : "-"} />
        <Figure label="Profit target" value={account.profitTarget === null ? "None" : formatMoney(account.profitTarget)} />
        <Figure
          label="Trading days"
          value={account.minTradingDays > 0 ? `${account.tradingDays} of ${account.minTradingDays}` : `${account.tradingDays}`}
        />
      </dl>

      {progress !== null && (
        <div className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Progress to target</span>
          <div
            role="progressbar"
            aria-label="Progress to target"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={Math.round(progress)}
            className="h-2 overflow-hidden rounded bg-background"
          >
            <div className="h-full bg-profit" style={{ width: `${progress}%` }} />
          </div>
        </div>
      )}

      {floors.length > 0 && (
        <table className="w-full text-sm">
          <caption className="pb-2 text-left text-muted">Loss limits</caption>
          <thead className="text-left text-muted">
            <tr>
              <th className="py-1 font-normal">Limit</th>
              <th className="py-1 text-right font-normal">Level</th>
              <th className="py-1 text-right font-normal">Room left</th>
            </tr>
          </thead>
          <tbody className="font-mono tabular-nums">
            {floors.map((floor) => (
              <tr key={floor.floorId} className="border-t border-border">
                <td className="py-1 font-sans">{floorLabel(floor.floorId)}</td>
                <td className="py-1 text-right">{formatMoney(floor.level)}</td>
                <td className="py-1 text-right">{formatMoney(floor.headroom)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <p className="text-xs text-muted">
        {live ? "Valued at the latest prices." : "As last reported by the trading platform. Open positions are not valued here right now."}
      </p>
    </Panel>
  );
}

function StatusNotice({ details }: { details: AccountDetails }) {
  const { account, breach } = details;
  switch (account.status) {
    case "OpeningAccount":
      return <Notice tone="text-warning">The trading account for {account.stageName} is being opened.</Notice>;
    case "AwaitingFunding":
      return <Notice tone="text-profit">Every evaluation stage is passed. The firm is reviewing the funded account.</Notice>;
    case "Failed":
      return breach ? (
        <Notice tone="text-loss">
          Failed on {formatDateTime(breach.time)}: equity {formatMoney(breach.equity)} fell below the {failureLabels[breach.reason]} at{" "}
          {formatMoney(breach.level)}.
        </Notice>
      ) : (
        <Notice tone="text-loss">Failed.</Notice>
      );
    case "Cancelled":
      return <Notice tone="text-muted">Cancelled by the firm.</Notice>;
    default:
      return null;
  }
}

function Notice({ tone, children }: { tone: string; children: React.ReactNode }) {
  return (
    <p role="status" className={`rounded border border-border bg-background px-3 py-2 text-sm ${tone}`}>
      {children}
    </p>
  );
}
