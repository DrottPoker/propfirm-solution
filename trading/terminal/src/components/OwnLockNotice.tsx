"use client";

import { lockDescription, lockEvent, lockTitle } from "@/lib/ownLimits";
import { useTradingStore } from "@/lib/store";

import { LockIcon } from "./icons";

/**
 * Says plainly while new orders are locked until the next trading day, by the trader's own limit or by the trader
 * (ADR 0054), and that the challenge goes on. Charts, history and the account stay open meanwhile.
 */
export function OwnLockNotice() {
  const own = useTradingStore((s) => (s.account?.status === "Disabled" ? null : (s.account?.ownLimits ?? null)));
  const events = useTradingStore((s) => s.events);
  const lock = own?.lock;
  if (!own || !lock) {
    return null;
  }

  const timeZone = own.tradingDay.timeZone;
  const locked = lockEvent(
    events.map((e) => e.event),
    lock,
  );
  return (
    <div
      role="status"
      data-testid="own-lock"
      className="mx-2 mt-2 flex animate-enter items-start gap-3 rounded-xl border border-accent/40 bg-accent/10 px-4 py-3 text-sm shadow-card"
    >
      <LockIcon className="mt-0.5 size-5 text-accent" />
      <div className="flex min-w-0 flex-col gap-0.5">
        <p className="font-semibold text-accent">{lockTitle(lock, locked?.timestamp ?? null, timeZone)}</p>
        <p>{lockDescription(lock, locked?.positionsClosed ?? null, timeZone)}</p>
      </div>
    </div>
  );
}
