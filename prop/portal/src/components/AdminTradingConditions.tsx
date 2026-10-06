"use client";

import { useState } from "react";

import type { TradingConditions } from "@/lib/api/types";
import { instrumentGroup, instrumentGroups, instrumentName, type InstrumentGroup } from "@/lib/instruments";
import { useFirmSettings, useSaveTradingConditions, useTradingConditions } from "@/lib/queries";
import { applyToGroup, conditionRows, conditionsRequest, type BulkConditions, type ConditionRow } from "@/lib/tradingConditions";
import { useSavedNote } from "@/lib/useSavedNote";

import { InstrumentMark } from "./InstrumentMark";
import { AdminPage, buttonClass, ErrorText, fieldClass, Loading, Message, PageHeader, Panel, secondaryButtonClass } from "./ui";

/**
 * What the firm's traders trade, and on what terms: the instruments, the leverage, the markup on the spread and the
 * commission. A change applies at once to every account of the firm, also to open trades.
 */
export function AdminTradingConditions() {
  const settings = useFirmSettings();
  const ready = settings.data !== undefined && settings.data.status !== "Provisioning";
  const conditions = useTradingConditions(ready);

  if (settings.data?.status === "Provisioning") {
    return <Message text="Your trading server is being set up. This takes a few seconds." />;
  }

  if (settings.isError || conditions.isError) {
    return <Message text={(settings.error ?? conditions.error)?.message ?? "The trading conditions cannot be loaded."} />;
  }

  if (!conditions.data) {
    return <Loading />;
  }

  return (
    <AdminPage>
      <PageHeader
        title="Trading conditions"
        description="The instruments your traders trade in the terminal, and on what terms. Changes apply at once to every account, also to open trades."
      />
      <ConditionsForm conditions={conditions.data} />
    </AdminPage>
  );
}

function ConditionsForm({ conditions }: { conditions: TradingConditions }) {
  const save = useSaveTradingConditions();
  useSavedNote(save, "Saved. Your traders trade on these conditions now.");
  const [rows, setRows] = useState<ConditionRow[]>(() => conditionRows(conditions.symbols));
  const [problem, setProblem] = useState<string | null>(null);
  const readOnly = !conditions.changeable;

  const change = (symbol: string, changes: Partial<ConditionRow>) => {
    save.reset();
    setRows((current) => current.map((r) => (r.symbol === symbol ? { ...r, ...changes } : r)));
  };

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    const request = conditionsRequest(rows);
    setProblem("problem" in request ? request.problem : null);
    if ("symbols" in request) {
      save.mutate(request.symbols, { onSuccess: (saved) => setRows(conditionRows(saved.symbols)) });
    }
  };

  const details = new Map(conditions.symbols.map((s) => [s.symbol, s]));
  // The instruments by group, Forex first, so a firm sees its currency pairs and its metals apart.
  const groups = instrumentGroups
    .map((group) => ({ group, rows: rows.filter((r) => instrumentGroup(r.symbol) === group) }))
    .filter((g) => g.rows.length > 0);
  return (
    <Panel>
      {readOnly && <p className="text-sm text-muted">Your trading conditions are set by us. Ask us if you want them changed.</p>}
      {!readOnly && (
        <BulkBar
          groups={groups.map((g) => g.group)}
          onApply={(group, values) => {
            save.reset();
            setRows((current) => applyToGroup(current, group, values));
          }}
        />
      )}
      <form onSubmit={submit} className="flex flex-col gap-4">
        <div className="overflow-x-auto">
          <table className="w-full min-w-[44rem] text-sm">
            <thead className="text-left text-muted">
              <tr>
                <th scope="col" className="py-2 pr-4 font-normal">
                  Instrument
                </th>
                <th scope="col" className="py-2 pr-4 font-normal">
                  Leverage
                </th>
                <th scope="col" className="py-2 pr-4 font-normal">
                  Spread markup
                </th>
                <th scope="col" className="py-2 font-normal">
                  Commission per lot and side
                </th>
              </tr>
            </thead>
            {groups.map(({ group, rows: groupRows }) => (
              <tbody key={group}>
                <tr>
                  <th colSpan={4} scope="colgroup" className="pb-1.5 pt-5 text-left text-[11px] font-semibold uppercase tracking-[0.14em] text-muted">
                    {group}
                  </th>
                </tr>
                {groupRows.map((row) => {
                  const instrument = details.get(row.symbol);
                  const off = !row.enabled || readOnly;
                  return (
                    <tr key={row.symbol} className={`border-t border-border transition-opacity ${row.enabled ? "" : "opacity-60"}`}>
                      <td className="py-2.5 pr-4">
                        <label className="flex items-center gap-2.5">
                          <input
                            type="checkbox"
                            checked={row.enabled}
                            disabled={readOnly}
                            onChange={(e) => change(row.symbol, { enabled: e.target.checked })}
                            aria-label={`Traders trade ${row.symbol}`}
                          />
                          <InstrumentMark symbol={row.symbol} />
                          <span className="flex flex-col">
                            <span className="font-medium">
                              {row.symbol} <span className="font-normal text-muted">{instrumentName(row.symbol) !== row.symbol && instrumentName(row.symbol)}</span>
                            </span>
                            {instrument && (
                              <span className="text-xs text-muted">
                                1 lot = {instrument.contractSize.toLocaleString("en-US")} {instrument.baseCurrency}
                              </span>
                            )}
                          </span>
                        </label>
                      </td>
                      <td className="py-2.5 pr-4">
                        <span className="flex items-center gap-1">
                          <span className="text-muted">1:</span>
                          <input
                            aria-label={`${row.symbol} leverage`}
                            inputMode="numeric"
                            disabled={off}
                            value={row.leverage}
                            onChange={(e) => change(row.symbol, { leverage: e.target.value })}
                            className={`${fieldClass} w-20 py-1 text-right disabled:opacity-50`}
                          />
                        </span>
                      </td>
                      <td className="py-2.5 pr-4">
                        <span className="flex items-center gap-1.5">
                          <input
                            aria-label={`${row.symbol} spread markup in points`}
                            inputMode="numeric"
                            disabled={off}
                            value={row.spreadMarkupPoints}
                            onChange={(e) => change(row.symbol, { spreadMarkupPoints: e.target.value })}
                            className={`${fieldClass} w-20 py-1 text-right disabled:opacity-50`}
                          />
                          <span className="text-muted">points</span>
                        </span>
                      </td>
                      <td className="py-2.5">
                        <span className="flex items-center gap-1.5">
                          <input
                            aria-label={`${row.symbol} commission per lot and side`}
                            inputMode="decimal"
                            disabled={off}
                            value={row.commissionPerLotPerSide}
                            onChange={(e) => change(row.symbol, { commissionPerLotPerSide: e.target.value })}
                            className={`${fieldClass} w-24 py-1 text-right disabled:opacity-50`}
                          />
                          <span className="text-muted">{conditions.currency}</span>
                        </span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            ))}
          </table>
        </div>
        <p className="text-xs text-muted">
          Margin is the trade&apos;s value divided by the leverage. The markup is added to the market&apos;s spread, in the instrument&apos;s smallest
          price step. The commission is charged when a trade opens and again when it closes. An instrument with open trades or orders can be turned
          off once they are closed.
        </p>
        {!readOnly && (
          <>
            <ErrorText error={problem ? new Error(problem) : save.error} />
            <button type="submit" disabled={save.isPending} className={`${buttonClass} self-start`}>
              {save.isPending ? "Saving..." : "Save conditions"}
            </button>
          </>
        )}
      </form>
    </Panel>
  );
}

/**
 * Conditions typed once for a whole group of instruments, such as every forex pair, put on each that is traded. Empty
 * fields leave each instrument's own. Nothing is saved until the form is.
 */
function BulkBar({ groups, onApply }: { groups: InstrumentGroup[]; onApply: (group: InstrumentGroup | "All", values: BulkConditions) => void }) {
  const [group, setGroup] = useState<InstrumentGroup | "All">("All");
  const [values, setValues] = useState<BulkConditions>({ leverage: "", spreadMarkupPoints: "", commissionPerLotPerSide: "" });
  const set = (changes: Partial<BulkConditions>) => setValues((current) => ({ ...current, ...changes }));
  const empty = Object.values(values).every((v) => v.trim() === "");

  return (
    <div className="flex flex-wrap items-end gap-3 rounded-xl border border-border bg-background/40 p-3.5 text-sm">
      <label className="flex flex-col gap-1">
        <span className="text-xs text-muted">Set for</span>
        <select aria-label="Set for" value={group} onChange={(e) => setGroup(e.target.value as InstrumentGroup | "All")} className={`${fieldClass} py-1.5`}>
          <option value="All">Every instrument</option>
          {groups.map((g) => (
            <option key={g} value={g}>
              {g === "Forex" ? "Every forex pair" : g === "Metals" ? "Every metal" : "Every other instrument"}
            </option>
          ))}
        </select>
      </label>
      <label className="flex flex-col gap-1">
        <span className="text-xs text-muted">Leverage 1:</span>
        <input aria-label="Leverage for all" inputMode="numeric" value={values.leverage} onChange={(e) => set({ leverage: e.target.value })} className={`${fieldClass} w-20 py-1.5 text-right`} />
      </label>
      <label className="flex flex-col gap-1">
        <span className="text-xs text-muted">Markup, points</span>
        <input aria-label="Spread markup for all" inputMode="numeric" value={values.spreadMarkupPoints} onChange={(e) => set({ spreadMarkupPoints: e.target.value })} className={`${fieldClass} w-20 py-1.5 text-right`} />
      </label>
      <label className="flex flex-col gap-1">
        <span className="text-xs text-muted">Commission</span>
        <input
          aria-label="Commission for all"
          inputMode="decimal"
          value={values.commissionPerLotPerSide}
          onChange={(e) => set({ commissionPerLotPerSide: e.target.value })}
          className={`${fieldClass} w-24 py-1.5 text-right`}
        />
      </label>
      <button type="button" disabled={empty} onClick={() => onApply(group, values)} className={`${secondaryButtonClass} py-1.5`}>
        Fill in below
      </button>
      <span className="basis-full text-xs text-muted">Fills in the instruments that are traded. Empty fields leave each one as it is. Save the conditions below to use them.</span>
    </div>
  );
}
