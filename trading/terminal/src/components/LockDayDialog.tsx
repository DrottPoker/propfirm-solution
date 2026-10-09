"use client";

import { useEffect, useId, useRef, useState } from "react";

import { accountName } from "@/lib/account";
import { CommandRejectedError } from "@/lib/api/client";
import { rejectionText } from "@/lib/events";
import { formatClock, formatSignedMoney, timeZoneName } from "@/lib/format";
import { useNow } from "@/lib/marketHours";
import { timeUntilText } from "@/lib/ownLimits";
import { useLockTrading, useMe } from "@/lib/queries";
import { useSheet } from "@/lib/sheet";
import { useTradingStore } from "@/lib/store";

import { LockIcon } from "./icons";
import { useDismiss } from "./useDismiss";

/**
 * Locks new orders until the next trading day (ADR 0054), which nobody can undo, not the trader and not the firm. Open
 * positions close at the current prices, or stay open to be closed or have their stops moved, as the trader chooses.
 */
export function LockDayDialog({ accountId, firmName }: { accountId: string; firmName: string }) {
  const account = useTradingStore((s) => s.account);
  const details = useMe().data?.accountDetails.find((d) => d.accountId === accountId);
  const close = useSheet((s) => s.close);
  const ref = useRef<HTMLDivElement>(null);
  const safe = useRef<HTMLButtonElement>(null);
  const titleId = useId();
  const [closePositions, setClosePositions] = useState(true);
  const lock = useLockTrading(accountId);
  const now = useNow(30_000);
  useDismiss(true, ref, close);
  // The safe choice has the focus, since locking cannot be undone.
  useEffect(() => safe.current?.focus(), []);

  if (!account) {
    return null;
  }

  const own = account.ownLimits;
  const timeZone = own.tradingDay.timeZone;
  const until = formatClock(own.nextDayStart, timeZone);
  const positions = account.positions;
  const result = positions.reduce((sum, p) => sum + p.profit, 0);
  const closing = closePositions && positions.length > 0;
  const failure = lock.error instanceof CommandRejectedError ? rejectionText(lock.error.reason) : lock.error ? "Could not lock the day." : null;

  // The note under the account bar says the day is locked, so no other note is needed.
  const confirm = () => lock.mutate(closing, { onSuccess: close });

  return (
    <div className="fixed inset-0 z-30 flex animate-fade items-center justify-center bg-background/75 p-4 backdrop-blur-sm">
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="flex w-full max-w-lg animate-pop flex-col gap-5 rounded-2xl border border-border bg-panel p-6 shadow-float"
      >
        <div className="flex items-start gap-4">
          <span className="flex size-11 shrink-0 items-center justify-center rounded-full bg-accent/15 text-accent">
            <LockIcon className="size-5" />
          </span>
          <div className="flex flex-col gap-1.5">
            <h2 id={titleId} className="text-xl leading-tight font-semibold">
              Lock trading until {until}?
            </h2>
            <p className="text-sm leading-relaxed text-muted">
              No new orders on {accountName(accountId, details)} until the next trading day starts, at {until} {timeZoneName(timeZone)}
              {now && `, in ${timeUntilText(own.nextDayStart, now.getTime())}`}. You cannot undo it before then, and {firmName} cannot either.
            </p>
          </div>
        </div>

        {positions.length > 0 && (
          <fieldset className="flex flex-col gap-2 text-sm">
            <legend className="mb-2 font-semibold">
              Your {positions.length === 1 ? "open position" : `${positions.length} open positions`}
            </legend>
            <Choice checked={closePositions} onChange={() => setClosePositions(true)} title="Close them now">
              At the current prices, result {formatSignedMoney(result)} {account.currency} before commission.
            </Choice>
            <Choice checked={!closePositions} onChange={() => setClosePositions(false)} title="Keep them open">
              You can still close them and move their stops, but not add to them.
            </Choice>
          </fieldset>
        )}

        <div className="flex flex-wrap items-center justify-end gap-2">
          {failure && (
            <p role="alert" className="mr-auto text-xs text-loss">
              {failure}
            </p>
          )}
          <button
            ref={safe}
            type="button"
            onClick={close}
            className="h-10 rounded-lg border border-border bg-raised px-4 text-sm font-medium transition duration-150 hover:border-muted active:translate-y-px"
          >
            Keep trading
          </button>
          <button
            type="button"
            disabled={lock.isPending}
            onClick={confirm}
            className="h-10 rounded-lg bg-accent px-4 text-sm font-semibold text-accent-foreground transition duration-150 hover:brightness-110 active:translate-y-px disabled:opacity-50"
          >
            {closing ? `Close and lock until ${until}` : `Lock until ${until}`}
          </button>
        </div>
      </div>
    </div>
  );
}

function Choice({ checked, onChange, title, children }: { checked: boolean; onChange: () => void; title: string; children: React.ReactNode }) {
  return (
    <label
      className={`flex cursor-pointer items-start gap-3 rounded-xl border px-3.5 py-3 transition-colors duration-150 ${checked ? "border-accent bg-accent/10" : "border-border hover:border-muted"}`}
    >
      <input type="radio" name="positions" checked={checked} onChange={onChange} className="mt-1 accent-[var(--accent)]" />
      <span className="flex flex-col gap-0.5">
        <span className="font-medium">{title}</span>
        <span className="text-xs text-muted">{children}</span>
      </span>
    </label>
  );
}
