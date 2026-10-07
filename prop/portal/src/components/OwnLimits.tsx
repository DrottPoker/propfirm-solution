"use client";

import type { AccountDetails, OwnLimitAmounts } from "@/lib/api/types";
import { isTrading } from "@/lib/dashboard";
import { formatShortDateTime, formatSignedMoney } from "@/lib/format";
import { amountLimitText, countLimitText, hasOwnLimits, lockedDayText, lockText, pendingText } from "@/lib/ownLimits";
import { useOwnLimits } from "@/lib/queries";

import { LockIcon } from "./icons";
import { Badge, ErrorText } from "./ui";

/**
 * The limits the trader set for themselves in the terminal (ADR 0054), which the firm sees but cannot change: those that
 * hold today, any that loosen from the next trading day, the lock until then, and the days they locked lately.
 */
export function OwnLimitsPanel({ details }: { details: AccountDetails }) {
  const ownLimits = useOwnLimits(details.account.id);
  const timeZone = details.challenge.tradingDay.timeZone;
  const currency = details.account.currency;
  const data = ownLimits.data;
  const now = data?.now ?? null;

  return (
    <section aria-labelledby="own-limits-heading" className="flex flex-col gap-4 rounded-xl border border-border bg-panel p-5 shadow-card">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex max-w-3xl flex-col gap-1">
          <h2 id="own-limits-heading" className="font-semibold tracking-tight">
            The trader&apos;s own limits
          </h2>
          <p className="text-sm text-muted">
            Limits the trader set for themselves in the terminal, on top of yours. Reaching one closes their positions and locks new orders until the
            next trading day. It never ends the challenge, and only the trader can set them.
          </p>
        </div>
        {now?.lock && (
          <Badge tone="warning">
            <LockIcon className="mr-1.5 size-3.5" />
            {lockText(now.lock, timeZone)}
          </Badge>
        )}
      </div>

      <ErrorText error={ownLimits.error} />
      {!data ? (
        <p className="text-sm text-muted">Loading...</p>
      ) : (
        <>
          {!now ? (
            <p className="text-sm text-muted">{isTrading(details) ? "The trading platform cannot be reached right now." : "Shown while the stage is traded."}</p>
          ) : !hasOwnLimits(now.limits, now.pending) ? (
            <p className="text-sm text-muted">
              {data.locks.length === 0 ? "The trader has set no limits of their own, and no day was locked in the last 30 days." : "The trader has set no limits of their own."}
            </p>
          ) : (
            <dl className="grid grid-cols-1 gap-3 sm:grid-cols-3">
              <Limit
                label="Daily loss limit"
                value={amountLimitText(now.limits.dailyLoss, currency)}
                next={pendingText(now.limits.dailyLoss, now.pending, (l) => l.dailyLoss, (v) => amountLimitText(v, currency))}
              />
              <Limit
                label="Daily profit target"
                value={amountLimitText(now.limits.dailyTarget, currency)}
                next={pendingText(now.limits.dailyTarget, now.pending, (l) => l.dailyTarget, (v) => amountLimitText(v, currency))}
              />
              <Limit
                label="Trades a day"
                value={countLimitText(now.limits.maxTrades)}
                next={pendingText(now.limits.maxTrades, now.pending, (l: OwnLimitAmounts) => l.maxTrades, countLimitText)}
                note={`${now.tradesToday} opened today`}
              />
            </dl>
          )}

          {/* Without limits or locked days, the sentence above says it all. */}
          {(data.locks.length > 0 || (now && hasOwnLimits(now.limits, now.pending))) && (
            <div className="flex flex-col gap-2">
              <h3 className="text-sm font-semibold">Locked days, last 30 days</h3>
              {data.locks.length === 0 ? (
                <p className="text-sm text-muted">No days were locked.</p>
              ) : (
                <ol className="flex flex-col">
                  {data.locks.map((day) => (
                    <li
                      key={day.time}
                      className="grid grid-cols-1 gap-x-4 gap-y-0.5 border-t border-border py-2.5 text-sm sm:grid-cols-[9rem_minmax(0,1fr)_auto] sm:items-baseline"
                    >
                      <span className="font-mono text-xs text-muted tabular-nums">{formatShortDateTime(day.time, timeZone)}</span>
                      <span>{lockedDayText(day, currency)}</span>
                      <span
                        className={`font-mono tabular-nums ${day.dayResult > 0 ? "text-profit" : day.dayResult < 0 ? "text-loss" : "text-muted"}`}
                        title="The day's result when it locked"
                      >
                        {formatSignedMoney(day.dayResult)}
                      </span>
                    </li>
                  ))}
                </ol>
              )}
            </div>
          )}
        </>
      )}
    </section>
  );
}

function Limit({ label, value, next, note }: { label: string; value: string; next: string | null; note?: string }) {
  return (
    <div className="flex flex-col gap-1 rounded-lg bg-raised px-4 py-3">
      <dt className="text-xs text-muted">{label}</dt>
      <dd className="text-lg font-semibold tracking-tight tabular-nums">{value}</dd>
      {next && <dd className="text-xs text-warning">{next}</dd>}
      {note && <dd className="text-xs text-muted">{note}</dd>}
    </div>
  );
}
