"use client";

import { useEffect, useState } from "react";

import { CommandRejectedError } from "@/lib/api/client";
import type { EngineEvent, InstrumentLimits, OrderSnapshot, PositionSnapshot, Side } from "@/lib/api/types";
import {
  balanceOperationName,
  closeReasons,
  describeEvent,
  isWarning,
  netResult,
  partResult,
  positionCommission,
  rejectionText,
  type DigitsOf,
} from "@/lib/events";
import { formatMoney, formatPrice, formatSignedMoney, formatTime, formatVolume, timeZoneName } from "@/lib/format";
import { isClosed, opensText } from "@/lib/marketHours";
import { parsePrice } from "@/lib/orderInput";
import { breakEvenStop, defaultPart, partProblem, trailingPips } from "@/lib/orderTools";
import {
  useCancelOrder,
  useCloseAllPositions,
  useClosePosition,
  useInstruments,
  useMarketHours,
  useModifyOrder,
  useModifyStops,
  usePointValue,
} from "@/lib/queries";
import { estimatedProfit, resolveStops, type StopKind, type StopUnit } from "@/lib/stops";
import { useSheet } from "@/lib/sheet";
import { useTradingStore } from "@/lib/store";
import { useTimeZone } from "@/lib/timeZone";

import { StopUnitToggle } from "./StopUnitToggle";
import { showError } from "./TradeNotices";

const tabs = ["Positions", "Orders", "History", "Events"] as const;
type Tab = (typeof tabs)[number];

type ClosedPosition = Extract<EngineEvent, { kind?: "PositionClosed" }>;
type ClosedPart = Extract<EngineEvent, { kind?: "PositionPartiallyClosed" }>;
type BalanceOperation = Extract<EngineEvent, { kind?: "BalanceAdjusted" }>;
type HistoryRow = ClosedPosition | ClosedPart | BalanceOperation;

// The generated kind is optional, so a plain comparison does not narrow the other branch.
const isBalanceOperation = (e: HistoryRow): e is BalanceOperation => e.kind === "BalanceAdjusted";
const isPart = (e: HistoryRow): e is ClosedPart => e.kind === "PositionPartiallyClosed";

// A refusal comes and goes in a corner like a fill, so it is seen wherever the trader looks.
const onError = (e: Error) => showError(e instanceof CommandRejectedError ? rejectionText(e.reason) : "Could not reach the trading service.");

export function BottomPanel({ accountId, digitsOf }: { accountId: string; digitsOf: DigitsOf }) {
  const [tab, setTab] = useState<Tab>("Positions");
  const account = useTradingStore((s) => s.account);
  const events = useTradingStore((s) => s.events);

  const counts: Record<Tab, number | null> = {
    Positions: account?.positions.length ?? 0,
    Orders: account?.orders.length ?? 0,
    History: null,
    Events: null,
  };

  return (
    <section className="flex min-h-0 flex-col overflow-hidden rounded-xl border border-border bg-panel text-sm shadow-card">
      <nav className="flex items-center gap-1 overflow-x-auto border-b border-border px-2" role="tablist">
        {tabs.map((t) => (
          <button
            key={t}
            type="button"
            role="tab"
            aria-selected={t === tab}
            onClick={() => setTab(t)}
            className={`-mb-px border-b-2 px-3 py-2.5 font-medium whitespace-nowrap transition-colors duration-150 ${t === tab ? "border-accent text-foreground" : "border-transparent text-muted hover:text-foreground"}`}
          >
            {t}
            {counts[t] ? ` (${counts[t]})` : ""}
          </button>
        ))}
        {tab === "Positions" && (account?.positions.length ?? 0) > 0 && <CloseAll accountId={accountId} count={account?.positions.length ?? 0} />}
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
  const modify = useModifyStops(accountId);
  // The row being edited, and whether its stops or a part to close.
  const [editing, setEditing] = useState<{ positionId: string; what: "stops" | "part" } | null>(null);
  const instruments = useInstruments(accountId).data;
  const markets = useMarketHours(accountId).data;
  const timeZone = useTimeZone();
  // Why a position cannot be closed or changed now, or null when it can.
  const closedNote = (symbol: string) => {
    const market = markets?.find((m) => m.symbol === symbol);
    return market && isClosed(market) ? `The market is closed. ${opensText(market, timeZone, timeZoneName(timeZone))}.` : null;
  };

  if (positions.length === 0) {
    return <Empty text="No open positions." />;
  }

  return (
    <Table headers={["Symbol", "Side", "Volume", "Open price", "Current price", "SL", "TP", "Margin", "Unrealized P/L", ""]}>
      {positions.map((p) => {
        const digits = digitsOf(p.symbol);
        const closed = closedNote(p.symbol);
        const limits = instruments?.find((i) => i.symbol === p.symbol);
        const part = limits ? defaultPart(p.volume, limits) : null;
        const breakEven = breakEvenStop(p);
        const done = () => setEditing(null);
        if (editing?.positionId === p.positionId && editing.what === "stops") {
          return <EditStops key={p.positionId} accountId={accountId} position={p} digits={digits} onDone={done} onError={onError} />;
        }

        if (editing?.positionId === p.positionId && limits && part !== null) {
          return <ClosePart key={p.positionId} accountId={accountId} position={p} limits={limits} start={part} onDone={done} onError={onError} />;
        }

        return (
          <tr key={p.positionId} className="border-t border-border transition-colors duration-150 hover:bg-raised/50">
            <SymbolCell symbol={p.symbol} title={`Position ${p.positionId}`} />
            <Cell>
              <SideLabel side={p.side} />
            </Cell>
            <Cell number>{formatVolume(p.volume)}</Cell>
            <Cell number>{formatPrice(p.openPrice, digits)}</Cell>
            <Cell number>{formatPrice(p.currentPrice, digits)}</Cell>
            <StopCell accountId={accountId} position={p} price={p.stopLoss} digits={digits} trailing={p.trailingDistance} />
            <StopCell accountId={accountId} position={p} price={p.takeProfit} digits={digits} />
            <Cell number>{formatMoney(p.margin)}</Cell>
            <Cell number className={p.profit >= 0 ? "text-profit" : "text-loss"}>
              {formatSignedMoney(p.profit)}
            </Cell>
            <Cell>
              <span className="flex justify-end gap-2">
                <ActionButton disabled={closed !== null} title={closed ?? undefined} onClick={() => setEditing({ positionId: p.positionId, what: "stops" })}>
                  Edit
                </ActionButton>
                <ActionButton
                  disabled={closed !== null || breakEven === null || modify.isPending}
                  title={
                    closed ??
                    (breakEven === null
                      ? "Possible once the price has moved past the open price, unless the stop loss already protects it."
                      : `Move the stop loss to the open price, ${formatPrice(breakEven, digits)}, so the position cannot lose before commission.`)
                  }
                  onClick={() =>
                    modify.mutate(
                      { positionId: p.positionId, stopLoss: breakEven, takeProfit: p.takeProfit, trailingStop: p.trailingDistance !== null },
                      { onError },
                    )
                  }
                >
                  Break even
                </ActionButton>
                <ActionButton
                  disabled={closed !== null || part === null}
                  title={closed ?? (part === null ? "The position is too small to close a part of it." : "Close part of the position and keep the rest open.")}
                  onClick={() => setEditing({ positionId: p.positionId, what: "part" })}
                >
                  Close part
                </ActionButton>
                <ActionButton
                  danger
                  disabled={close.isPending || closed !== null}
                  title={closed ?? undefined}
                  onClick={() => close.mutate({ positionId: p.positionId }, { onError })}
                >
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

// The stop's price and what the position would make or lose there, before commission. A trailing stop loss says so.
function StopCell({
  accountId,
  position,
  price,
  digits,
  trailing = null,
}: {
  accountId: string;
  position: PositionSnapshot;
  price: number | null;
  digits: number;
  trailing?: number | null;
}) {
  const pointValue = usePointValue(accountId, position.symbol).data;
  const amount = price !== null && pointValue ? estimatedProfit(position.side, position.volume, position.openPrice, price, digits, pointValue.perLot) : null;

  return (
    <Cell number>
      {trailing !== null && <TrailingTag distance={trailing} digits={digits} />}
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
  const [trailing, setTrailing] = useState(position.trailingDistance !== null);

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
          .join(", ")
      : [resolved.stopLoss, resolved.takeProfit]
          .map((price, i) => {
            const amount = amountAt(price);
            return amount === null ? null : `${i === 0 ? "SL" : "TP"} ${formatSignedMoney(amount)}`;
          })
          .filter(Boolean)
          .join(", ");

  const save = () => {
    if (!resolved.ok) {
      showError(resolved.error);
      return;
    }

    if (trailing && resolved.stopLoss === null) {
      showError("Set a stop loss for the trailing stop to follow.");
      return;
    }

    modify.mutate(
      { positionId: position.positionId, stopLoss: resolved.stopLoss, takeProfit: resolved.takeProfit, trailingStop: trailing },
      { onSuccess: onDone, onError },
    );
  };

  return (
    <tr className="border-t border-border bg-accent/5">
      <SymbolCell symbol={position.symbol} title={`Position ${position.positionId}`} />
      <td colSpan={9} className="px-3 py-1">
        <span className="flex items-center gap-2">
          <StopInput label="SL" value={fields[unit].stopLoss} onChange={(value) => setField("stopLoss", value)} />
          <StopInput label="TP" value={fields[unit].takeProfit} onChange={(value) => setField("takeProfit", value)} />
          <StopUnitToggle unit={unit} currency={pointValue?.currency ?? currency ?? "Money"} onChange={switchUnit} />
          <TrailingToggle on={trailing} onChange={setTrailing} />
          <button
            type="button"
            className="rounded-md bg-accent px-3 py-1 text-xs font-medium text-accent-foreground transition duration-150 hover:brightness-110 active:translate-y-px disabled:opacity-40"
            disabled={modify.isPending}
            onClick={save}
          >
            Save
          </button>
          <ActionButton onClick={onDone}>Cancel</ActionButton>
          {preview && <span className="font-mono text-xs text-muted tabular-nums">{preview}</span>}
        </span>
      </td>
    </tr>
  );
}

/** A part of the position to close, starting from half of it. The rest stays open with its stops. */
function ClosePart({
  accountId,
  position,
  limits,
  start,
  onDone,
  onError,
}: {
  accountId: string;
  position: PositionSnapshot;
  limits: InstrumentLimits;
  start: number;
  onDone: () => void;
  onError: (e: Error) => void;
}) {
  const close = useClosePosition(accountId);
  const [volume, setVolume] = useState(start.toFixed(2));

  const save = () => {
    const part = Number(volume.trim().replace(",", "."));
    const problem = partProblem(part, position.volume, limits);
    if (problem) {
      showError(problem);
      return;
    }

    close.mutate({ positionId: position.positionId, volume: part }, { onSuccess: onDone, onError });
  };

  return (
    <tr className="border-t border-border bg-accent/5">
      <SymbolCell symbol={position.symbol} title={`Position ${position.positionId}`} />
      <td colSpan={9} className="px-3 py-1">
        <span className="flex items-center gap-2">
          <StopInput label="Close" value={volume} onChange={setVolume} placeholder={start.toFixed(2)} />
          <span className="text-muted">of {formatVolume(position.volume)} lots</span>
          <button
            type="button"
            className="rounded-md bg-accent px-3 py-1 text-xs font-medium text-accent-foreground transition duration-150 hover:brightness-110 active:translate-y-px disabled:opacity-40"
            disabled={close.isPending}
            onClick={save}
          >
            Close part
          </button>
          <ActionButton onClick={onDone}>Cancel</ActionButton>
        </span>
      </td>
    </tr>
  );
}

// How long the confirmation of closing everything waits for the second click.
const confirmCloseAllMs = 4_000;

/** Closes every position at once. The first click asks, and a second click within a few seconds closes. */
function CloseAll({ accountId, count }: { accountId: string; count: number }) {
  const closeAll = useCloseAllPositions(accountId);
  const [asking, setAsking] = useState(false);

  useEffect(() => {
    if (!asking) {
      return;
    }

    const timer = setTimeout(() => setAsking(false), confirmCloseAllMs);
    return () => clearTimeout(timer);
  }, [asking]);

  const onClick = () => {
    if (!asking) {
      setAsking(true);
      return;
    }

    setAsking(false);
    closeAll.mutate(null, { onError });
  };

  return (
    <span className="ml-auto shrink-0 py-1.5">
      <ActionButton danger disabled={closeAll.isPending} onClick={onClick} title="Close every open position at once. Positions in a closed market stay open.">
        {asking ? `Close ${count === 1 ? "the position" : `all ${count}`}?` : "Close all"}
      </ActionButton>
    </span>
  );
}

/** A trailing stop loss: a small tag before the level, with its distance on hover. */
function TrailingTag({ distance, digits }: { distance: number; digits: number }) {
  return (
    <span
      title={`Trailing stop: the stop loss follows the price ${trailingPips(distance, digits)} pips behind it, and never moves back.`}
      className="mr-1.5 font-sans text-[10px] font-semibold tracking-wide text-accent uppercase"
    >
      Trail
    </span>
  );
}

function TrailingToggle({ on, onChange }: { on: boolean; onChange: (on: boolean) => void }) {
  return (
    <label className="flex items-center gap-1.5 text-xs text-muted" title="The stop loss follows the price at the distance it is set at, and never moves back.">
      <input type="checkbox" checked={on} onChange={(e) => onChange(e.target.checked)} className="accent-[var(--accent)]" />
      Trailing stop
    </label>
  );
}

function StopInput({ label, value, onChange, placeholder = "None" }: { label: string; value: string; onChange: (value: string) => void; placeholder?: string }) {
  return (
    <label className="flex items-center gap-1">
      <span className="text-muted">{label}</span>
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
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
  const markets = useMarketHours(accountId).data;
  const [editing, setEditing] = useState<string | null>(null);

  if (orders.length === 0) {
    return <Empty text="No pending orders." />;
  }

  return (
    <Table headers={["Symbol", "Type", "Side", "Volume", "Price", "SL", "TP", "Placed", ""]}>
      {orders.map((o) => {
        const digits = digitsOf(o.symbol);
        const market = markets?.find((m) => m.symbol === o.symbol);
        const closed = market && isClosed(market) ? `The market is closed. ${opensText(market, timeZone, timeZoneName(timeZone))}.` : null;
        if (editing === o.orderId) {
          return <EditOrder key={o.orderId} accountId={accountId} order={o} digits={digits} onDone={() => setEditing(null)} onError={onError} />;
        }

        return (
          <tr key={o.orderId} className="border-t border-border transition-colors duration-150 hover:bg-raised/50">
            <SymbolCell symbol={o.symbol} title={`Order ${o.orderId}`} />
            <Cell>{o.type}</Cell>
            <Cell>
              <SideLabel side={o.side} />
            </Cell>
            <Cell number>{formatVolume(o.volume)}</Cell>
            <Cell number>{formatPrice(o.price, digits)}</Cell>
            <Cell number>
              {o.trailingDistance !== null && <TrailingTag distance={o.trailingDistance} digits={digits} />}
              {formatPrice(o.stopLoss, digits)}
            </Cell>
            <Cell number>{formatPrice(o.takeProfit, digits)}</Cell>
            <Cell>{formatTime(o.placedTime, timeZone)}</Cell>
            <Cell>
              <span className="flex justify-end gap-2">
                <ActionButton disabled={closed !== null} title={closed ?? undefined} onClick={() => setEditing(o.orderId)}>
                  Edit
                </ActionButton>
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

/** A pending order's price, stop loss, take profit and trailing stop, as prices. */
function EditOrder({
  accountId,
  order,
  digits,
  onDone,
  onError,
}: {
  accountId: string;
  order: OrderSnapshot;
  digits: number;
  onDone: () => void;
  onError: (e: Error) => void;
}) {
  const modify = useModifyOrder(accountId);
  const [fields, setFields] = useState({
    price: order.price.toFixed(digits),
    stopLoss: order.stopLoss?.toFixed(digits) ?? "",
    takeProfit: order.takeProfit?.toFixed(digits) ?? "",
  });
  const [trailing, setTrailing] = useState(order.trailingDistance !== null);
  const set = (field: keyof typeof fields) => (value: string) => setFields((f) => ({ ...f, [field]: value }));

  const save = () => {
    const price = parsePrice(fields.price, digits);
    const stopLoss = parsePrice(fields.stopLoss, digits);
    const takeProfit = parsePrice(fields.takeProfit, digits);
    if (!price.ok || price.value === null || !stopLoss.ok || !takeProfit.ok) {
      showError(`Enter prices with at most ${digits} decimals.`);
      return;
    }

    if (trailing && stopLoss.value === null) {
      showError("Set a stop loss for the trailing stop to follow.");
      return;
    }

    modify.mutate(
      { orderId: order.orderId, price: price.value, stopLoss: stopLoss.value, takeProfit: takeProfit.value, trailingStop: trailing },
      { onSuccess: onDone, onError },
    );
  };

  return (
    <tr className="border-t border-border bg-accent/5">
      <SymbolCell symbol={order.symbol} title={`Order ${order.orderId}`} />
      <td colSpan={8} className="px-3 py-1">
        <span className="flex items-center gap-2">
          <StopInput label={`${order.type} price`} value={fields.price} onChange={set("price")} placeholder="" />
          <StopInput label="SL" value={fields.stopLoss} onChange={set("stopLoss")} />
          <StopInput label="TP" value={fields.takeProfit} onChange={set("takeProfit")} />
          <TrailingToggle on={trailing} onChange={setTrailing} />
          <button
            type="button"
            className="rounded-md bg-accent px-3 py-1 text-xs font-medium text-accent-foreground transition duration-150 hover:brightness-110 active:translate-y-px disabled:opacity-40"
            disabled={modify.isPending}
            onClick={save}
          >
            Save
          </button>
          <ActionButton onClick={onDone}>Cancel</ActionButton>
        </span>
      </td>
    </tr>
  );
}

// Closed positions and balance operations such as payouts, newest first. Commission is for both opening and closing, and
// the result is after it, as in the firm's portal.
function History({ events, digitsOf }: { events: EngineEvent[]; digitsOf: DigitsOf }) {
  const timeZone = useTimeZone();
  const openSheet = useSheet((s) => s.open);
  const details = (positionId: string) => (
    <td className="px-3 py-1 text-right">
      <ActionButton onClick={() => openSheet({ kind: "details", positionId })} title="The prices behind this trade">
        Details
      </ActionButton>
    </td>
  );
  const rows = events
    .filter((e): e is HistoryRow => e.kind === "PositionClosed" || e.kind === "PositionPartiallyClosed" || e.kind === "BalanceAdjusted")
    .reverse();

  if (rows.length === 0) {
    return <Empty text="No closed positions yet." />;
  }

  return (
    <Table headers={["Closed", "Symbol", "Side", "Volume", "Open price", "Close price", "Reason", "Commission", "Result", ""]}>
      {rows.map((c) => {
        if (isBalanceOperation(c)) {
          return (
            <tr key={`${c.operationId}-${c.timestamp}`} className="border-t border-border transition-colors duration-150 hover:bg-raised/50">
              <Cell number>{formatTime(c.timestamp, timeZone)}</Cell>
              <td colSpan={7} className="px-3 py-1.5 text-muted">
                {balanceOperationName(c.amount)}
              </td>
              <Cell number className={c.amount >= 0 ? "text-profit" : "text-loss"}>
                {formatSignedMoney(c.amount)}
              </Cell>
              <td />
            </tr>
          );
        }

        const digits = digitsOf(c.symbol);
        if (isPart(c)) {
          const partOutcome = partResult(c);
          return (
            <tr key={`${c.positionId}-${c.timestamp}`} className="border-t border-border transition-colors duration-150 hover:bg-raised/50">
              <Cell number>{formatTime(c.timestamp, timeZone)}</Cell>
              <SymbolCell symbol={c.symbol} title={`Position ${c.positionId}`} />
              <Cell>
                <SideLabel side={c.side} />
              </Cell>
              <Cell number>{formatVolume(c.volume)}</Cell>
              <Cell number>{formatPrice(c.openPrice, digits)}</Cell>
              <Cell number>{formatPrice(c.closePrice, digits)}</Cell>
              <Cell>
                <span title={`${formatVolume(c.remainingVolume)} lots stayed open`}>Part closed</span>
              </Cell>
              <Cell number>{formatMoney(c.commission)}</Cell>
              <Cell number className={partOutcome >= 0 ? "text-profit" : "text-loss"}>
                <span title={`${formatSignedMoney(c.profit)} before ${formatMoney(c.commission)} commission for closing the part`}>
                  {formatSignedMoney(partOutcome)}
                </span>
              </Cell>
              {details(c.positionId)}
            </tr>
          );
        }

        const commission = positionCommission(c, events);
        const result = netResult(c, events);
        return (
          <tr key={`${c.positionId}-${c.timestamp}`} className="border-t border-border transition-colors duration-150 hover:bg-raised/50">
            <Cell number>{formatTime(c.timestamp, timeZone)}</Cell>
            <SymbolCell symbol={c.symbol} title={`Position ${c.positionId}`} />
            <Cell>
              <SideLabel side={c.side} />
            </Cell>
            <Cell number>{formatVolume(c.volume)}</Cell>
            <Cell number>{formatPrice(c.openPrice, digits)}</Cell>
            <Cell number>{formatPrice(c.closePrice, digits)}</Cell>
            <Cell>{closeReasons[c.reason]}</Cell>
            <Cell number>{formatMoney(commission)}</Cell>
            <Cell number className={result >= 0 ? "text-profit" : "text-loss"}>
              <span title={`${formatSignedMoney(c.profit)} before ${formatMoney(commission)} commission`}>{formatSignedMoney(result)}</span>
            </Cell>
            {details(c.positionId)}
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

// The id of a position or order means nothing to a trader, so it is only there when the mouse rests on the symbol.
function SymbolCell({ symbol, title }: { symbol: string; title: string }) {
  return (
    <td className="px-3 py-1.5 font-medium whitespace-nowrap" title={title}>
      {symbol}
    </td>
  );
}

function SideLabel({ side }: { side: Side }) {
  return <span className={`font-medium ${side === "Buy" ? "text-buy" : "text-sell"}`}>{side}</span>;
}

function ActionButton({
  children,
  onClick,
  disabled,
  title,
  danger = false,
}: {
  children: React.ReactNode;
  onClick: () => void;
  disabled?: boolean;
  title?: string;
  danger?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      title={title}
      className={`rounded-md border border-border px-3 py-1 text-xs font-medium text-muted transition duration-150 hover:bg-raised hover:text-foreground active:translate-y-px disabled:opacity-40 ${danger ? "hover:border-loss/50 hover:text-loss" : "hover:border-muted"}`}
    >
      {children}
    </button>
  );
}

function Empty({ text }: { text: string }) {
  return <p className="px-3 py-6 text-center text-muted">{text}</p>;
}
