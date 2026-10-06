"use client";

import { useEffect, useRef } from "react";
import { toast } from "sonner";

import { useNow } from "@/lib/marketHours";
import { useAccountRules } from "@/lib/queries";
import { nextWarnings, type RuleWarning, type WarningMemory } from "@/lib/ruleWarnings";
import { useSettings } from "@/lib/settings";
import { playFillSound, playWarningSound } from "@/lib/sound";
import { useTradingStore } from "@/lib/store";

/** How long a warning stays: long enough to read while trading. */
const warningDuration = 15_000;

// The rules the notes in the corner are about, so opening the rulebook can clear them.
const shown = new Set<string>();

/** Clears the notes about the rules, for example when the trader opens the rulebook, which says the same. */
export function dismissRuleWarnings() {
  shown.forEach((id) => toast.dismiss(id));
  shown.clear();
}

/**
 * Warns the trader in a note in the corner when a loss limit comes close or is broken, a deadline comes close, the
 * best day goes above the consistency rule or the profit target is reached, with a sound unless the trader turned it
 * off (ADR 0052). What already holds when the terminal opens is told without a sound.
 */
export function useRuleWarnings(accountId: string, profitTarget: number | null, timeZone: string) {
  const account = useTradingStore((s) => s.account);
  const rules = useAccountRules(accountId);
  const now = useNow(30_000);
  const memory = useRef<WarningMemory | null>(null);
  const rulesLoaded = !rules.isPending;
  const rulesData = rules.data ?? null;

  useEffect(() => {
    if (!account || !rulesLoaded || !now) {
      return;
    }

    const first = memory.current === null;
    const next = nextWarnings(memory.current, { account, profitTarget, rules: rulesData, now: now.getTime(), timeZone });
    memory.current = next.memory;
    next.warnings.forEach(showWarning);
    if (!first && next.warnings.length > 0 && useSettings.getState().warningSound) {
      if (next.warnings.some((w) => w.level !== "success")) {
        playWarningSound();
      } else {
        playFillSound();
      }
    }
  }, [account, rulesLoaded, rulesData, now, profitTarget, timeZone]);
}

function showWarning(warning: RuleWarning) {
  shown.add(warning.id);
  const options = { id: warning.id, description: warning.description, duration: warningDuration };
  if (warning.level === "success") {
    toast.success(warning.title, options);
  } else if (warning.level === "danger") {
    toast.error(warning.title, options);
  } else {
    toast.warning(warning.title, options);
  }
}
