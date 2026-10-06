"use client";

import { backLink, endedText } from "@/lib/account";
import type { AccountDetails, ServerInfo } from "@/lib/api/types";
import { useTradingStore } from "@/lib/store";
import { useTimeZone } from "@/lib/timeZone";

/** Says plainly that trading on the account has ended and why, with the way to the account at the firm. */
export function EndedNotice({ details, server }: { details: AccountDetails | undefined; server: ServerInfo }) {
  const disabled = useTradingStore((s) => s.account?.status === "Disabled");
  const events = useTradingStore((s) => s.events);
  const timeZone = useTimeZone();

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
      {link && (
        <a
          href={link}
          className="ml-auto shrink-0 rounded-lg border border-border bg-panel px-3 py-1.5 font-medium transition duration-150 hover:border-muted hover:bg-raised active:translate-y-px"
        >
          See the account at {server.name}
        </a>
      )}
    </div>
  );
}
