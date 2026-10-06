"use client";

import { useCallback, useId, useRef, useState } from "react";

import { timeZoneName } from "@/lib/format";
import { useNow } from "@/lib/marketHours";
import { useAccountRules } from "@/lib/queries";
import { attentionOf, rulebook, type RuleRow, type RuleState } from "@/lib/rules";
import { useTradingStore } from "@/lib/store";
import { useTimeZone } from "@/lib/timeZone";

import { InfoIcon, RulesIcon } from "./icons";
import { dismissRuleWarnings } from "./RuleWarnings";
import { useDismiss } from "./useDismiss";

const stateStyles: Record<RuleState, { note: string; mark: string; name: string }> = {
  ok: { note: "text-muted", mark: "bg-border", name: "" },
  warning: { note: "text-warning", mark: "bg-warning", name: "Close" },
  danger: { note: "text-loss", mark: "bg-loss", name: "Urgent" },
  done: { note: "text-profit", mark: "bg-profit", name: "Met" },
};

/**
 * The account's whole rulebook behind a button in the account bar: the profit target, the loss limits, the trading
 * days, the deadlines and the consistency rule, each with where it stands (ADR 0052). The button shows a dot when a
 * rule needs attention. The account's page at the firm, when there is one, has every rule in full.
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
  const rules = useAccountRules(accountId).data ?? null;
  const now = useNow(30_000);
  const timeZone = useTimeZone();
  const rows = account && now ? rulebook({ account, profitTarget, rules, now: now.getTime(), timeZone }) : [];
  const attention = attentionOf(rows);

  return (
    <div ref={ref} className="relative shrink-0">
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
        title={attention ? "A rule needs your attention" : "The account's rules"}
        className={`relative flex h-9 items-center gap-1.5 rounded-full bg-raised px-3 text-xs font-medium ring-1 transition duration-150 hover:ring-accent active:translate-y-px ${open ? "ring-accent" : "ring-border"}`}
      >
        <RulesIcon />
        <span className="max-sm:sr-only">Rules</span>
        {attention && (
          <span
            data-testid="rules-attention"
            aria-label={attention === "danger" ? "Urgent" : "Needs attention"}
            className={`absolute -top-0.5 -right-0.5 size-2.5 rounded-full ring-2 ring-panel ${attention === "danger" ? "bg-loss" : "bg-warning"}`}
          />
        )}
      </button>
      {open && (
        <div
          id={panelId}
          role="dialog"
          aria-label="Rules"
          className="absolute top-11 right-0 z-20 w-80 max-w-[calc(100vw-1rem)] origin-top-right animate-pop rounded-xl border border-border bg-panel p-1 shadow-float max-sm:-right-12"
        >
          <div className="border-b border-border px-3 py-2.5">
            <p className="font-medium">Rules of this account</p>
            <p className="text-xs text-muted">As {firmName} checks them. Times in {timeZoneName(timeZone)}.</p>
          </div>
          {rows.length > 0 ? (
            <ul className="flex flex-col py-1">
              {rows.map((row) => (
                <Rule key={row.id} row={row} />
              ))}
            </ul>
          ) : (
            <p className="px-3 py-3 text-xs text-muted">Loading...</p>
          )}
          {detailsUrl && (
            <a
              href={detailsUrl}
              className="block border-t border-border px-3 py-2 text-xs text-muted transition-colors duration-150 hover:text-foreground"
            >
              Every rule in full at {firmName}
            </a>
          )}
        </div>
      )}
    </div>
  );
}

function Rule({ row }: { row: RuleRow }) {
  const style = stateStyles[row.state];
  return (
    <li className="flex items-start gap-2.5 rounded-md px-3 py-1.5 text-xs" title={row.help} data-rule={row.id}>
      <span aria-hidden="true" className={`mt-1 size-2 shrink-0 rounded-full ${style.mark}`} />
      <span className="flex min-w-0 flex-1 items-center gap-1 text-muted">
        {row.label}
        <InfoIcon className="size-3" />
      </span>
      <span className="flex flex-col items-end text-right">
        <span className="font-mono text-[13px] text-foreground tabular-nums">{row.value}</span>
        {row.note && <span className={`font-mono text-[11px] tabular-nums ${style.note}`}>{row.note}</span>}
        {style.name && <span className="sr-only">{style.name}</span>}
      </span>
    </li>
  );
}
