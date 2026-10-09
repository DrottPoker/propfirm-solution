"use client";

import { useCallback, useEffect, useState } from "react";

import { CommandRejectedError } from "@/lib/api/client";
import type { EngineEvent, InstrumentLimits, OrderSnapshot, PositionSnapshot, Side } from "@/lib/api/types";
import { useAlerts } from "@/lib/alerts";
import {
  closeReasons,
  collapseRepeats,
  describeEvent,
  isWarning,
  positionCommission,
  rejectionText,
  topicOf,
  withoutRepeats,
  type DigitsOf,
  type EventTopic,
} from "@/lib/events";
import { byDay, formatMoney, formatPrice, formatSignedMoney, formatTime, formatVolume, formatWhen, timeZoneName } from "@/lib/format";
import {
  historyCsv,
  historyRanges,
  historyRows,
  historySummary,
  inRange,
  isBalanceOperation,
  isPart,
  rowResult,
  type HistoryRange,
  type HistoryRow,
} from "@/lib/history";
import { useLayout } from "@/lib/layout";
import { isClosed, opensText, useClock } from "@/lib/marketHours";
import { newEvents } from "@/lib/notices";
import { ghostLines, useOrderDraft } from "@/lib/orderDraft";
import { parsePrice } from "@/lib/orderInput";
import { breakEvenStop, defaultPart, partProblem, trailingPips } from "@/lib/orderTools";
import { wordsFor } from "@/lib/profile";
import { useProfile } from "@/lib/profileContext";
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
import { useSettings } from "@/lib/settings";
import { useSheet } from "@/lib/sheet";
import { estimatedProfit, resolveStops, type StopKind, type StopUnit } from "@/lib/stops";
import { useTradingStore } from "@/lib/store";
import { distanceUnit } from "@/lib/ticket";
import { useTimeZone } from "@/lib/timeZone";

import { AlertsList } from "./AlertsList";
import { ChevronDownIcon, DownloadIcon, MoreIcon } from "./icons";
import { Menu } from "./Menu";
import { Popover } from "./Popover";
import { StopUnitToggle } from "./StopUnitToggle";
import { showError } from "./TradeNotices";

const tabs = ["Positions", "Orders", "History", "Events", "Alerts"] as const;
type Tab = (typeof tabs)[number];

// A refusal comes and goes in a corner like a fill, so it is seen wherever the trader looks.
const onError = (e: Error) => showError(e instanceof CommandRejectedError ? rejectionText(e.reason) : "Could not reach the trading service.");

// How long a close waits for its second press when closes ask first, and closing everything always does.
const confirmCloseMs = 4_000;

export function BottomPanel({ accountId, digitsOf }: { accountId: string; digitsOf: DigitsOf }) {
  const [tab, setTab] = useState<Tab>("Positions");
  const account = useTradingStore((s) => s.account);
  const events = useTradingStore((s) => s.events);
  const alertCount = useAlerts((s) => s.alerts.length);
  // Folded down to its tabs on a computer, so the chart gets the room (ADR 0058). A phone shows it whole.
  const open = useLayout((s) => s.bottomOpen);
  const setOpen = useLayout((s) => s.setOpen);
  useOpenOnNewTrade(setTab);

  const counts: Record<Tab, number | null> = {
    Positions: account?.positions.length ?? 0,
    Orders: account?.orders.length ?? 0,
    History: null,
    Events: null,
    Alerts: alertCount,
  };

  // A press on another tab opens it, and on the open tab folds the panel, as in many editors.
  const choose = (t: Tab) => {
    if (t === tab && open) {
      setOpen("bottom", false);
    } else {
      setTab(t);
      if (!open) {
        setOpen("bottom", true);
      }
    }
  };

  return (
    <section className="flex min-h-0 flex-col overflow-hidden bg-panel text-sm">
      <nav className="flex h-9 shrink-0 items-center gap-1 overflow-x-auto border-b border-border px-2" role="tablist" aria-label="Positions and history">
        {tabs.map((t) => (
          <button
            key={t}
            type="button"
            role="tab"
            aria-selected={t === tab}
            onClick={() => choose(t)}
            className={`-mb-px h-full border-b-2 px-3 font-medium whitespace-nowrap transition-colors duration-150 ${t === tab && open ? "border-accent text-foreground" : t === tab ? "border-transparent text-foreground" : "border-transparent text-muted hover:text-foreground"}`}
          >
            {t}
            {counts[t] ? <span className="ml-1.5 text-muted tabular-nums">{counts[t]}</span> : null}
          </button>
        ))}
        <span className="ml-auto flex shrink-0 items-center gap-1">
          {tab === "Positions" && open && (account?.positions.length ?? 0) > 0 && <CloseAll accountId={accountId} count={account?.positions.length ?? 0} />}
          <button
            type="button"
            onClick={() => setOpen("bottom", !open)}
            aria-expanded={open}
            aria-label={open ? "Fold the panel down" : "Open the panel"}
            title={open ? "Fold the panel down, so the chart gets the room" : "Open the panel"}
            className="hidden size-7 items-center justify-center rounded-md text-muted transition-colors duration-150 hover:bg-raised hover:text-foreground lg:flex"
          >
            <ChevronDownIcon className={`size-3.5 transition-transform duration-150 ${open ? "" : "rotate-180"}`} />
          </button>
        </span>
      </nav>
      <div className={`flex min-h-0 flex-1 flex-col ${open ? "" : "lg:hidden"}`} role="tabpanel" aria-label={tab}>
        {tab === "Positions" && <Positions accountId={accountId} positions={account?.positions ?? []} digitsOf={digitsOf} />}
        {tab === "Orders" && <Orders accountId={accountId} digitsOf={digitsOf} />}
        {tab === "History" && <History accountId={accountId} events={events.map((e) => e.event)} digitsOf={digitsOf} />}
        {tab === "Events" && <Events events={withoutRepeats(events.map((e) => e.event))} digitsOf={digitsOf} />}
        {tab === "Alerts" && (
          <div className="min-h-0 flex-1 overflow-y-auto">
            <AlertsList digitsOf={digitsOf} />
          </div>
        )}
      </div>
    </section>
  );
}

/**
 * Opens the folded panel on the trader's own new position or pending order, at its tab, so a trade never goes out of
 * sight. Positions the engine opens from a pending order count too, since the trader placed it.
 */
function useOpenOnNewTrade(setTab: (tab: Tab) => void) {
  useEffect(
    () =>
      newEvents.subscribe((envelopes) => {
        const kinds = envelopes.map((e) => e.event.kind);
        const tab = kinds.includes("PositionOpened") ? "Positions" : kinds.includes("OrderPlaced") ? "Orders" : null;
        const layout = useLayout.getState();
        if (tab && !layout.bottomOpen) {
          setTab(tab);
          layout.setOpen("bottom", true);
        }
      }),
    [setTab],
  );
}

/** Why the symbol's positions and orders cannot be changed now, or null when they can. */
function useClosedNote(accountId: string): (symbol: string) => string | null {
  const markets = useMarketHours(accountId).data;
  const timeZone = useTimeZone();
  return (symbol) => {
    const market = markets?.find((m) => m.symbol === symbol);
    return market && isClosed(market) ? `The market is closed. ${opensText(market, timeZone, timeZoneName(timeZone))}.` : null;
  };
}

// ---------------------------------------------------------------------------------------------------------------------
// Positions

/**
 * The open positions with when they opened, their stops and what each would make there, the commission paid on open
 * and the result now, with the totals under them. Close is a button of its own on each row, and the rest is in a menu
 * beside it, so a position is never closed by a press meant for something else (ADR 0058).
 */
function Positions({ accountId, positions, digitsOf }: { accountId: string; positions: PositionSnapshot[]; digitsOf: DigitsOf }) {
  const events = useTradingStore((s) => s.events);
  const currency = useTradingStore((s) => s.account?.currency) ?? "";
  if (positions.length === 0) {
    return <Empty text="No open positions." />;
  }

  // The commission on open comes from the position's opened event, unless that is older than the events at hand.
  const commissions = new Map<string, number>();
  for (const { event } of events) {
    if (event.kind === "PositionOpened") {
      commissions.set(event.positionId, event.commission);
    }
  }

  const known = positions.every((p) => commissions.has(p.positionId));
  const totalCommission = positions.reduce((sum, p) => sum + (commissions.get(p.positionId) ?? 0), 0);
  const totalProfit = positions.reduce((sum, p) => sum + p.profit, 0);

  return (
    <>
      {/* A phone shows a card for each position, with the result large and Close on it (ADR 0058). */}
      <ul aria-label="Open positions" className="min-h-0 flex-1 overflow-y-auto lg:hidden">
        {positions.map((p) => (
          <PositionRow
            key={p.positionId}
            accountId={accountId}
            position={p}
            digits={digitsOf(p.symbol)}
            commission={commissions.get(p.positionId) ?? null}
            card
          />
        ))}
        <li className="flex items-baseline justify-between gap-3 px-3 py-2.5 text-sm">
          <span className="text-muted">{positions.length === 1 ? "1 position" : `${positions.length} positions`}</span>
          <span className={`live font-semibold tabular-nums ${totalProfit >= 0 ? "text-profit" : "text-loss"}`}>
            {formatSignedMoney(totalProfit)} <span className="text-[11px] font-normal text-muted">{currency}</span>
          </span>
        </li>
      </ul>
      <div className="flex min-h-0 flex-1 flex-col max-lg:hidden">
        <Table
          columns={[
            { name: "Symbol" },
            { name: "Side" },
            { name: "Volume", number: true },
            { name: "Opened" },
            { name: "Open price", number: true },
            { name: "Price", number: true },
            { name: "Stop loss", number: true },
            { name: "Take profit", number: true },
            { name: "Commission", number: true },
            { name: "Result", number: true },
            { name: "", label: "Actions" },
          ]}
          footer={
            <tr className="border-t border-border">
              <td colSpan={8} className="px-3 py-1.5 text-xs text-muted">
                {positions.length === 1 ? "1 position" : `${positions.length} positions`}
              </td>
              <td className="px-3 py-1.5 text-right text-muted tabular-nums" title={known ? "Paid on open" : "Some positions opened before the events at hand"}>
                {known ? formatMoney(totalCommission) : "-"}
              </td>
              <td
                className={`live px-3 py-1.5 text-right font-semibold tabular-nums ${totalProfit >= 0 ? "text-profit" : "text-loss"}`}
                title="The result of every open position now, before commission"
              >
                {formatSignedMoney(totalProfit)} <span className="text-[11px] font-normal text-muted">{currency}</span>
              </td>
              <td />
            </tr>
          }
        >
          {positions.map((p) => (
            <PositionRow key={p.positionId} accountId={accountId} position={p} digits={digitsOf(p.symbol)} commission={commissions.get(p.positionId) ?? null} />
          ))}
        </Table>
      </div>
    </>
  );
}

function PositionRow({
  accountId,
  position: p,
  digits,
  commission,
  card = false,
}: {
  accountId: string;
  position: PositionSnapshot;
  digits: number;
  commission: number | null;
  /** A card on a phone, where the table's columns do not fit (ADR 0058). */
  card?: boolean;
}) {
  const close = useClosePosition(accountId);
  const modify = useModifyStops(accountId);
  const limits = useInstruments(accountId).data?.find((i) => i.symbol === p.symbol);
  const closed = useClosedNote(accountId)(p.symbol);
  const timeZone = useTimeZone();
  const now = useClock();
  const [editing, setEditing] = useState<"stops" | "part" | null>(null);
  const done = useCallback(() => setEditing(null), [setEditing]);
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const part = limits ? defaultPart(p.volume, limits) : null;
  const breakEven = breakEvenStop(p);

  const breakEvenHint =
    closed ??
    (breakEven === null
      ? "Once the price has moved past the open price, unless the stop loss already protects it."
      : `The stop loss to the open price, ${formatPrice(breakEven, digits)}, so the position cannot lose before commission.`);

  const actions = (
    <>
      <span ref={setAnchor} className="flex justify-end gap-1">
        <CloseButton
          disabled={close.isPending || closed !== null}
          title={closed ?? `Close ${p.side} ${formatVolume(p.volume)} ${p.symbol} at the market`}
          onClose={() => close.mutate({ positionId: p.positionId }, { onError })}
        >
          Close
        </CloseButton>
        <Menu
          label={`More for ${p.side} ${formatVolume(p.volume)} ${p.symbol}`}
          buttonLabel={`More for ${p.side} ${formatVolume(p.volume)} ${p.symbol}`}
          title="Stops, break even and closing a part"
          button={<MoreIcon className="size-4" />}
          align="end"
          side="above"
          items={[
            { value: "stops", label: "Stop loss and take profit", hint: closed ?? undefined, disabled: closed !== null, action: true },
            { value: "breakEven", label: "Break even", hint: breakEvenHint, disabled: closed !== null || breakEven === null || modify.isPending, action: true },
            {
              value: "part",
              label: "Close part",
              hint: closed ?? (part === null ? "The position is too small to close a part of it." : undefined),
              disabled: closed !== null || part === null,
              action: true,
            },
          ]}
          onChoose={(choice) => {
            if (choice === "breakEven") {
              modify.mutate(
                { positionId: p.positionId, stopLoss: breakEven, takeProfit: p.takeProfit, trailingStop: p.trailingDistance !== null },
                { onError },
              );
            } else {
              setEditing(choice);
            }
          }}
          className="flex size-7 items-center justify-center rounded-md text-muted transition-colors duration-150 hover:bg-raised hover:text-foreground"
        />
      </span>
      {editing === "stops" && <EditStops accountId={accountId} position={p} digits={digits} anchor={anchor} onDone={done} />}
      {editing === "part" && limits && part !== null && (
        <ClosePart accountId={accountId} position={p} limits={limits} start={part} anchor={anchor} onDone={done} />
      )}
    </>
  );

  if (card) {
    return (
      <li className={`flex flex-col gap-2 border-b border-border px-3 py-3 ${editing ? "bg-accent/5" : ""}`}>
        <div className="flex items-start justify-between gap-3">
          <div className="flex min-w-0 flex-col">
            <span className="font-semibold">{p.symbol}</span>
            <span className="text-xs text-muted">
              <SideLabel side={p.side} /> {formatVolume(p.volume)} at {formatPrice(p.openPrice, digits)}, {formatWhen(p.openTime, timeZone, now)}
            </span>
          </div>
          <span className={`live text-lg font-semibold tabular-nums ${p.profit >= 0 ? "text-profit" : "text-loss"}`}>{formatSignedMoney(p.profit)}</span>
        </div>
        <div className="flex items-end justify-between gap-3">
          <dl className="grid grid-cols-[auto_auto] gap-x-2 gap-y-0.5 text-xs">
            <dt className="text-muted">Price</dt>
            <dd className="live tabular-nums">{formatPrice(p.currentPrice, digits)}</dd>
            <dt className="text-muted">Stop loss</dt>
            <dd className="tabular-nums">{p.stopLoss === null ? "-" : formatPrice(p.stopLoss, digits)}</dd>
            <dt className="text-muted">Take profit</dt>
            <dd className="tabular-nums">{p.takeProfit === null ? "-" : formatPrice(p.takeProfit, digits)}</dd>
          </dl>
          {actions}
        </div>
      </li>
    );
  }

  return (
    <tr className={`border-t border-border transition-colors duration-150 hover:bg-raised/50 ${editing ? "bg-accent/5" : ""}`}>
      <SymbolCell symbol={p.symbol} />
      <Cell>
        <SideLabel side={p.side} />
      </Cell>
      <Cell number>{formatVolume(p.volume)}</Cell>
      <Cell className="text-muted" title={formatWhen(p.openTime, timeZone, now, true)}>
        {formatWhen(p.openTime, timeZone, now)}
      </Cell>
      <Cell number>{formatPrice(p.openPrice, digits)}</Cell>
      <Cell number className="live">
        {formatPrice(p.currentPrice, digits)}
      </Cell>
      <StopCell accountId={accountId} position={p} price={p.stopLoss} digits={digits} trailing={p.trailingDistance} />
      <StopCell accountId={accountId} position={p} price={p.takeProfit} digits={digits} />
      <Cell number className="text-muted">
        {commission === null ? "-" : formatMoney(commission)}
      </Cell>
      <Cell number className={`live font-medium ${p.profit >= 0 ? "text-profit" : "text-loss"}`}>
        {formatSignedMoney(p.profit)}
      </Cell>
      <td className="px-3 py-1">{actions}</td>
    </tr>
  );
}

/** Closes at once, or on a second press within a few seconds when the trader chose that closes ask first (ADR 0058). */
function CloseButton({ disabled, title, onClose, children }: { disabled: boolean; title: string; onClose: () => void; children: React.ReactNode }) {
  const asks = useSettings((s) => s.confirmCloses);
  const [asking, setAsking] = useState(false);
  useEffect(() => {
    if (!asking) {
      return;
    }

    const timer = setTimeout(() => setAsking(false), confirmCloseMs);
    return () => clearTimeout(timer);
  }, [asking]);

  return (
    <ActionButton
      danger
      active={asking}
      disabled={disabled}
      title={asking ? "Press again to close" : title}
      onClick={() => {
        if (asks && !asking) {
          setAsking(true);
          return;
        }

        setAsking(false);
        onClose();
      }}
    >
      {asking ? "Close?" : children}
    </ActionButton>
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

  if (price === null) {
    return (
      <Cell number className="text-muted">
        -
      </Cell>
    );
  }

  return (
    <Cell number>
      {trailing !== null && <TrailingTag distance={trailing} digits={digits} />}
      {formatPrice(price, digits)}
      {amount !== null && (
        <span className={`ml-1.5 text-xs ${amount >= 0 ? "text-profit/80" : "text-loss/80"}`} title="Estimated result at this level, before commission">
          {formatSignedMoney(amount)}
        </span>
      )}
    </Cell>
  );
}

/**
 * A position's stop loss and take profit in a box by its row, as prices, pips from the open price or amounts, with
 * what the position makes or loses at each. The chart shows the new levels faintly while the box is open (ADR 0058).
 */
function EditStops({
  accountId,
  position,
  digits,
  anchor,
  onDone,
}: {
  accountId: string;
  position: PositionSnapshot;
  digits: number;
  anchor: HTMLElement | null;
  onDone: () => void;
}) {
  const modify = useModifyStops(accountId);
  const pointValue = usePointValue(accountId, position.symbol).data ?? undefined;
  const currency = useTradingStore((s) => s.account?.currency) ?? "";
  const instrument = useInstruments(accountId).data?.find((i) => i.symbol === position.symbol);
  const [unit, setUnit] = useState<StopUnit>("price");
  const [fields, setFields] = useState<Record<StopUnit, Record<StopKind, string>>>({
    price: { stopLoss: position.stopLoss?.toFixed(digits) ?? "", takeProfit: position.takeProfit?.toFixed(digits) ?? "" },
    money: { stopLoss: "", takeProfit: "" },
    pips: { stopLoss: "", takeProfit: "" },
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
        money: { stopLoss: loss !== null && loss < 0 ? (-loss).toFixed(2) : "", takeProfit: win !== null && win > 0 ? win.toFixed(2) : "" },
      }));
    }
    setUnit(next);
  };

  const setField = (kind: StopKind, value: string) => setFields((f) => ({ ...f, [unit]: { ...f[unit], [kind]: value } }));
  // Pips and amounts are counted from the open price.
  const resolved = resolveStops(
    unit,
    fields[unit].stopLoss,
    fields[unit].takeProfit,
    position.side,
    position.openPrice,
    position.volume,
    digits,
    pointValue?.perLot,
  );
  const ghostKey = JSON.stringify(
    ghostLines({
      type: "Market",
      side: position.side,
      entry: position.openPrice,
      stops: resolved,
      lots: position.volume,
      digits,
      pointValuePerLot: pointValue?.perLot,
    }),
  );

  useEffect(() => {
    useOrderDraft.getState().setEdit({ symbol: position.symbol, lines: JSON.parse(ghostKey) });
  }, [position.symbol, ghostKey]);
  useEffect(() => () => useOrderDraft.getState().setEdit(null), []);

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

  const results = resolved.ok
    ? [
        { name: "stop loss", amount: amountAt(resolved.stopLoss), price: resolved.stopLoss },
        { name: "take profit", amount: amountAt(resolved.takeProfit), price: resolved.takeProfit },
      ].filter((r) => r.price !== null)
    : [];

  return (
    <Popover anchor={anchor} label={`Stops of ${position.side} ${formatVolume(position.volume)} ${position.symbol}`} onClose={onDone} className="w-[22rem]">
      <form
        className="flex flex-col gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          save();
        }}
      >
        <div className="flex items-center justify-between gap-3">
          <p className="font-medium">
            <SideLabel side={position.side} /> {formatVolume(position.volume)} {position.symbol}
          </p>
          <StopUnitToggle
            unit={unit}
            currency={pointValue?.currency ?? currency}
            distance={instrument ? distanceUnit(instrument) : "pips"}
            onChange={switchUnit}
          />
        </div>
        <div className="grid grid-cols-2 gap-2">
          <Field label="Stop loss" value={fields[unit].stopLoss} onChange={(value) => setField("stopLoss", value)} />
          <Field label="Take profit" value={fields[unit].takeProfit} onChange={(value) => setField("takeProfit", value)} />
        </div>
        <p className={`text-xs ${resolved.ok ? "text-muted" : "text-warning"}`}>
          {!resolved.ok
            ? resolved.error
            : results.length === 0
              ? "No stops: the position stays open until it is closed."
              : results
                  .map(
                    (r) =>
                      `At the ${r.name}${unit === "price" ? "" : ` (${formatPrice(r.price, digits)})`}${r.amount === null ? "" : `: ${formatSignedMoney(r.amount)} ${currency}`}`,
                  )
                  .join(". ")}
        </p>
        <TrailingToggle on={trailing} onChange={setTrailing} />
        <div className="flex justify-end gap-2">
          <ActionButton onClick={onDone}>Cancel</ActionButton>
          <SubmitButton disabled={modify.isPending}>Save stops</SubmitButton>
        </div>
      </form>
    </Popover>
  );
}

/** A part of the position to close in a box by its row, starting from half of it. The rest stays open with its stops. */
function ClosePart({
  accountId,
  position,
  limits,
  start,
  anchor,
  onDone,
}: {
  accountId: string;
  position: PositionSnapshot;
  limits: InstrumentLimits;
  start: number;
  anchor: HTMLElement | null;
  onDone: () => void;
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
    <Popover anchor={anchor} label={`Close part of ${position.side} ${formatVolume(position.volume)} ${position.symbol}`} onClose={onDone} className="w-72">
      <form
        className="flex flex-col gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          save();
        }}
      >
        <p className="font-medium">
          Close part of <SideLabel side={position.side} /> {formatVolume(position.volume)} {position.symbol}
        </p>
        <Field label={`Lots to close, of ${formatVolume(position.volume)}`} value={volume} onChange={setVolume} placeholder={start.toFixed(2)} />
        <p className="text-xs text-muted">The rest stays open with its stop loss and take profit.</p>
        <div className="flex justify-end gap-2">
          <ActionButton onClick={onDone}>Cancel</ActionButton>
          <SubmitButton disabled={close.isPending} danger>
            Close part
          </SubmitButton>
        </div>
      </form>
    </Popover>
  );
}

/** Closes every position at once. The first click asks, and a second click within a few seconds closes. */
function CloseAll({ accountId, count }: { accountId: string; count: number }) {
  const closeAll = useCloseAllPositions(accountId);
  const [asking, setAsking] = useState(false);

  useEffect(() => {
    if (!asking) {
      return;
    }

    const timer = setTimeout(() => setAsking(false), confirmCloseMs);
    return () => clearTimeout(timer);
  }, [asking]);

  return (
    <ActionButton
      danger
      active={asking}
      disabled={closeAll.isPending}
      title="Close every open position at once. Positions in a closed market stay open."
      onClick={() => {
        if (!asking) {
          setAsking(true);
          return;
        }

        setAsking(false);
        closeAll.mutate(null, { onError });
      }}
    >
      {asking ? `Close ${count === 1 ? "the position" : `all ${count}`}?` : "Close all"}
    </ActionButton>
  );
}

/** A trailing stop loss: a small word before the level, with its distance on hover. */
function TrailingTag({ distance, digits }: { distance: number; digits: number }) {
  return (
    <span
      title={`Trailing stop: the stop loss follows the price ${trailingPips(distance, digits)} pips behind it, and never moves back.`}
      className="mr-1.5 text-[11px] font-medium text-accent"
    >
      Trailing
    </span>
  );
}

function TrailingToggle({ on, onChange }: { on: boolean; onChange: (on: boolean) => void }) {
  return (
    <label
      className="flex w-fit items-center gap-2 text-xs text-muted"
      title="The stop loss follows the price at the distance it is set at, and never moves back."
    >
      <input type="checkbox" checked={on} onChange={(e) => onChange(e.target.checked)} className="accent-[var(--accent)]" />
      Trailing stop
    </label>
  );
}

function Field({ label, value, onChange, placeholder = "None" }: { label: string; value: string; onChange: (value: string) => void; placeholder?: string }) {
  return (
    <label className="flex flex-col gap-1 text-xs">
      <span className="text-muted">{label}</span>
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        inputMode="decimal"
        className="h-8 rounded-md border border-border bg-background px-2 text-sm tabular-nums outline-none transition-colors duration-150 focus:border-accent"
      />
    </label>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// Orders

function Orders({ accountId, digitsOf }: { accountId: string; digitsOf: DigitsOf }) {
  const orders = useTradingStore((s) => s.account?.orders ?? []);

  if (orders.length === 0) {
    return <Empty text="No pending orders." />;
  }

  return (
    <>
      <ul aria-label="Pending orders" className="min-h-0 flex-1 overflow-y-auto lg:hidden">
        {orders.map((o) => (
          <OrderRow key={o.orderId} accountId={accountId} order={o} digits={digitsOf(o.symbol)} card />
        ))}
      </ul>
      <div className="flex min-h-0 flex-1 flex-col max-lg:hidden">
        <Table
          columns={[
            { name: "Symbol" },
            { name: "Order" },
            { name: "Volume", number: true },
            { name: "Price", number: true },
            { name: "Stop loss", number: true },
            { name: "Take profit", number: true },
            { name: "Placed" },
            { name: "", label: "Actions" },
          ]}
        >
          {orders.map((o) => (
            <OrderRow key={o.orderId} accountId={accountId} order={o} digits={digitsOf(o.symbol)} />
          ))}
        </Table>
      </div>
    </>
  );
}

function OrderRow({ accountId, order: o, digits, card = false }: { accountId: string; order: OrderSnapshot; digits: number; card?: boolean }) {
  const cancel = useCancelOrder(accountId);
  const closed = useClosedNote(accountId)(o.symbol);
  const timeZone = useTimeZone();
  const now = useClock();
  const [editing, setEditing] = useState(false);
  const done = useCallback(() => setEditing(false), [setEditing]);
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);

  const actions = (
    <>
      <span ref={setAnchor} className="flex justify-end gap-1">
        <ActionButton disabled={closed !== null} title={closed ?? "The order's price and stops"} onClick={() => setEditing(true)}>
          Edit
        </ActionButton>
        <ActionButton
          danger
          disabled={cancel.isPending}
          title={`Cancel the ${o.side.toLowerCase()} ${o.type.toLowerCase()} order`}
          onClick={() => cancel.mutate(o.orderId, { onError })}
        >
          Cancel order
        </ActionButton>
      </span>
      {editing && <EditOrder accountId={accountId} order={o} digits={digits} anchor={anchor} onDone={done} />}
    </>
  );

  if (card) {
    return (
      <li className={`flex flex-col gap-2 border-b border-border px-3 py-3 ${editing ? "bg-accent/5" : ""}`}>
        <div className="flex items-start justify-between gap-3">
          <div className="flex min-w-0 flex-col">
            <span className="font-semibold">{o.symbol}</span>
            <span className="text-xs text-muted">
              <SideLabel side={o.side} /> {o.type.toLowerCase()} {formatVolume(o.volume)}, placed {formatWhen(o.placedTime, timeZone, now)}
            </span>
          </div>
          <span className="text-lg font-semibold tabular-nums">{formatPrice(o.price, digits)}</span>
        </div>
        <div className="flex items-end justify-between gap-3">
          <dl className="grid grid-cols-[auto_auto] gap-x-2 gap-y-0.5 text-xs">
            <dt className="text-muted">Stop loss</dt>
            <dd className="tabular-nums">{o.stopLoss === null ? "-" : formatPrice(o.stopLoss, digits)}</dd>
            <dt className="text-muted">Take profit</dt>
            <dd className="tabular-nums">{o.takeProfit === null ? "-" : formatPrice(o.takeProfit, digits)}</dd>
          </dl>
          {actions}
        </div>
      </li>
    );
  }

  return (
    <tr className={`border-t border-border transition-colors duration-150 hover:bg-raised/50 ${editing ? "bg-accent/5" : ""}`}>
      <SymbolCell symbol={o.symbol} />
      <Cell>
        <SideLabel side={o.side} /> <span className="text-muted">{o.type.toLowerCase()}</span>
      </Cell>
      <Cell number>{formatVolume(o.volume)}</Cell>
      <Cell number>{formatPrice(o.price, digits)}</Cell>
      <Cell number className={o.stopLoss === null ? "text-muted" : ""}>
        {o.trailingDistance !== null && <TrailingTag distance={o.trailingDistance} digits={digits} />}
        {o.stopLoss === null ? "-" : formatPrice(o.stopLoss, digits)}
      </Cell>
      <Cell number className={o.takeProfit === null ? "text-muted" : ""}>
        {o.takeProfit === null ? "-" : formatPrice(o.takeProfit, digits)}
      </Cell>
      <Cell className="text-muted" title={formatWhen(o.placedTime, timeZone, now, true)}>
        {formatWhen(o.placedTime, timeZone, now)}
      </Cell>
      <td className="px-3 py-1">{actions}</td>
    </tr>
  );
}

/** A pending order's price, stop loss, take profit and trailing stop, as prices, in a box by its row. */
function EditOrder({
  accountId,
  order,
  digits,
  anchor,
  onDone,
}: {
  accountId: string;
  order: OrderSnapshot;
  digits: number;
  anchor: HTMLElement | null;
  onDone: () => void;
}) {
  const modify = useModifyOrder(accountId);
  const pointValue = usePointValue(accountId, order.symbol).data ?? undefined;
  const [fields, setFields] = useState({
    price: order.price.toFixed(digits),
    stopLoss: order.stopLoss?.toFixed(digits) ?? "",
    takeProfit: order.takeProfit?.toFixed(digits) ?? "",
  });
  const [trailing, setTrailing] = useState(order.trailingDistance !== null);
  const set = (field: keyof typeof fields) => (value: string) => setFields((f) => ({ ...f, [field]: value }));
  const price = parsePrice(fields.price, digits);
  const stopLoss = parsePrice(fields.stopLoss, digits);
  const takeProfit = parsePrice(fields.takeProfit, digits);
  const entry = price.ok ? (price.value ?? undefined) : undefined;
  const ghostKey = JSON.stringify(
    ghostLines({
      type: order.type,
      side: order.side,
      entry,
      stops: stopLoss.ok && takeProfit.ok ? { ok: true, stopLoss: stopLoss.value, takeProfit: takeProfit.value } : { ok: false, error: "" },
      lots: order.volume,
      digits,
      pointValuePerLot: pointValue?.perLot,
    }),
  );

  useEffect(() => {
    useOrderDraft.getState().setEdit({ symbol: order.symbol, lines: JSON.parse(ghostKey) });
  }, [order.symbol, ghostKey]);
  useEffect(() => () => useOrderDraft.getState().setEdit(null), []);

  const save = () => {
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
    <Popover anchor={anchor} label={`${order.side} ${order.type.toLowerCase()} ${formatVolume(order.volume)} ${order.symbol}`} onClose={onDone}>
      <form
        className="flex flex-col gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          save();
        }}
      >
        <p className="font-medium">
          <SideLabel side={order.side} /> {order.type.toLowerCase()} {formatVolume(order.volume)} {order.symbol}
        </p>
        <Field label="Price" value={fields.price} onChange={set("price")} placeholder="" />
        <div className="grid grid-cols-2 gap-2">
          <Field label="Stop loss" value={fields.stopLoss} onChange={set("stopLoss")} />
          <Field label="Take profit" value={fields.takeProfit} onChange={set("takeProfit")} />
        </div>
        <TrailingToggle on={trailing} onChange={setTrailing} />
        <div className="flex justify-end gap-2">
          <ActionButton onClick={onDone}>Cancel</ActionButton>
          <SubmitButton disabled={modify.isPending}>Save order</SubmitButton>
        </div>
      </form>
    </Popover>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// History

/**
 * Closed positions and money in and out, such as payouts, newest first under a heading for each day: today, this week
 * or everything the terminal has, with what the closes made and a CSV file of them. Commission is for both opening and
 * closing, and the result is after it, as in the firm's portal.
 */
function History({ accountId, events, digitsOf }: { accountId: string; events: EngineEvent[]; digitsOf: DigitsOf }) {
  const timeZone = useTimeZone();
  const now = useClock();
  const profile = useProfile();
  const words = wordsFor(profile.kind);
  const currency = useTradingStore((s) => s.account?.currency) ?? "";
  const openSheet = useSheet((s) => s.open);
  const [range, setRange] = useState<HistoryRange>("all");
  const all = historyRows(events);
  const rows = inRange(all, range, timeZone, now);
  const summary = historySummary(rows, events);
  const moneyName = (amount: number) => (amount >= 0 ? "Deposit" : words.withdrawal);

  const download = () => {
    const csv = historyCsv(rows, events, digitsOf, timeZone, { deposit: "Deposit", withdrawal: words.withdrawal });
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = `history-${accountId}-${range}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  };

  return (
    <>
      <Toolbar>
        <Segmented label="Period" value={range} options={historyRanges} onChange={setRange} />
        <span className="min-w-0 truncate text-xs text-muted">
          {summary.trades === 1 ? "1 trade" : `${summary.trades} trades`},{" "}
          <span className={`font-medium tabular-nums ${summary.result >= 0 ? "text-profit" : "text-loss"}`}>{formatSignedMoney(summary.result)}</span>{" "}
          {currency} after commission
        </span>
        <button
          type="button"
          onClick={download}
          disabled={rows.length === 0}
          title="The rows shown, as a file for a spreadsheet"
          className="ml-auto flex shrink-0 items-center gap-1.5 rounded-md px-2 py-1 text-xs font-medium text-muted transition-colors duration-150 hover:bg-raised hover:text-foreground disabled:opacity-40"
        >
          <DownloadIcon className="size-3.5" />
          Export CSV
        </button>
      </Toolbar>
      {rows.length === 0 ? (
        <Empty text={all.length === 0 ? "No closed positions yet." : range === "today" ? "Nothing closed today." : "Nothing closed this week."} />
      ) : (
        <Table
          columns={[
            { name: "Time" },
            { name: "Symbol" },
            { name: "Side" },
            { name: "Volume", number: true },
            { name: "Open price", number: true },
            { name: "Close price", number: true },
            { name: "Reason" },
            { name: "Commission", number: true },
            { name: "Result", number: true },
            { name: "", label: "Details" },
          ]}
        >
          {byDay(rows, (r) => r.timestamp, timeZone, now).map((group) => (
            <DayRows key={group.day} heading={group.heading} span={10}>
              {group.rows.map((row) => (
                <HistoryLine
                  key={`${row.kind}-${"positionId" in row ? row.positionId : row.operationId}-${row.timestamp}`}
                  row={row}
                  events={events}
                  digitsOf={digitsOf}
                  moneyName={moneyName}
                  onDetails={profile.modules.tradeDetails ? (positionId) => openSheet({ kind: "details", positionId }) : null}
                />
              ))}
            </DayRows>
          ))}
        </Table>
      )}
    </>
  );
}

function HistoryLine({
  row,
  events,
  digitsOf,
  moneyName,
  onDetails,
}: {
  row: HistoryRow;
  events: EngineEvent[];
  digitsOf: DigitsOf;
  moneyName: (amount: number) => string;
  /** Opens the trade's details, or null when the firm's profile leaves them out (ADR 0058). */
  onDetails: ((positionId: string) => void) | null;
}) {
  const timeZone = useTimeZone();
  if (isBalanceOperation(row)) {
    return (
      <tr className="border-t border-border transition-colors duration-150 hover:bg-raised/50">
        <Cell className="text-muted">{formatTime(row.timestamp, timeZone)}</Cell>
        {/* Money in or out of the account, such as a payout, is not a trading result, so it is told in words. */}
        <td colSpan={9} className="px-3 py-1.5 text-muted">
          {moneyName(row.amount)} of <span className="text-foreground tabular-nums">{formatMoney(Math.abs(row.amount))}</span>, balance{" "}
          <span className="tabular-nums">{formatMoney(row.balanceAfter)}</span>
        </td>
      </tr>
    );
  }

  const digits = digitsOf(row.symbol);
  const part = isPart(row);
  const result = rowResult(row, events);
  const commission = part ? row.commission : positionCommission(row, events);
  return (
    <tr className="border-t border-border transition-colors duration-150 hover:bg-raised/50">
      <Cell className="text-muted">{formatTime(row.timestamp, timeZone)}</Cell>
      <SymbolCell symbol={row.symbol} />
      <Cell>
        <SideLabel side={row.side} />
      </Cell>
      <Cell number>{formatVolume(row.volume)}</Cell>
      <Cell number>{formatPrice(row.openPrice, digits)}</Cell>
      <Cell number>{formatPrice(row.closePrice, digits)}</Cell>
      <Cell>{part ? <span title={`${formatVolume(row.remainingVolume)} lots stayed open`}>Part closed</span> : closeReasons[row.reason]}</Cell>
      <Cell number className="text-muted">
        {formatMoney(commission)}
      </Cell>
      <Cell number className={`font-medium ${result >= 0 ? "text-profit" : "text-loss"}`}>
        <span title={`${formatSignedMoney(row.profit)} before ${formatMoney(commission)} commission`}>{formatSignedMoney(result)}</span>
      </Cell>
      <td className="px-3 py-1 text-right">
        {onDetails && (
          <ActionButton onClick={() => onDetails(row.positionId)} title="The prices behind this trade">
            Details
          </ActionButton>
        )}
      </td>
    </tr>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// Events

const eventTopics: { value: EventTopic | "all"; label: string }[] = [
  { value: "all", label: "All" },
  { value: "trades", label: "Trades" },
  { value: "account", label: "Account" },
  { value: "warnings", label: "Warnings" },
];

/**
 * Everything that happened on the account, newest first under a heading for each day, by topic. The same event several
 * times in a row, such as a refusal while prices were old, is told once with how many times (ADR 0058).
 */
function Events({ events, digitsOf }: { events: EngineEvent[]; digitsOf: DigitsOf }) {
  const timeZone = useTimeZone();
  const now = useClock();
  const [topic, setTopic] = useState<EventTopic | "all">("all");
  const describe = (event: EngineEvent) => describeEvent(event, digitsOf);
  const runs = collapseRepeats(
    events.filter((e) => topic === "all" || topicOf(e) === topic),
    describe,
  ).reverse();

  return (
    <>
      <Toolbar>
        <Segmented label="Show" value={topic} options={eventTopics} onChange={setTopic} />
      </Toolbar>
      {runs.length === 0 ? (
        <Empty text={events.length === 0 ? "No events yet." : "No events of this kind."} />
      ) : (
        <div className="min-h-0 flex-1 overflow-y-auto">
          {byDay(runs, (r) => r.event.timestamp, timeZone, now).map((group) => (
            <section key={group.day} aria-label={group.heading}>
              <h3 className="sticky top-0 z-[1] border-b border-border bg-panel px-3 py-1 text-[11px] font-medium text-muted">{group.heading}</h3>
              <ul className="divide-y divide-border">
                {group.rows.map(({ event, count, first }, index) => (
                  <li key={`${event.timestamp}-${index}`} className="flex gap-4 px-3 py-1.5">
                    <span className="w-16 shrink-0 text-muted tabular-nums">{formatTime(event.timestamp, timeZone)}</span>
                    <span className={`min-w-0 flex-1 ${isWarning(event) ? "text-warning" : ""}`}>
                      {describe(event)}
                      {event.kind === "EquityFloorBreached" && (
                        <span className="text-muted">
                          {" "}
                          Prices:{" "}
                          {event.prices
                            .map((p) => `${p.symbol} ${formatPrice(p.bid, digitsOf(p.symbol))}/${formatPrice(p.ask, digitsOf(p.symbol))}`)
                            .join(", ") || "none"}
                        </span>
                      )}
                    </span>
                    {count > 1 && (
                      <span className="shrink-0 text-xs text-muted" title={`The first at ${formatTime(first, timeZone)}`}>
                        {count} times since {formatTime(first, timeZone)}
                      </span>
                    )}
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      )}
    </>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// Building blocks

type Column = { name: string; number?: boolean; label?: string };

/** A table whose head stays in view while it scrolls, with numbers right-aligned under right-aligned headings. */
function Table({ columns, children, footer }: { columns: Column[]; children: React.ReactNode; footer?: React.ReactNode }) {
  return (
    <div className="min-h-0 flex-1 overflow-auto">
      <table className="w-full">
        <thead className="sticky top-0 z-[1] bg-panel text-xs text-muted">
          <tr>
            {columns.map((c, i) => (
              <th key={`${c.name}-${i}`} scope="col" className={`px-3 py-2 font-normal whitespace-nowrap ${c.number ? "text-right" : "text-left"}`}>
                {c.name || <span className="sr-only">{c.label}</span>}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
        {footer && <tfoot className="sticky bottom-0 bg-panel">{footer}</tfoot>}
      </table>
    </div>
  );
}

/** A day's rows under its heading, in a table. */
function DayRows({ heading, span, children }: { heading: string; span: number; children: React.ReactNode }) {
  return (
    <>
      <tr>
        <th colSpan={span} scope="colgroup" className="border-t border-border bg-background/40 px-3 py-1 text-left text-[11px] font-medium text-muted">
          {heading}
        </th>
      </tr>
      {children}
    </>
  );
}

function Toolbar({ children }: { children: React.ReactNode }) {
  return <div className="flex h-9 shrink-0 items-center gap-3 border-b border-border px-3">{children}</div>;
}

function Segmented<T extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: T;
  options: { value: T; label: string }[];
  onChange: (value: T) => void;
}) {
  return (
    <div role="group" aria-label={label} className="flex shrink-0 rounded-md bg-background p-0.5 text-xs">
      {options.map((o) => (
        <button
          key={o.value}
          type="button"
          aria-pressed={o.value === value}
          onClick={() => onChange(o.value)}
          className={`rounded px-2.5 py-0.5 font-medium transition duration-150 ${o.value === value ? "bg-raised text-foreground shadow-card" : "text-muted hover:text-foreground"}`}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}

function Cell({ children, number, className = "", title }: { children: React.ReactNode; number?: boolean; className?: string; title?: string }) {
  return (
    <td title={title} className={`px-3 py-1.5 whitespace-nowrap ${number ? "text-right tabular-nums" : ""} ${className}`}>
      {children}
    </td>
  );
}

// The id of a position or order means nothing to a trader, so it is in the trade's details, under For support.
function SymbolCell({ symbol }: { symbol: string }) {
  return <td className="px-3 py-1.5 font-medium whitespace-nowrap">{symbol}</td>;
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
  active = false,
}: {
  children: React.ReactNode;
  onClick: () => void;
  disabled?: boolean;
  title?: string;
  danger?: boolean;
  /** Waiting for a second press, which the button says in red. */
  active?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      title={title}
      className={`rounded-md border px-2.5 py-1 text-xs font-medium whitespace-nowrap transition duration-150 active:translate-y-px disabled:opacity-40 ${
        active
          ? "border-loss/60 bg-loss/15 text-loss"
          : `border-border text-muted hover:bg-raised hover:text-foreground ${danger ? "hover:border-loss/50 hover:text-loss" : "hover:border-muted"}`
      }`}
    >
      {children}
    </button>
  );
}

function SubmitButton({ children, disabled, danger = false }: { children: React.ReactNode; disabled: boolean; danger?: boolean }) {
  return (
    <button
      type="submit"
      disabled={disabled}
      className={`rounded-md px-3 py-1 text-xs font-semibold transition duration-150 hover:brightness-110 active:translate-y-px disabled:opacity-40 ${danger ? "bg-loss text-background" : "bg-accent text-accent-foreground"}`}
    >
      {children}
    </button>
  );
}

function Empty({ text }: { text: string }) {
  return <p className="px-3 py-6 text-center text-muted">{text}</p>;
}
