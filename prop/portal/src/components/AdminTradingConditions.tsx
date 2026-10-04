"use client";

import { useState } from "react";

import type { TradingConditions } from "@/lib/api/types";
import { useFirmSettings, useSaveTradingConditions, useTradingConditions } from "@/lib/queries";
import { conditionRows, conditionsRequest, type ConditionRow } from "@/lib/tradingConditions";

import { AdminPage, buttonClass, ErrorText, fieldClass, Message, PageHeader, Panel } from "./ui";

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
    return <Message text="Loading..." />;
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
  return (
    <Panel>
      {readOnly && <p className="text-sm text-muted">Your trading conditions are set by us. Ask us if you want them changed.</p>}
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
            <tbody>
              {rows.map((row) => {
                const instrument = details.get(row.symbol);
                const off = !row.enabled || readOnly;
                return (
                  <tr key={row.symbol} className="border-t border-border">
                    <td className="py-2.5 pr-4">
                      <label className="flex items-center gap-2.5">
                        <input
                          type="checkbox"
                          checked={row.enabled}
                          disabled={readOnly}
                          onChange={(e) => change(row.symbol, { enabled: e.target.checked })}
                          aria-label={`Traders trade ${row.symbol}`}
                        />
                        <span className="flex flex-col">
                          <span className="font-mono font-medium">{row.symbol}</span>
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
            {save.isSuccess && (
              <p role="status" className="text-sm text-profit">
                Saved. Your traders trade on these conditions now.
              </p>
            )}
            <button type="submit" disabled={save.isPending} className={`${buttonClass} self-start`}>
              {save.isPending ? "Saving..." : "Save conditions"}
            </button>
          </>
        )}
      </form>
    </Panel>
  );
}
