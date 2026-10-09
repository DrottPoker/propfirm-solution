"use client";

import { useCallback, useId, useRef, useState } from "react";

import type { OwnLimitsSnapshot } from "@/lib/api/types";
import { formatClock, timeZoneName } from "@/lib/format";
import { useNow } from "@/lib/marketHours";
import { canSetLimits, clockText, ownLimitRows, pendingText, type LimitTone, type OwnLimitRow } from "@/lib/ownLimits";
import { wordsFor } from "@/lib/profile";
import { useProfile } from "@/lib/profileContext";
import { useAccountRules } from "@/lib/queries";
import { attentionOf, rulebook, type RuleRow, type RuleState } from "@/lib/rules";
import { useSheet } from "@/lib/sheet";
import { useTradingStore } from "@/lib/store";
import { useTimeZone } from "@/lib/timeZone";

import { InfoIcon, LockIcon, RulesIcon, WarningIcon } from "./icons";
import { dismissRuleWarnings } from "./RuleWarnings";
import { Tooltip } from "./Tooltip";
import { useDismiss } from "./useDismiss";

// Where a rule stands is told by the colour of its note, never by a dot (ADR 0047).
const stateStyles: Record<RuleState, { note: string; name: string }> = {
  ok: { note: "text-muted", name: "" },
  warning: { note: "text-warning", name: "Close" },
  danger: { note: "text-loss", name: "Urgent" },
  done: { note: "text-profit", name: "Met" },
};

const toneValues: Record<LimitTone, string> = { accent: "text-foreground", profit: "text-foreground", warning: "text-warning", loss: "text-loss" };
const toneBars: Record<LimitTone, string> = { accent: "bg-accent", profit: "bg-profit", warning: "bg-warning", loss: "bg-loss" };

/**
 * The account's whole rulebook behind a button in the account bar: the profit target, the loss limits, the trading
 * days, the deadlines, the consistency rule and a funded account's payout, each with where it stands (ADR 0052). The
 * button turns amber or red when a rule needs attention. The account's page at the firm, when there is one, has every
 * rule in full. Under the firm's rules, which decide the account, are the trader's own limits, which the trader changes
 * here or locks the rest of the day with (ADR 0054). Which of the two are shown, and the button's name, follow the
 * firm's terminal profile (ADR 0058).
 */
export function RulesMenu({
  accountId,
  profitTarget,
  firmName,
  detailsUrl,
}: {
  accountId: string;
  profitTarget: number | null;
  firmName: string;
  detailsUrl: string | null;
}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const panelId = useId();
  const close = useCallback(() => setOpen(false), []);
  useDismiss(open, ref, close);

  const account = useTradingStore((s) => s.account);
  const profile = useProfile();
  const name = wordsFor(profile.kind).rules;
  const rules = useAccountRules(accountId).data ?? null;
  const now = useNow(30_000);
  const timeZone = useTimeZone();
  const rows = account && now && profile.modules.rulebook ? rulebook({ account, profitTarget, rules, now: now.getTime(), timeZone }) : [];
  const attention = attentionOf(rows);
  const openSheet = useSheet((s) => s.open);
  const openOwn = (kind: "limits" | "lock") => {
    setOpen(false);
    openSheet({ kind });
  };

  return (
    <div ref={ref} data-tour="rules" className="relative shrink-0">
      <button
        type="button"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => {
          if (!open) {
            dismissRuleWarnings();
          }

          setOpen(!open);
        }}
        title={attention ? "A rule needs your attention" : `The account's ${name.toLowerCase()}`}
        className={`flex h-9 items-center gap-1.5 rounded-lg bg-raised px-3 text-xs font-medium ring-1 transition duration-150 hover:ring-accent active:translate-y-px ${
          open ? "ring-accent" : attention === "danger" ? "text-loss ring-loss/50" : attention ? "text-warning ring-warning/50" : "ring-border"
        }`}
      >
        {attention ? (
          <span data-testid="rules-attention" aria-label={attention === "danger" ? "Urgent" : "Needs attention"} className="flex">
            <WarningIcon className="size-4" />
          </span>
        ) : (
          <RulesIcon />
        )}
        <span className="max-sm:sr-only">{name}</span>
      </button>
      {open && (
        <div
          id={panelId}
          role="dialog"
          aria-label={name}
          className="absolute top-11 right-0 z-20 max-h-[calc(100vh-5rem)] w-96 max-w-[calc(100vw-1rem)] origin-top-right animate-pop overflow-y-auto rounded-xl border border-border bg-panel p-1 shadow-float max-sm:-right-12"
        >
          {profile.modules.rulebook && (
            <section aria-label={`${firmName}'s ${name.toLowerCase()}`}>
              <div className="flex items-baseline justify-between gap-3 px-3 pt-2.5 pb-1">
                <h3 className="text-xs font-semibold">
                  {firmName}&apos;s {name.toLowerCase()}
                </h3>
                <span className="text-[11px] text-muted">Times in {timeZoneName(timeZone)}</span>
              </div>
              {rows.length > 0 ? (
                <ul className="flex flex-col pb-1">
                  {rows.map((row) => (
                    <Rule key={row.id} row={row} payoutUrl={detailsUrl} firmName={firmName} />
                  ))}
                </ul>
              ) : (
                <p className="px-3 py-3 text-xs text-muted">Loading...</p>
              )}
              {detailsUrl && (
                <a href={detailsUrl} className="block px-3 pb-2.5 text-xs text-muted transition-colors duration-150 hover:text-foreground">
                  Every rule in full at {firmName}
                </a>
              )}
            </section>
          )}
          {account && profile.modules.ownLimits && canSetLimits(account) && (
            <OwnLimits own={account.ownLimits} equity={account.equity} firmName={firmName} onOpen={openOwn} />
          )}
        </div>
      )}
    </div>
  );
}

function OwnLimits({
  own,
  equity,
  firmName,
  onOpen,
}: {
  own: OwnLimitsSnapshot;
  equity: number;
  firmName: string;
  onOpen: (kind: "limits" | "lock") => void;
}) {
  const rows = ownLimitRows(own, equity);
  const timeZone = own.tradingDay.timeZone;
  const later = pendingText(own.limits, own.pending, own.nextDayStart, timeZone);
  const until = formatClock(own.nextDayStart, timeZone);
  return (
    <section aria-label="Your own limits" className="flex flex-col gap-2.5 border-t border-border px-3 py-2.5 first:border-t-0">
      <div className="flex items-baseline justify-between gap-3">
        <h3 className="text-xs font-semibold">Your own limits</h3>
        <span className="text-[11px] text-muted">Today, until {clockText(own.nextDayStart, timeZone)}</span>
      </div>
      {rows.length === 0 ? (
        <p className="text-xs leading-relaxed text-muted">
          Set a daily loss limit, a daily profit target or a number of trades for yourself, on top of {firmName}&apos;s rules.
        </p>
      ) : (
        rows.map((row) => <OwnLimit key={row.id} row={row} />)
      )}
      {later && <p className="text-[11px] text-warning tabular-nums">{later}</p>}
      <div className="flex flex-wrap items-center gap-2 pt-0.5">
        <button
          type="button"
          onClick={() => onOpen("limits")}
          className="h-8 rounded-lg bg-accent px-3 text-xs font-semibold text-accent-foreground transition duration-150 hover:brightness-110 active:translate-y-px"
        >
          {rows.length === 0 && !own.pending ? "Set your limits" : "Change your limits"}
        </button>
        {own.lock ? (
          <span className="flex h-8 items-center gap-1.5 px-1 text-xs font-medium text-accent">
            <LockIcon className="size-3.5" />
            Locked until {until}
          </span>
        ) : (
          <button
            type="button"
            onClick={() => onOpen("lock")}
            className="flex h-8 items-center gap-1.5 rounded-lg border border-border bg-raised px-3 text-xs font-medium transition duration-150 hover:border-muted active:translate-y-px"
          >
            <LockIcon className="size-3.5" />
            Lock the rest of the day
          </button>
        )}
      </div>
    </section>
  );
}

function OwnLimit({ row }: { row: OwnLimitRow }) {
  return (
    <div className="flex flex-col gap-1" data-own-limit={row.id}>
      <div className="flex justify-between gap-3 text-xs">
        <span>{row.label}</span>
        <span className={`text-[13px] tabular-nums ${toneValues[row.tone]}`}>{row.value}</span>
      </div>
      <div aria-hidden="true" className="h-1 overflow-hidden rounded-full bg-border">
        <div className={`h-full rounded-full transition-[width] duration-300 ${toneBars[row.tone]}`} style={{ width: `${Math.round(row.used * 100)}%` }} />
      </div>
      <span className="text-[11px] text-muted">{row.note}</span>
    </div>
  );
}

function Rule({ row, payoutUrl, firmName }: { row: RuleRow; payoutUrl: string | null; firmName: string }) {
  const style = stateStyles[row.state];
  return (
    <li className="flex items-start gap-2.5 rounded-md px-3 py-1.5 text-xs" data-rule={row.id}>
      <Tooltip content={row.help} focusable className="min-w-0 flex-1">
        <span className="flex items-center gap-1 text-muted">
          {row.label}
          <InfoIcon className="size-3" />
        </span>
      </Tooltip>
      <span className="flex flex-col items-end text-right">
        <span className="text-[13px] text-foreground tabular-nums">{row.value}</span>
        {row.note && <span className={`text-[11px] tabular-nums ${style.note}`}>{row.note}</span>}
        {style.name && <span className="sr-only">{style.name}</span>}
        {row.action === "payout" && payoutUrl && (
          <a href={payoutUrl} className="mt-0.5 text-[11px] font-medium text-accent hover:underline">
            Ask for it at {firmName}
          </a>
        )}
      </span>
    </li>
  );
}
