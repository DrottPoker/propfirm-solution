"use client";

import type { DigitsOf } from "@/lib/events";
import { useSheet } from "@/lib/sheet";

import { BreachReportSheet } from "./BreachReportSheet";
import { TradeDetailsSheet } from "./TradeDetailsSheet";

/** The panel open over the terminal, if any: a trade's details or the breach report (ADR 0053). */
export function Sheets({ accountId, digitsOf }: { accountId: string; digitsOf: DigitsOf }) {
  const sheet = useSheet((s) => s.sheet);
  if (!sheet) {
    return null;
  }

  return sheet.kind === "details" ? (
    <TradeDetailsSheet key={sheet.positionId} accountId={accountId} positionId={sheet.positionId} />
  ) : (
    <BreachReportSheet accountId={accountId} digitsOf={digitsOf} />
  );
}
