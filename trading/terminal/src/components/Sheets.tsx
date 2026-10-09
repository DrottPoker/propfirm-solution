"use client";

import type { ServerInfo } from "@/lib/api/types";
import type { DigitsOf } from "@/lib/events";
import { useSheet } from "@/lib/sheet";

import { BreachReportSheet } from "./BreachReportSheet";
import { AboutDialog, ShortcutsDialog } from "./HelpDialogs";
import { LockDayDialog } from "./LockDayDialog";
import { OwnLimitsSheet } from "./OwnLimitsSheet";
import { SettingsDialog } from "./SettingsDialog";
import { TradeDetailsSheet } from "./TradeDetailsSheet";

/**
 * The panel open over the terminal, if any: a trade's details or the breach report (ADR 0053), the trader's own limits
 * or locking the rest of the day (ADR 0054), the settings (ADR 0055), or the shortcuts or About (ADR 0058).
 */
export function Sheets({ accountId, digitsOf, server }: { accountId: string; digitsOf: DigitsOf; server: ServerInfo }) {
  const firmName = server.name;
  const sheet = useSheet((s) => s.sheet);
  switch (sheet?.kind) {
    case "details":
      return <TradeDetailsSheet key={sheet.positionId} accountId={accountId} positionId={sheet.positionId} />;
    case "breach":
      return <BreachReportSheet accountId={accountId} digitsOf={digitsOf} />;
    case "limits":
      return <OwnLimitsSheet accountId={accountId} firmName={firmName} />;
    case "lock":
      return <LockDayDialog accountId={accountId} firmName={firmName} />;
    case "settings":
      return <SettingsDialog />;
    case "shortcuts":
      return <ShortcutsDialog />;
    case "about":
      return <AboutDialog accountId={accountId} server={server} />;
    default:
      return null;
  }
}
