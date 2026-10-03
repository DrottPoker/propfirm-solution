"use client";

import { useMemo, useState } from "react";

import { CommandRejectedError } from "@/lib/api/client";
import type { EngineEvent, InstrumentInfo, PositionSnapshot } from "@/lib/api/types";
import { balanceOperationName, describeEvent, isWarning, type DigitsOf } from "@/lib/events";
import { formatMoney, formatPrice, formatTime, formatVolume } from "@/lib/format";
import { parsePrice } from "@/lib/orderInput";
import { useCancelOrder, useClosePosition, useModifyStops } from "@/lib/queries";
import { useTradingStore } from "@/lib/store";

const tabs = ["Positions", "Orders", "History", "Events"] as const;
type Tab = (typeof tabs)[number];

type ClosedPosition = Extract<EngineEvent, { kind?: "PositionClosed" }>;
type BalanceOperation = Extract<EngineEvent, { kind?: "BalanceAdjusted" }>;

// The generated kind is optional, so a plain comparison does not narrow the other branch.
const isBalanceOperation = (e: ClosedPosition | BalanceOperation): e is BalanceOperation => e.kind === "BalanceAdjusted";

export function BottomPanel({ accountId, instruments }: { accountId: string; instruments: InstrumentInfo[] }) {
  const [tab, setTab] = useState<Tab>("Positions");
  const [error, setError] = useState<string | null>(null);
  const account = useTradingStore((s) => s.account);
  const events = useTradingStore((s) => s.events);

  const digitsOf = useMemo<DigitsOf>(() => {
    const bySymbol = new Map(instruments.map((i) => [i.symbol, i.digits]));
    return (symbol) => bySymbol.get(symbol) ?? 5;
  }, [instruments]);

  const onError = (e: Error) => setError(e instanceof CommandRejectedError ? `Rejected: ${e.reason}` : "Could not reach the trading service.");

  const counts: Record<Tab, number | null> = {
    Positions: account?.positions.length ?? 0,
    Orders: account?.orders.length ?? 0,
    History: null,
    Events: null,
  };

  return (
    <section className="flex min-h-0 flex-col bg-panel text-sm">
      <nav className="flex items-center gap-1 border-b border-border px-2" role="tablist">
        {tabs.map((t) => (
          <button
            key={t}
            type="button"
            role="tab"
            aria-selected={t === tab}
            onClick={() => setTab(t)}
            className={`px-3 py-2 ${t === tab ? "border-b-2 border-accent text-foreground" : "text-muted hover:text-foreground"}`}
          >
            {t}
            {counts[t] ? ` (${counts[t]})` : ""}
          </button>
        ))}
        {error && (
          <button type="button" className="ml-auto px-2 text-loss" onClick={() => setError(null)} title="Dismiss">
            {error}
          </button>
        )}
      </nav>
      <div className="min-h-0 flex-1 overflow-y-auto" role="tabpanel">
        {tab === "Positions" && <Positions accountId={accountId} positions={account?.positions ?? []} digitsOf={digitsOf} onError={onError} />}
        {tab === "Orders" && <Orders accountId={accountId} digitsOf={digitsOf} onError={onError} />}
        {tab === "History" && <History events={events.map((e) => e.event)} digitsOf={digitsOf} />}
        {tab === "Events" && <Events events={events.map((e) => e.event)} digitsOf={digitsOf} />}
      </div>
    </section>
  );
}

function Positions({
  accountId,
  positions,
  digitsOf,
  onError,
}: {
  accountId: string;
  positions: PositionSnapshot[];
  digitsOf: DigitsOf;
  onError: (e: Error) => void;
}) {
  const close = useClosePosition(accountId);
  const [editing, setEditing] = useState<string | null>(null);

  if (positions.length === 0) {
    return <Empty text="No open positions." />;
  }

  return (
    <Table headers={["Position", "Symbol", "Side", "Volume", "Open", "Current", "SL", "TP", "Margin", "Profit", ""]}>
      {positions.map((p) => {
        const digits = digitsOf(p.symbol);
        return editing === p.positionId ? (
          <EditStops key={p.positionId} accountId={accountId} position={p} digits={digits} onDone={() => setEditing(null)} onError={onError} />
        ) : (
          <tr key={p.positionId} className="border-t border-border">
            <Cell>{shortId(p.positionId)}</Cell>
            <Cell>{p.symbol}</Cell>
            <Cell className={p.side === "Buy" ? "text-profit" : "text-loss"}>{p.side}</Cell>
            <Cell number>{formatVolume(p.volume)}</Cell>
            <Cell number>{formatPrice(p.openPrice, digits)}</Cell>
            <Cell number>{formatPrice(p.currentPrice, digits)}</Cell>
            <Cell number>{formatPrice(p.stopLoss, digits)}</Cell>
            <Cell number>{formatPrice(p.takeProfit, digits)}</Cell>
            <Cell number>{formatMoney(p.margin)}</Cell>
            <Cell number className={p.profit >= 0 ? "text-profit" : "text-loss"}>
              {formatMoney(p.profit)}
            </Cell>
            <Cell>
              <span className="flex justify-end gap-3">
                <button type="button" className="text-muted hover:text-foreground" onClick={() => setEditing(p.positionId)}>
                  SL/TP
                </button>
                <button type="button" className="text-muted hover:text-loss" disabled={close.isPending} onClick={() => close.mutate(p.positionId, { onError })}>
                  Close
                </button>
              </span>
            </Cell>
          </tr>
        );
      })}
    </Table>
  );
}

function EditStops({
  accountId,
  position,
  digits,
  onDone,
  onError,
}: {
  accountId: string;
  position: PositionSnapshot;
  digits: number;
  onDone: () => void;
  onError: (e: Error) => void;
}) {
  const modify = useModifyStops(accountId);
  const [stopLoss, setStopLoss] = useState(position.stopLoss?.toFixed(digits) ?? "");
  const [takeProfit, setTakeProfit] = useState(position.takeProfit?.toFixed(digits) ?? "");

  const save = () => {
    const sl = parsePrice(stopLoss, digits);
    const tp = parsePrice(takeProfit, digits);
    if (!sl.ok || !tp.ok) {
      onError(new Error(`Stop loss and take profit need at most ${digits} decimals.`));
      return;
    }

    modify.mutate({ positionId: position.positionId, stopLoss: sl.value, takeProfit: tp.value }, { onSuccess: onDone, onError });
  };

  return (
    <tr className="border-t border-border bg-accent/5">
      <Cell>{shortId(position.positionId)}</Cell>
      <Cell>{position.symbol}</Cell>
      <td colSpan={9} className="px-3 py-1">
        <span className="flex items-center gap-2">
          <StopInput label="SL" value={stopLoss} onChange={setStopLoss} />
          <StopInput label="TP" value={takeProfit} onChange={setTakeProfit} />
          <button type="button" className="rounded bg-accent px-3 py-1 text-white disabled:opacity-40" disabled={modify.isPending} onClick={save}>
            Save
          </button>
          <button type="button" className="text-muted hover:text-foreground" onClick={onDone}>
            Cancel
          </button>
        </span>
      </td>
    </tr>
  );
}

function StopInput({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  return (
    <label className="flex items-center gap-1">
      <span className="text-muted">{label}</span>
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder="None"
        inputMode="decimal"
        className="w-28 rounded border border-border bg-background px-2 py-0.5 font-mono tabular-nums outline-none focus:border-accent"
      />
    </label>
  );
}

function Orders({ accountId, digitsOf, onError }: { accountId: string; digitsOf: DigitsOf; onError: (e: Error) => void }) {
  const orders = useTradingStore((s) => s.account?.orders ?? []);
  const cancel = useCancelOrder(accountId);

  if (orders.length === 0) {
    return <Empty text="No pending orders." />;
  }

  return (
    <Table headers={["Order", "Symbol", "Type", "Side", "Volume", "Price", "SL", "TP", "Placed", ""]}>
      {orders.map((o) => {
        const digits = digitsOf(o.symbol);
        return (
          <tr key={o.orderId} className="border-t border-border">
            <Cell>{shortId(o.orderId)}</Cell>
            <Cell>{o.symbol}</Cell>
            <Cell>{o.type}</Cell>
            <Cell className={o.side === "Buy" ? "text-profit" : "text-loss"}>{o.side}</Cell>
            <Cell number>{formatVolume(o.volume)}</Cell>
            <Cell number>{formatPrice(o.price, digits)}</Cell>
            <Cell number>{formatPrice(o.stopLoss, digits)}</Cell>
            <Cell number>{formatPrice(o.takeProfit, digits)}</Cell>
            <Cell>{formatTime(o.placedTime)}</Cell>
            <Cell>
              <button type="button" className="text-muted hover:text-loss" disabled={cancel.isPending} onClick={() => cancel.mutate(o.orderId, { onError })}>
                Cancel
              </button>
            </Cell>
          </tr>
        );
      })}
    </Table>
  );
}

// Closed positions and balance operations such as payouts, newest first.
function History({ events, digitsOf }: { events: EngineEvent[]; digitsOf: DigitsOf }) {
  const rows = events
    .filter((e): e is ClosedPosition | BalanceOperation => e.kind === "PositionClosed" || e.kind === "BalanceAdjusted")
    .reverse();

  if (rows.length === 0) {
    return <Empty text="No closed positions yet." />;
  }

  return (
    <Table headers={["Closed", "Position", "Symbol", "Side", "Volume", "Open", "Close", "Reason", "Commission", "Profit"]}>
      {rows.map((c) => {
        if (isBalanceOperation(c)) {
          return (
            <tr key={`${c.operationId}-${c.timestamp}`} className="border-t border-border">
              <Cell>{formatTime(c.timestamp)}</Cell>
              <td colSpan={8} className="px-3 py-1 text-muted">
                {balanceOperationName(c.amount)}
              </td>
              <Cell number className={c.amount >= 0 ? "text-profit" : "text-loss"}>
                {formatMoney(c.amount)}
              </Cell>
            </tr>
          );
        }

        const digits = digitsOf(c.symbol);
        return (
          <tr key={`${c.positionId}-${c.timestamp}`} className="border-t border-border">
            <Cell>{formatTime(c.timestamp)}</Cell>
            <Cell>{shortId(c.positionId)}</Cell>
            <Cell>{c.symbol}</Cell>
            <Cell className={c.side === "Buy" ? "text-profit" : "text-loss"}>{c.side}</Cell>
            <Cell number>{formatVolume(c.volume)}</Cell>
            <Cell number>{formatPrice(c.openPrice, digits)}</Cell>
            <Cell number>{formatPrice(c.closePrice, digits)}</Cell>
            <Cell>{c.reason}</Cell>
            <Cell number>{formatMoney(c.commission)}</Cell>
            <Cell number className={c.profit >= 0 ? "text-profit" : "text-loss"}>
              {formatMoney(c.profit)}
            </Cell>
          </tr>
        );
      })}
    </Table>
  );
}

function Events({ events, digitsOf }: { events: EngineEvent[]; digitsOf: DigitsOf }) {
  if (events.length === 0) {
    return <Empty text="No events yet." />;
  }

  return (
    <ul className="divide-y divide-border">
      {[...events].reverse().map((event, index) => (
        <li key={`${event.timestamp}-${index}`} className={`flex gap-4 px-3 py-1.5 ${isWarning(event) ? "text-warning" : ""}`}>
          <span className="shrink-0 font-mono text-muted tabular-nums">{formatTime(event.timestamp)}</span>
          <span>{describeEvent(event, digitsOf)}</span>
          {event.kind === "EquityFloorBreached" && (
            <span className="text-muted">
              Prices: {event.prices.map((p) => `${p.symbol} ${formatPrice(p.bid, digitsOf(p.symbol))}/${formatPrice(p.ask, digitsOf(p.symbol))}`).join(", ") || "none"}
            </span>
          )}
        </li>
      ))}
    </ul>
  );
}

function Table({ headers, children }: { headers: string[]; children: React.ReactNode }) {
  return (
    <table className="w-full">
      <thead className="sticky top-0 bg-panel text-xs text-muted">
        <tr>
          {headers.map((h, i) => (
            <th key={`${h}-${i}`} className="px-3 py-1.5 text-left font-normal">
              {h}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>{children}</tbody>
    </table>
  );
}

function Cell({ children, number, className = "" }: { children: React.ReactNode; number?: boolean; className?: string }) {
  return <td className={`px-3 py-1 ${number ? "font-mono tabular-nums" : ""} ${className}`}>{children}</td>;
}

function Empty({ text }: { text: string }) {
  return <p className="px-3 py-4 text-muted">{text}</p>;
}

// Ids are client-generated UUIDs; the start is enough to tell them apart on screen.
function shortId(id: string): string {
  return id.length > 8 ? id.slice(0, 8) : id;
}
