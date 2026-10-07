"use client";

import { backLink, endedText } from "@/lib/account";
import type { AccountDetails, ServerInfo } from "@/lib/api/types";
import { useSheet } from "@/lib/sheet";
import { useTradingStore } from "@/lib/store";
import { useTimeZone } from "@/lib/timeZone";

/**
 * Says plainly that trading on the account has ended and why, with the breach report when a loss limit was broken and
 * the way to the account at the firm.
 */
export function EndedNotice({ details, server }: { details: AccountDetails | undefined; server: ServerInfo }) {
  const disabled = useTradingStore((s) => s.account?.status === "Disabled");
  const events = useTradingStore((s) => s.events);
  const timeZone = useTimeZone();
  const openSheet = useSheet((s) => s.open);
  const broken = events.some((e) => e.event.kind === "EquityFloorBreached");

  if (!disabled) {
    return null;
  }

  const link = backLink(details, server);
  return (
    <div role="alert" className="mx-2 mt-2 flex animate-enter flex-wrap items-center gap-x-6 gap-y-2 rounded-xl border border-loss/40 bg-loss/10 px-4 py-3 text-sm shadow-card">
      <div className="flex min-w-0 flex-col gap-0.5">
        <p className="font-semibold text-loss">Trading on this account has ended</p>
        <p>{endedText(events.map((e) => e.event), timeZone)}</p>
      </div>
      <span className="ml-auto flex shrink-0 flex-wrap gap-2">
        {broken && (
          <button
            type="button"
            onClick={() => openSheet({ kind: "breach" })}
            className="rounded-lg border border-loss/40 bg-panel px-3 py-1.5 font-medium transition duration-150 hover:border-loss hover:bg-raised active:translate-y-px"
          >
            See the breach report
          </button>
        )}
        {link && (
          <a
            href={link}
            className="rounded-lg border border-border bg-panel px-3 py-1.5 font-medium transition duration-150 hover:border-muted hover:bg-raised active:translate-y-px"
          >
            See the account at {server.name}
          </a>
        )}
      </span>
    </div>
  );
}
