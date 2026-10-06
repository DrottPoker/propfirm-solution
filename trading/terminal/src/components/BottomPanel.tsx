"use client";

import { useMemo, useState } from "react";

import { CommandRejectedError } from "@/lib/api/client";
import type { EngineEvent, InstrumentInfo, PositionSnapshot, Side } from "@/lib/api/types";
import { balanceOperationName, closeReasons, describeEvent, isWarning, netResult, positionCommission, rejectionText, shortId, type DigitsOf } from "@/lib/events";
import { formatMoney, formatPrice, formatSignedMoney, formatTime, formatVolume } from "@/lib/format";
import { useCancelOrder, useClosePosition, useModifyStops, usePointValue } from "@/lib/queries";
import { estimatedProfit, resolveStops, type StopKind, type StopUnit } from "@/lib/stops";
import { useTradingStore } from "@/lib/store";
import { useTimeZone } from "@/lib/timeZone";

import { StopUnitToggle } from "./StopUnitToggle";

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

  const onError = (e: Error) => setError(e instanceof CommandRejectedError ? rejectionText(e.reason) : "Could not reach the trading service.");

  const counts: Record<Tab, number | null> = {
    Positions: account?.positions.length ?? 0,
    Orders: account?.orders.length ?? 0,
    History: null,
    Events: null,
  };

  return (
    <section className="flex min-h-0 flex-col rounded-lg border border-border bg-panel text-sm">
      <nav className="flex items-center gap-1 border-b border-border px-2" role="tablist">
        {tabs.map((t) => (
          <button
            key={t}
            type="button"
            role="tab"
            aria-selected={t === tab}
            onClick={() => setTab(t)}
            className={`-mb-px border-b-2 px-3 py-2.5 font-medium ${t === tab ? "border-accent text-foreground" : "border-transparent text-muted hover:text-foreground"}`}
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
    <Table headers={["Position", "Symbol", "Side", "Volume", "Open price", "Current price", "SL", "TP", "Margin", "Unrealized P/L", ""]}>
      {positions.map((p) => {
        const digits = digitsOf(p.symbol);
        return editing === p.positionId ? (
          <EditStops key={p.positionId} accountId={accountId} position={p} digits={digits} onDone={() => setEditing(null)} onError={onError} />
        ) : (
          <tr key={p.positionId} className="border-t border-border hover:bg-raised/50">
            <Cell className="font-mono text-muted">{shortId(p.positionId)}</Cell>
            <Cell className="font-medium">{p.symbol}</Cell>
            <Cell>
              <SideBadge side={p.side} />
            </Cell>
            <Cell number>{formatVolume(p.volume)}</Cell>
            <Cell number>{formatPrice(p.openPrice, digits)}</Cell>
            <Cell number>{formatPrice(p.currentPrice, digits)}</Cell>
            <StopCell accountId={accountId} position={p} price={p.stopLoss} digits={digits} />
            <StopCell accountId={accountId} position={p} price={p.takeProfit} digits={digits} />
            <Cell number>{formatMoney(p.margin)}</Cell>
            <Cell number className={p.profit >= 0 ? "text-profit" : "text-loss"}>
              {formatSignedMoney(p.profit)}
            </Cell>
            <Cell>
              <span className="flex justify-end gap-2">
                <ActionButton onClick={() => setEditing(p.positionId)}>Edit</ActionButton>
                <ActionButton danger disabled={close.isPending} onClick={() => close.mutate(p.positionId, { onError })}>
                  Close
                </ActionButton>
              </span>
            </Cell>
          </tr>
        );
      })}
    </Table>
  );
}

// The stop's price and what the position would make or lose there, before commission.
function StopCell({ accountId, position, price, digits }: { accountId: string; position: PositionSnapshot; price: number | null; digits: number }) {
  const pointValue = usePointValue(accountId, position.symbol).data;
  const amount = price !== null && pointValue ? estimatedProfit(position.side, position.volume, position.openPrice, price, digits, pointValue.perLot) : null;

  return (
    <Cell number>
      {formatPrice(price, digits)}
      {amount !== null && (
        <>
          {" "}
          <span className={`text-xs ${amount >= 0 ? "text-profit/80" : "text-loss/80"}`} title="Estimated result at this level, before commission">
            {formatSignedMoney(amount)}
          </span>
        </>
      )}
    </Cell>
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
  const pointValue = usePointValue(accountId, position.symbol).data ?? undefined;
  const currency = useTradingStore((s) => s.account?.currency);
  const [unit, setUnit] = useState<StopUnit>("price");
  const [fields, setFields] = useState<Record<StopUnit, Record<StopKind, string>>>({
    price: { stopLoss: position.stopLoss?.toFixed(digits) ?? "", takeProfit: position.takeProfit?.toFixed(digits) ?? "" },
    money: { stopLoss: "", takeProfit: "" },
  });

  const amountAt = (price: number | null) =>
    price !== null && pointValue ? estimatedProfit(position.side, position.volume, position.openPrice, price, digits, pointValue.perLot) : null;

  // Amounts start from the current stops, when they lose and win as a stop loss and take profit should.
  const switchUnit = (next: StopUnit) => {
    if (next === "money" && fields.money.stopLoss === "" && fields.money.takeProfit === "") {
      const loss = amountAt(position.stopLoss);
      const win = amountAt(position.takeProfit);
      setFields((f) => ({
        ...f,
        money: {
          stopLoss: loss !== null && loss < 0 ? (-loss).toFixed(2) : "",
          takeProfit: win !== null && win > 0 ? win.toFixed(2) : "",
        },
      }));
    }
    setUnit(next);
  };

  const setField = (kind: StopKind, value: string) => setFields((f) => ({ ...f, [unit]: { ...f[unit], [kind]: value } }));
  // Amounts are counted from the open price.
  const resolved = resolveStops(unit, fields[unit].stopLoss, fields[unit].takeProfit, position.side, position.openPrice, position.volume, digits, pointValue?.perLot);
  const preview = !resolved.ok
    ? ""
    : unit === "money"
      ? [resolved.stopLoss !== null && `SL ${formatPrice(resolved.stopLoss, digits)}`, resolved.takeProfit !== null && `TP ${formatPrice(resolved.takeProfit, digits)}`]
          .filter(Boolean)
          .join(" · ")
      : [resolved.stopLoss, resolved.takeProfit]
          .map((price, i) => {
            const amount = amountAt(price);
            return amount === null ? null : `${i === 0 ? "SL" : "TP"} ${formatSignedMoney(amount)}`;
          })
          .filter(Boolean)
          .join(" · ");

  const save = () => {
    if (!resolved.ok) {
      onError(new Error(resolved.error));
      return;
    }

    modify.mutate({ positionId: position.positionId, stopLoss: resolved.stopLoss, takeProfit: resolved.takeProfit }, { onSuccess: onDone, onError });
  };

  return (
    <tr className="border-t border-border bg-accent/5">
      <Cell>{shortId(position.positionId)}</Cell>
      <Cell>{position.symbol}</Cell>
      <td colSpan={9} className="px-3 py-1">
        <span className="flex items-center gap-2">
          <StopInput label="SL" value={fields[unit].stopLoss} onChange={(value) => setField("stopLoss", value)} />
          <StopInput label="TP" value={fields[unit].takeProfit} onChange={(value) => setField("takeProfit", value)} />
          <StopUnitToggle unit={unit} currency={pointValue?.currency ?? currency ?? "Money"} onChange={switchUnit} />
          <button type="button" className="rounded-md bg-accent px-3 py-1 text-xs font-medium text-white disabled:opacity-40" disabled={modify.isPending} onClick={save}>
            Save
          </button>
          <ActionButton onClick={onDone}>Cancel</ActionButton>
          {preview && <span className="font-mono text-xs text-muted tabular-nums">{preview}</span>}
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
        className="w-28 rounded-md border border-border bg-raised px-2 py-1 font-mono tabular-nums outline-none focus:border-accent"
      />
    </label>
  );
}

function Orders({ accountId, digitsOf, onError }: { accountId: string; digitsOf: DigitsOf; onError: (e: Error) => void }) {
  const orders = useTradingStore((s) => s.account?.orders ?? []);
  const cancel = useCancelOrder(accountId);
  const timeZone = useTimeZone();

  if (orders.length === 0) {
    return <Empty text="No pending orders." />;
  }

  return (
    <Table headers={["Order", "Symbol", "Type", "Side", "Volume", "Price", "SL", "TP", "Placed", ""]}>
      {orders.map((o) => {
        const digits = digitsOf(o.symbol);
        return (
          <tr key={o.orderId} className="border-t border-border hover:bg-raised/50">
            <Cell className="font-mono text-muted">{shortId(o.orderId)}</Cell>
            <Cell className="font-medium">{o.symbol}</Cell>
            <Cell>{o.type}</Cell>
            <Cell>
              <SideBadge side={o.side} />
            </Cell>
            <Cell number>{formatVolume(o.volume)}</Cell>
            <Cell number>{formatPrice(o.price, digits)}</Cell>
            <Cell number>{formatPrice(o.stopLoss, digits)}</Cell>
            <Cell number>{formatPrice(o.takeProfit, digits)}</Cell>
            <Cell>{formatTime(o.placedTime, timeZone)}</Cell>
            <Cell>
              <span className="flex justify-end">
                <ActionButton danger disabled={cancel.isPending} onClick={() => cancel.mutate(o.orderId, { onError })}>
                  Cancel
                </ActionButton>
              </span>
            </Cell>
          </tr>
        );
      })}
    </Table>
  );
}

// Closed positions and balance operations such as payouts, newest first. Commission is for both opening and closing, and
// the result is after it, as in the firm's portal.
function History({ events, digitsOf }: { events: EngineEvent[]; digitsOf: DigitsOf }) {
  const timeZone = useTimeZone();
  const rows = events
    .filter((e): e is ClosedPosition | BalanceOperation => e.kind === "PositionClosed" || e.kind === "BalanceAdjusted")
    .reverse();

  if (rows.length === 0) {
    return <Empty text="No closed positions yet." />;
  }

  return (
    <Table headers={["Closed", "Position", "Symbol", "Side", "Volume", "Open price", "Close price", "Reason", "Commission", "Result"]}>
      {rows.map((c) => {
        if (isBalanceOperation(c)) {
          return (
            <tr key={`${c.operationId}-${c.timestamp}`} className="border-t border-border hover:bg-raised/50">
              <Cell>{formatTime(c.timestamp, timeZone)}</Cell>
              <td colSpan={8} className="px-3 py-1.5 text-muted">
                {balanceOperationName(c.amount)}
              </td>
              <Cell number className={c.amount >= 0 ? "text-profit" : "text-loss"}>
                {formatSignedMoney(c.amount)}
              </Cell>
            </tr>
          );
        }

        const digits = digitsOf(c.symbol);
        const commission = positionCommission(c, events);
        const result = netResult(c, events);
        return (
          <tr key={`${c.positionId}-${c.timestamp}`} className="border-t border-border hover:bg-raised/50">
            <Cell>{formatTime(c.timestamp, timeZone)}</Cell>
            <Cell className="font-mono text-muted">{shortId(c.positionId)}</Cell>
            <Cell className="font-medium">{c.symbol}</Cell>
            <Cell>
              <SideBadge side={c.side} />
            </Cell>
            <Cell number>{formatVolume(c.volume)}</Cell>
            <Cell number>{formatPrice(c.openPrice, digits)}</Cell>
            <Cell number>{formatPrice(c.closePrice, digits)}</Cell>
            <Cell>{closeReasons[c.reason]}</Cell>
            <Cell number>{formatMoney(commission)}</Cell>
            <Cell number className={result >= 0 ? "text-profit" : "text-loss"}>
              <span title={`${formatSignedMoney(c.profit)} before ${formatMoney(commission)} commission`}>{formatSignedMoney(result)}</span>
            </Cell>
          </tr>
        );
      })}
    </Table>
  );
}

function Events({ events, digitsOf }: { events: EngineEvent[]; digitsOf: DigitsOf }) {
  const timeZone = useTimeZone();
  if (events.length === 0) {
    return <Empty text="No events yet." />;
  }

  return (
    <ul className="divide-y divide-border">
      {[...events].reverse().map((event, index) => (
        <li key={`${event.timestamp}-${index}`} className={`flex gap-4 px-3 py-2 ${isWarning(event) ? "text-warning" : ""}`}>
          <span className="shrink-0 font-mono text-muted tabular-nums">{formatTime(event.timestamp, timeZone)}</span>
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
            <th key={`${h}-${i}`} className="px-3 py-2 text-left font-normal whitespace-nowrap">
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
  return <td className={`px-3 py-1.5 whitespace-nowrap ${number ? "font-mono tabular-nums" : ""} ${className}`}>{children}</td>;
}

function SideBadge({ side }: { side: Side }) {
  const color = side === "Buy" ? "border-buy/40 bg-buy/10 text-buy" : "border-sell/40 bg-sell/10 text-sell";
  return <span className={`rounded border px-1.5 py-0.5 text-xs font-medium ${color}`}>{side}</span>;
}

function ActionButton({
  children,
  onClick,
  disabled,
  danger = false,
}: {
  children: React.ReactNode;
  onClick: () => void;
  disabled?: boolean;
  danger?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={`rounded-md border border-border px-3 py-1 text-xs font-medium text-muted hover:text-foreground disabled:opacity-40 ${danger ? "hover:border-loss/50 hover:text-loss" : "hover:border-muted"}`}
    >
      {children}
    </button>
  );
}

function Empty({ text }: { text: string }) {
  return <p className="px-3 py-6 text-center text-muted">{text}</p>;
}
