"use client";

import type { Account } from "@/lib/api/types";
import { canOpenTerminal } from "@/lib/challenge";
import { useTerminalLink } from "@/lib/queries";

import { buttonClass, ErrorText } from "./ui";

/** Logs the trader in to the trading terminal with a one-time link. The trading password is never shown. */
export function OpenTerminalButton({ account, className = "" }: { account: Account; className?: string }) {
  const link = useTerminalLink();

  return (
    <div className="flex flex-col items-end gap-2">
      <button
        type="button"
        disabled={!canOpenTerminal(account) || link.isPending}
        onClick={() => link.mutate(account.id, { onSuccess: (result) => window.location.assign(result.url) })}
        className={`${buttonClass} ${className}`}
      >
        {link.isPending ? "Opening..." : "Open terminal"}
      </button>
      <ErrorText error={link.error} />
    </div>
  );
}
