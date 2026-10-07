"use client";

import { useId, useState } from "react";
import { toast } from "sonner";

import { accountName } from "@/lib/account";
import { CommandRejectedError } from "@/lib/api/client";
import type { OwnLimitsSnapshot } from "@/lib/api/types";
import { rejectionText } from "@/lib/events";
import { formatClock, formatMoney, timeZoneName } from "@/lib/format";
import { firmDailyLoss, limitsForm, parseLimitsForm, pendingText, stricterOf, type LimitsForm } from "@/lib/ownLimits";
import { useMe, useSetOwnLimits } from "@/lib/queries";
import { useSheet } from "@/lib/sheet";
import { useTradingStore } from "@/lib/store";

import { Sheet } from "./Sheet";

/**
 * The limits the trader sets for themselves (ADR 0054): a daily loss limit in the account currency or in percent of the
 * day's start, a daily profit target and the most trades a day. A stricter limit applies at once, a looser one from the
 * next trading day, which the panel says before the trader saves.
 */
export function OwnLimitsSheet({ accountId, firmName }: { accountId: string; firmName: string }) {
  const account = useTradingStore((s) => s.account);
  const details = useMe().data?.accountDetails.find((d) => d.accountId === accountId);
  return (
    <Sheet label="Your limits" title="Stricter than the firm's" subtitle={accountName(accountId, details)} printable={false}>
      {account ? (
        <LimitsBody accountId={accountId} own={account.ownLimits} currency={account.currency} firmLoss={firmDailyLoss(account.floors)} firmName={firmName} />
      ) : (
        <p className="px-5 py-4 text-sm text-muted">Loading...</p>
      )}
    </Sheet>
  );
}

function LimitsBody({
  accountId,
  own,
  currency,
  firmLoss,
  firmName,
}: {
  accountId: string;
  own: OwnLimitsSnapshot;
  currency: string;
  firmLoss: number | null;
  firmName: string;
}) {
  // What the trader last asked for, from the next trading day when some are loosened.
  const [form, setForm] = useState<LimitsForm>(() => limitsForm(own.pending ?? own.limits));
  const [tried, setTried] = useState(false);
  const setLimits = useSetOwnLimits(accountId);
  const close = useSheet((s) => s.close);
  const ids = { loss: useId(), target: useId(), trades: useId() };

  const day = own.tradingDay;
  const dayStarts = `${day.start.slice(0, 5)} ${timeZoneName(day.timeZone)}`;
  const nextDay = formatClock(own.nextDayStart, day.timeZone);
  const parsed = parseLimitsForm(form, own.dayStartBalance, firmLoss);
  const error = tried && !parsed.ok ? parsed : null;
  const wanted = parsed.ok ? parsed.limits : null;
  const applied = wanted ? stricterOf(own.limits, wanted) : null;
  const later = wanted && applied ? pendingText(applied, wanted, own.nextDayStart, day.timeZone) : null;

  const change = (changes: Partial<LimitsForm>) => setForm((f) => ({ ...f, ...changes }));
  const save = (e: React.FormEvent) => {
    e.preventDefault();
    setTried(true);
    if (!wanted) {
      return;
    }

    setLimits.mutate(wanted, {
      onSuccess: () => {
        // One note at a time, so saving again replaces it.
        toast.success("Your limits are saved", { id: "own-limits", description: later ? `The looser ones apply from ${nextDay}.` : "They apply now." });
        close();
      },
    });
  };

  const lossAmount = parsed.ok ? parsed.limits.dailyLoss : null;
  const targetAmount = parsed.ok ? parsed.limits.dailyTarget : null;
  const failure = setLimits.error instanceof CommandRejectedError ? rejectionText(setLimits.error.reason) : setLimits.error ? "Could not save your limits." : null;

  return (
    <form onSubmit={save} noValidate className="flex h-full flex-col">
      <div className="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto px-5 py-4 text-sm">
        <p className="leading-relaxed text-muted">
          Limits you set for yourself, on top of {firmName}&apos;s rules. They count from the start of each trading day, {dayStarts}, like the
          firm&apos;s daily loss limit.
        </p>

        <fieldset className="flex flex-col gap-2">
          <legend className="mb-2 font-semibold">Daily loss limit</legend>
          <div className="flex items-center gap-2">
            <label htmlFor={ids.loss} className="sr-only">
              Daily loss limit {form.lossUnit === "amount" ? `in ${currency}` : "in percent of the day's start"}
            </label>
            <LimitInput id={ids.loss} value={form.dailyLoss} invalid={error?.field === "dailyLoss"} onChange={(dailyLoss) => change({ dailyLoss })} />
            <span role="group" aria-label="Daily loss limit in" className="flex shrink-0 rounded-lg bg-raised p-0.5 ring-1 ring-border">
              {(["amount", "percent"] as const).map((unit) => (
                <button
                  key={unit}
                  type="button"
                  aria-pressed={form.lossUnit === unit}
                  onClick={() => change({ lossUnit: unit })}
                  className={`h-8 rounded-md px-2.5 text-xs font-medium transition-colors duration-150 ${form.lossUnit === unit ? "bg-accent text-accent-foreground" : "text-muted hover:text-foreground"}`}
                >
                  {unit === "amount" ? currency : "% of day start"}
                </button>
              ))}
            </span>
          </div>
          <FieldNote error={error?.field === "dailyLoss" ? error.message : null}>
            {lossAmount !== null && `Today that is equity ${formatMoney(own.dayStartBalance - lossAmount)}. `}
            {firmLoss !== null && `${firmName}'s daily loss limit is ${formatMoney(firmLoss)}, and yours must be smaller.`}
          </FieldNote>
        </fieldset>

        <fieldset className="flex flex-col gap-2">
          <legend className="mb-2 font-semibold">Daily profit target</legend>
          <div className="flex items-center gap-2">
            <label htmlFor={ids.target} className="sr-only">
              Daily profit target in {currency}
            </label>
            <LimitInput id={ids.target} value={form.dailyTarget} invalid={error?.field === "dailyTarget"} onChange={(dailyTarget) => change({ dailyTarget })} />
            <span className="w-32 shrink-0 text-xs text-muted">Off when empty</span>
          </div>
          <FieldNote error={error?.field === "dailyTarget" ? error.message : null}>
            {targetAmount !== null ? `Stop while you are ahead: today that is equity ${formatMoney(own.dayStartBalance + targetAmount)}.` : "Stop while you are ahead."}
          </FieldNote>
        </fieldset>

        <fieldset className="flex flex-col gap-2">
          <legend className="mb-2 font-semibold">Trades a day</legend>
          <div className="flex items-center gap-2">
            <label htmlFor={ids.trades} className="sr-only">
              Most trades a day
            </label>
            <LimitInput
              id={ids.trades}
              value={form.maxTrades}
              invalid={error?.field === "maxTrades"}
              onChange={(maxTrades) => change({ maxTrades })}
              numeric
              className="w-24"
            />
            <span className="text-xs text-muted">Each opened position counts. {own.tradesToday} so far today.</span>
          </div>
          {error?.field === "maxTrades" && <FieldNote error={error.message} />}
        </fieldset>

        <div className="flex flex-col gap-1.5 rounded-xl bg-raised px-4 py-3 leading-relaxed">
          <strong className="font-semibold">When a limit is reached</strong>
          <span className="text-muted">
            The loss limit or the profit target closes every position at that price and locks new orders until {nextDay}. The trade count only refuses
            new orders. None of it breaks a rule of {firmName}&apos;s.
          </span>
        </div>

        <div className="flex flex-col gap-1.5 rounded-xl border border-warning/35 bg-warning/5 px-4 py-3 leading-relaxed">
          <strong className="font-semibold text-warning">Stricter now, looser tomorrow</strong>
          <span className="text-muted">
            A stricter limit applies at once. A looser one, or one you turn off, applies from the next trading day, so a bad moment cannot undo it.
          </span>
          {later && (
            <span data-testid="limits-later" className="font-mono text-xs text-warning tabular-nums">
              {later}
            </span>
          )}
        </div>
      </div>

      <div className="flex flex-wrap items-center justify-end gap-2 border-t border-border px-5 py-3">
        {failure && (
          <p role="alert" className="mr-auto text-xs text-loss">
            {failure}
          </p>
        )}
        <button
          type="button"
          onClick={close}
          className="h-10 rounded-lg border border-border bg-raised px-4 font-medium transition duration-150 hover:border-muted active:translate-y-px"
        >
          Cancel
        </button>
        <button
          type="submit"
          disabled={setLimits.isPending}
          className="h-10 rounded-lg bg-accent px-4 font-semibold text-accent-foreground transition duration-150 hover:brightness-110 active:translate-y-px disabled:opacity-50"
        >
          Save limits
        </button>
      </div>
    </form>
  );
}

function LimitInput({
  id,
  value,
  invalid,
  onChange,
  numeric = false,
  className = "flex-1",
}: {
  id: string;
  value: string;
  invalid: boolean;
  onChange: (value: string) => void;
  numeric?: boolean;
  className?: string;
}) {
  return (
    <input
      id={id}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      inputMode={numeric ? "numeric" : "decimal"}
      autoComplete="off"
      placeholder="Off"
      aria-invalid={invalid || undefined}
      className={`h-10 min-w-0 rounded-lg border bg-background px-3 text-right font-mono tabular-nums outline-none transition-colors duration-150 placeholder:text-muted/60 focus:border-accent ${invalid ? "border-loss" : "border-border"} ${className}`}
    />
  );
}

function FieldNote({ error, children }: { error: string | null; children?: React.ReactNode }) {
  return error ? (
    <span role="alert" className="text-xs text-loss">
      {error}
    </span>
  ) : (
    <span className="text-xs leading-relaxed text-muted">{children}</span>
  );
}
