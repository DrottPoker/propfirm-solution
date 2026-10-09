"use client";

import { useCallback, useEffect, useId, useRef, useState } from "react";

import { suspendedHelp } from "@/lib/account";
import { CommandRejectedError } from "@/lib/api/client";
import type { InstrumentInfo, PlaceOrderRequest, PointValue, Side } from "@/lib/api/types";
import { rejectionText } from "@/lib/events";
import { formatMoney, formatPrice, formatUnits, formatVolume, timeZoneName } from "@/lib/format";
import { closingSoon, opensText, useNow } from "@/lib/marketHours";
import { useOrderBlock, type OrderBlock } from "@/lib/orderBlock";
import { orderQuestion } from "@/lib/orderConfirm";
import { ghostLines, sideOfStops, useOrderDraft } from "@/lib/orderDraft";
import { parsePrice, parseVolume, pipSize, stepPrice, stepVolume } from "@/lib/orderInput";
import { estimatedMargin, nearestLimit, pipValue, riskText, roomName, shareOfRoom, shareRisk, stopLossRisk } from "@/lib/orderSummary";
import { orderLockText, tradesUsed, tradesUsedText } from "@/lib/ownLimits";
import { ageText } from "@/lib/priceAge";
import { useProfile } from "@/lib/profileContext";
import { useMarket, usePlaceOrder, usePointValue } from "@/lib/queries";
import { parseSizing, riskAmount, saveSizing, sizeForRisk, stepRisk, type RiskSize, type RiskUnit, type Sizing } from "@/lib/riskSize";
import { resolveStops, stepAmount, type ResolvedStops, type StopKind, type StopUnit } from "@/lib/stops";
import { useTradingStore } from "@/lib/store";
import { readSetting } from "@/lib/syncedSettings";
import { distanceUnit, pendingTypeProblem, pipsOf, pipsOfRoom, spreadText, type PendingType } from "@/lib/ticket";
import { changeVolume, useConfirmOrders, useVolume } from "@/lib/ticketStore";
import { useTimeZone } from "@/lib/timeZone";

import { ChevronDownIcon, InfoIcon, LockIcon, MinusIcon, PlusIcon } from "./icons";
import { InstrumentConditions } from "./InstrumentConditions";
import { showError } from "./TradeNotices";
import { useDismiss } from "./useDismiss";

const sides: Side[] = ["Sell", "Buy"];

type Mode = "market" | "limit" | "stop";

const modes: { mode: Mode; label: string; title: string }[] = [
  { mode: "market", label: "Market", title: "Opens now at the market price" },
  { mode: "limit", label: "Limit", title: "Waits for a better price: below the market for a buy, above it for a sell" },
  { mode: "stop", label: "Stop", title: "Waits for the price to move through a level: above the market for a buy, below it for a sell" },
];

type StopFields = Record<StopKind, string>;

const noStops: StopFields = { stopLoss: "", takeProfit: "" };
const emptyStops: Record<StopUnit, StopFields> = { price: noStops, money: noStops, pips: noStops };

/** A size from risk, or why there is none. A prompt asks for something not typed yet, rather than a problem. */
type Sized = RiskSize | { ok: false; reason: string; prompt: true };

/**
 * What a side's order would be, or why it cannot be sent. A prompt asks for something not typed yet. A limit or a stop
 * on the wrong side of the market is expected for one side, since its price fits only one, and its reason names the
 * side.
 */
type Check = { ok: true; order: PlaceOrderRequest } | { ok: false; reason: string; prompt: boolean; wrongType?: boolean };

export function OrderPanel({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo | null }) {
  // Nothing to preview without a symbol.
  useEffect(() => () => useOrderDraft.getState().setDraft(null, []), []);

  return (
    <aside aria-label="Order panel" data-tour="order-panel" className="flex min-h-0 flex-col overflow-y-auto bg-panel text-sm">
      {instrument ? <OrderTicket accountId={accountId} instrument={instrument} /> : null}
    </aside>
  );
}

/**
 * The order ticket (ADR 0058): a market, limit or stop order, sized in lots or from what it risks, with its stops as
 * prices, pips or amounts, and buttons that say exactly what they send. A limit or a stop price fits one side of the
 * market, so the other side's button waits and says why when pointed at. What is missing or wrong is said under the
 * buttons, which wait until it is fixed. What the order means comes last: its margin, what a pip is worth, and what it risks against the room
 * left today.
 */
function OrderTicket({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo }) {
  const profile = useProfile();
  const [mode, setMode] = useState<Mode>("market");
  const volume = useVolume(instrument);
  const [price, setPrice] = useState("");
  const [unit, setUnit] = useState<StopUnit>("price");
  // One set of fields per unit, so switching never reads a price as an amount.
  const [stops, setStops] = useState<Record<StopUnit, StopFields>>(emptyStops);
  // The side the trader points at or has focused, which the chart preview and the summary follow.
  const [pointedSide, setPointedSide] = useState<Side | null>(null);
  // Kept between orders, like the unit of the stops.
  const [trailing, setTrailing] = useState(false);
  // In lots, or from what the order risks at its stop loss. Kept between orders and on every device (ADR 0052). Until
  // the trader chose, a firm that starts tickets from the risk does so (ADR 0058).
  const [sizing, setSizing] = useState<Sizing>(() => startSizing(profile.startingSize));
  const confirmOrders = useConfirmOrders();
  const [asking, setAsking] = useState<PlaceOrderRequest | null>(null);
  const cancelAsking = useCallback(() => setAsking(null), []);
  const [symbol, setSymbol] = useState(instrument.symbol);

  // A new symbol starts clean: its own price and stops, and no question waiting.
  if (symbol !== instrument.symbol) {
    setSymbol(instrument.symbol);
    setPrice("");
    setStops(emptyStops);
    setAsking(null);
  }

  const quote = useTradingStore((s) => s.prices[instrument.symbol]);
  const status = useTradingStore((s) => s.account?.status);
  // The trader's own lock or trades a day (ADR 0054), which take no new orders until the next trading day.
  const own = useTradingStore((s) => (s.account?.status === "Active" ? s.account.ownLimits : null));
  const accountCurrency = useTradingStore((s) => s.account?.currency) ?? "";
  const balance = useTradingStore((s) => s.account?.balance);
  const floors = useTradingStore((s) => s.account?.floors);
  const limit = nearestLimit(floors ?? []);
  const room = limit?.headroom;
  const placeOrder = usePlaceOrder(accountId);
  const pointValue = usePointValue(accountId, instrument.symbol).data ?? undefined;
  const market = useMarket(accountId, instrument.symbol);
  const timeZone = useTimeZone();
  const closing = closingSoon(market, useNow(15_000), timeZone);
  // Why no order is taken at all now, whatever is typed.
  const { block: blocked, priceAge } = useOrderBlock(accountId, instrument.symbol);

  const digits = instrument.digits;
  const unitName = distanceUnit(instrument);
  const riskMode = sizing.mode === "risk" && profile.modules.riskSizing;
  const parsedVolume = parseVolume(volume, instrument);
  const typedLots = parsedVolume.ok ? (parsedVolume.value ?? undefined) : undefined;
  const pending = mode !== "market";
  const typedPrice = pending ? parsePrice(price, digits) : null;
  const orderPrice = typedPrice?.ok ? (typedPrice.value ?? undefined) : undefined;
  // Sizing from risk needs the stop loss as a distance in prices or pips, since an amount would already be the risk.
  const stopUnit: StopUnit = riskMode && unit === "money" ? "price" : unit;
  const fields = stops[stopUnit];

  const changeSizing = (next: Sizing) => {
    setSizing(next);
    saveSizing(next);
  };
  const changeRisk = (risk: string) => changeSizing({ ...sizing, risks: { ...sizing.risks, [sizing.unit]: risk } });

  // Amounts and pips are counted from where the order opens: the ask for a market buy, the bid for a market sell, or the
  // pending order's price.
  const entryFor = (side: Side) => (mode === "market" ? (side === "Buy" ? quote?.ask : quote?.bid) : orderPrice);
  const type: PendingType | "Market" = mode === "market" ? "Market" : mode === "limit" ? "Limit" : "Stop";

  // The volume that loses the risk at the stop loss, per side, since each side opens at its own price.
  const amount = riskMode ? riskAmount(sizing.risks[sizing.unit], sizing.unit, balance, room) : null;
  const sizeFor = (side: Side): Sized => {
    const entry = entryFor(side);
    if (amount === null) {
      if (sizing.risks[sizing.unit].trim() === "") {
        return { ok: false, reason: "Enter what the order may lose at its stop loss.", prompt: true };
      }

      return {
        ok: false,
        reason:
          sizing.unit === "room" && room === undefined
            ? "The account has no loss limit to take a share of."
            : sizing.unit === "money"
              ? "Enter the risk as an amount with at most 2 decimals."
              : "Enter the risk as a percent from 0.01 to 100.",
      };
    }

    const stopLoss = resolveStops(stopUnit, fields.stopLoss, "", side, entry, 0, digits, undefined);
    if (!stopLoss.ok) {
      return { ok: false, reason: stopLoss.error };
    }

    if (stopLoss.stopLoss === null) {
      return { ok: false, reason: "Set a stop loss to size the order from the risk.", prompt: true };
    }

    if (entry === undefined || !pointValue) {
      return { ok: false, reason: mode === "market" ? "Waiting for a price." : "Enter the order's price.", prompt: true };
    }

    return sizeForRisk({ amount, side, entry, stopLoss: stopLoss.stopLoss, digits, pointValuePerLot: pointValue.perLot, limits: instrument });
  };
  const lotsFor = (side: Side): number | undefined => {
    if (!riskMode) {
      return typedLots;
    }

    const size = sizeFor(side);
    return size.ok ? size.lots : undefined;
  };

  const resolve = (side: Side) =>
    resolveStops(stopUnit, fields.stopLoss, fields.takeProfit, side, entryFor(side), lotsFor(side) ?? 0, digits, pointValue?.perLot);

  /** The order a side would send, or why it cannot, in the order the trader meets the fields. */
  const check = (side: Side): Check => {
    if (type !== "Market") {
      if (price.trim() === "") {
        return { ok: false, reason: "Enter the price the order waits for.", prompt: true };
      }

      if (orderPrice === undefined) {
        return { ok: false, reason: `Enter the price with at most ${digits} decimals.`, prompt: false };
      }

      if (!quote) {
        return { ok: false, reason: "Waiting for a price.", prompt: true };
      }

      const problem = pendingTypeProblem(side, type, orderPrice, quote, digits);
      if (problem) {
        return { ok: false, reason: problem, prompt: false, wrongType: true };
      }
    }

    if (riskMode) {
      const size = sizeFor(side);
      if (!size.ok) {
        return { ok: false, reason: size.reason, prompt: "prompt" in size };
      }
    } else if (!parsedVolume.ok) {
      return {
        ok: false,
        reason: `The volume goes from ${instrument.volumeMin} to ${instrument.volumeMax} lots in steps of ${instrument.volumeStep}.`,
        prompt: false,
      };
    }

    const resolved = resolve(side);
    if (!resolved.ok) {
      return { ok: false, reason: resolved.error, prompt: false };
    }

    const wrong = wrongSide(side, resolved, pending ? orderPrice : side === "Buy" ? quote?.bid : quote?.ask, digits);
    if (wrong) {
      return { ok: false, reason: wrong, prompt: false };
    }

    if (trailing && resolved.stopLoss === null) {
      return { ok: false, reason: "Set a stop loss for the trailing stop to follow.", prompt: true };
    }

    return {
      ok: true,
      order: {
        orderId: crypto.randomUUID(),
        symbol: instrument.symbol,
        side,
        type,
        volume: lotsFor(side) ?? 0,
        price: type === "Market" ? null : orderPrice,
        stopLoss: resolved.stopLoss,
        takeProfit: resolved.takeProfit,
        trailingStop: trailing,
      },
    };
  };
  const checks = { Sell: check("Sell"), Buy: check("Buy") };

  const send = (order: PlaceOrderRequest) =>
    placeOrder.mutate(order, {
      onSuccess: () => {
        // The next order starts clean but keeps the volume, the mode and how stops are typed.
        setPrice("");
        setStops(emptyStops);
      },
      onError: (e) => showError(e instanceof CommandRejectedError ? rejectionText(e.reason) : "Could not reach the trading service."),
    });

  const submit = (side: Side) => {
    const result = checks[side];
    if (!result.ok) {
      return;
    }

    if (confirmOrders) {
      setAsking(result.order);
    } else {
      send(result.order);
    }
  };

  // A price step is one pip. An amount step is what one pip is worth at the volume.
  const pipAmount = pointValue && typedLots ? pipValue(typedLots, digits, pointValue.perLot) : 10;
  const startPrice = orderPrice ?? quote?.bid;
  const setStop = (kind: StopKind, value: string) => setStops((s) => ({ ...s, [stopUnit]: { ...s[stopUnit], [kind]: value } }));
  const stepStop = (kind: StopKind, direction: 1 | -1) =>
    setStop(
      kind,
      stopUnit === "price"
        ? stepPrice(fields[kind], direction, digits, startPrice)
        : stopUnit === "pips"
          ? stepPips(fields[kind], direction)
          : stepAmount(fields[kind], direction, pipAmount),
    );

  // Typed prices fit one side at most, and amounts fit both. Without a side to go by, the chart shows a buy.
  const typedStops = resolveStops("price", stops.price.stopLoss, stops.price.takeProfit, "Buy", undefined, 0, digits, undefined);
  const fittingSide =
    stopUnit === "price" && typedStops.ok && quote
      ? sideOfStops(typedStops.stopLoss, typedStops.takeProfit, orderPrice ?? quote.bid, orderPrice ?? quote.ask)
      : null;
  const ghostSide = pointedSide ?? fittingSide ?? "Buy";
  const ghostKey = JSON.stringify(
    ghostLines({
      type,
      side: ghostSide,
      entry: entryFor(ghostSide),
      stops: resolve(ghostSide),
      lots: lotsFor(ghostSide),
      digits,
      pointValuePerLot: pointValue?.perLot,
    }),
  );

  useEffect(() => {
    useOrderDraft.getState().setDraft(instrument.symbol, JSON.parse(ghostKey));
  }, [instrument.symbol, ghostKey]);

  // A level picked on the chart fills in a field as a price, and a removed ghost line empties its field. An order price
  // picked on the chart makes a market ticket a limit order.
  useEffect(
    () =>
      useOrderDraft.subscribe((state, previous) => {
        const request = state.fieldRequest;
        if (!request || request === previous.fieldRequest || request.symbol !== instrument.symbol) {
          return;
        }

        const text = request.price === null ? "" : request.price.toFixed(instrument.digits);
        if (request.field === "entry") {
          setPrice(text);
          if (request.price !== null) {
            setMode((m) => (m === "market" ? "limit" : m));
          }
        } else if (request.price === null) {
          // The ghost shows whichever unit is chosen, and removing it means no stop in any.
          setStops((s) => ({
            price: { ...s.price, [request.field]: "" },
            money: { ...s.money, [request.field]: "" },
            pips: { ...s.pips, [request.field]: "" },
          }));
        } else {
          setUnit("price");
          setStops((s) => ({ ...s, price: { ...s.price, [request.field]: text } }));
        }
      }),
    [instrument.symbol, instrument.digits],
  );

  const accountState = status === "Disabled" ? "ended" : status === "Suspended" ? "paused" : null;
  // Which side's problem is said under the buttons: the one pointed at, or the first one with a problem. A limit or a
  // stop on the wrong side of the market is said only when pointed at while the other side can be sent.
  const said = (side: Side) => {
    const result = checks[side];
    return !result.ok && !(result.wrongType && checks[side === "Buy" ? "Sell" : "Buy"].ok);
  };
  const shownSide = pointedSide && !checks[pointedSide].ok ? pointedSide : (sides.find(said) ?? null);
  const shownCheck = shownSide ? checks[shownSide] : null;
  const sameForBoth = !checks.Sell.ok && !checks.Buy.ok && checks.Sell.reason === checks.Buy.reason;

  return (
    <div className="flex flex-col">
      <TicketHeader accountId={accountId} instrument={instrument} />

      {accountState ? (
        <AccountStateNotice state={accountState} />
      ) : (
        <div className="flex flex-col gap-3.5 p-3">
          <div className="grid grid-cols-3 gap-1 rounded-lg bg-background p-1" role="group" aria-label="Order type">
            {modes.map((m) => (
              <button
                key={m.mode}
                type="button"
                onClick={() => setMode(m.mode)}
                aria-pressed={m.mode === mode}
                title={m.title}
                className={`rounded-md px-2 py-1.5 font-medium transition duration-150 ease-out-soft active:translate-y-px ${m.mode === mode ? "bg-accent text-accent-foreground shadow-[inset_0_1px_0_rgb(255_255_255/0.2)]" : "text-muted hover:bg-raised hover:text-foreground"}`}
              >
                {m.label}
              </button>
            ))}
          </div>

          {pending && (
            <Field
              label="Price"
              value={price}
              onChange={setPrice}
              placeholder="The price to wait for"
              onStep={(direction) => setPrice(stepPrice(price, direction, digits, quote?.bid))}
              aside={
                <button
                  type="button"
                  onClick={() => quote && setPrice(quote.bid.toFixed(digits))}
                  className="text-[11px] text-muted underline-offset-2 transition-colors duration-150 hover:text-foreground hover:underline"
                >
                  Use the market price
                </button>
              }
            />
          )}

          <SizeField
            sizing={riskMode ? sizing : { ...sizing, mode: "lots" }}
            volume={volume}
            currency={accountCurrency || "money"}
            riskAvailable={profile.modules.riskSizing}
            roomAvailable={room !== undefined}
            onSizing={changeSizing}
            onVolume={(next) => changeVolume(instrument, next)}
            onStepVolume={(direction) => changeVolume(instrument, stepVolume(volume, direction, instrument))}
            onRisk={changeRisk}
            onStepRisk={(direction) => changeRisk(stepRisk(sizing.risks[sizing.unit], direction, sizing.unit))}
            below={
              riskMode ? (
                <RiskResult size={sizeFor(ghostSide)} currency={pointValue?.currency ?? accountCurrency} />
              ) : typedLots !== undefined ? (
                <span className="text-xs text-muted">
                  = {formatUnits(typedLots * instrument.contractSize)} {instrument.baseCurrency}
                </span>
              ) : null
            }
          />

          <div className="flex flex-col gap-2">
            <div className="flex items-center justify-between gap-2">
              <span className="text-xs text-muted">Stop loss and take profit</span>
              <UnitSelect
                label="Stops in"
                value={stopUnit}
                options={[
                  { value: "price", label: "Price" },
                  { value: "pips", label: unitName === "pips" ? "Pips" : "Points" },
                  ...(riskMode ? [] : [{ value: "money" as const, label: pointValue?.currency ?? accountCurrency ?? "Amount" }]),
                ]}
                onChange={(next) => setUnit(next as StopUnit)}
              />
            </div>
            <div className="grid grid-cols-2 gap-2">
              <Field
                label="Stop loss"
                value={fields.stopLoss}
                onChange={(value) => setStop("stopLoss", value)}
                placeholder="None"
                compact
                onStep={(d) => stepStop("stopLoss", d)}
              />
              <Field
                label="Take profit"
                value={fields.takeProfit}
                onChange={(value) => setStop("takeProfit", value)}
                placeholder="None"
                compact
                onStep={(d) => stepStop("takeProfit", d)}
              />
            </div>
            <Switch
              checked={trailing}
              onChange={setTrailing}
              label="Trailing stop"
              title="The stop loss follows the price at the distance it is set at, and never moves back. The trading service moves it, also when the terminal is closed."
            />
          </div>

          <MarketNotes
            instrument={instrument}
            blocked={blocked}
            marketText={market ? opensText(market, timeZone, timeZoneName(timeZone)) : null}
            priceAge={priceAge}
            closing={closing}
            ownText={
              own?.lock ? orderLockText(own.lock, own.tradingDay.timeZone) : own && tradesUsed(own) ? tradesUsedText(own, own.tradingDay.timeZone) : null
            }
          />

          {asking ? (
            <ConfirmOrder
              order={asking}
              digits={digits}
              disabled={blocked !== null || placeOrder.isPending}
              onConfirm={() => {
                send(asking);
                setAsking(null);
              }}
              onCancel={cancelAsking}
            />
          ) : (
            <div className="flex flex-col gap-1.5">
              <div className="grid grid-cols-[1fr_auto_1fr] items-stretch gap-1.5">
                <TradeButton
                  side="Sell"
                  label={buttonLabel("Sell", type)}
                  price={formatPrice(pending ? orderPrice : quote?.bid, digits)}
                  disabled={blocked !== null || !checks.Sell.ok || placeOrder.isPending}
                  reason={checks.Sell.ok ? undefined : checks.Sell.reason}
                  onClick={() => submit("Sell")}
                  onPoint={(pointed) => setPointedSide(pointed ? "Sell" : null)}
                />
                <span
                  className="live flex min-w-9 flex-col items-center justify-center text-[11px] text-muted"
                  title={quote ? `Spread: ${spreadText(quote, instrument)}` : "Spread"}
                >
                  {quote ? pipsOf(quote.ask - quote.bid, digits).toFixed(1) : "-"}
                </span>
                <TradeButton
                  side="Buy"
                  label={buttonLabel("Buy", type)}
                  price={formatPrice(pending ? orderPrice : quote?.ask, digits)}
                  disabled={blocked !== null || !checks.Buy.ok || placeOrder.isPending}
                  reason={checks.Buy.ok ? undefined : checks.Buy.reason}
                  onClick={() => submit("Buy")}
                  onPoint={(pointed) => setPointedSide(pointed ? "Buy" : null)}
                />
              </div>
              {blocked === null && shownCheck && !shownCheck.ok && (
                <p role="status" className={`text-xs ${shownCheck.prompt ? "text-muted" : "text-warning"}`}>
                  {sameForBoth || !shownSide || shownCheck.wrongType ? "" : `${shownSide}: `}
                  {shownCheck.reason}
                </p>
              )}
            </div>
          )}

          <OrderSummary
            instrument={instrument}
            lots={lotsFor(ghostSide)}
            price={orderPrice ?? (quote ? (quote.bid + quote.ask) / 2 : undefined)}
            pointValue={pointValue}
            side={ghostSide}
            entry={entryFor(ghostSide)}
            stops={resolve(ghostSide)}
            unitName={unitName}
          />
        </div>
      )}
    </div>
  );
}

/**
 * The symbol, its name and the button with its trading conditions: leverage, spread and markup, commission, contract
 * and trading hours, which stay out of the way until asked for.
 */
function TicketHeader({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const id = useId();
  const close = useCallback(() => setOpen(false), []);
  useDismiss(open, ref, close);

  return (
    <div ref={ref} className="relative flex h-12 shrink-0 items-center justify-between gap-2 border-b border-border px-3">
      <h2 className="flex min-w-0 flex-col leading-tight">
        <span className="font-semibold">{instrument.symbol}</span>
        <span className="truncate text-xs text-muted">{instrument.name}</span>
      </h2>
      <button
        type="button"
        aria-expanded={open}
        aria-controls={id}
        onClick={() => setOpen((o) => !o)}
        className={`flex items-center gap-1.5 rounded-md px-2 py-1 text-xs transition-colors duration-150 hover:bg-raised hover:text-foreground ${open ? "bg-raised text-foreground" : "text-muted"}`}
      >
        <InfoIcon className="size-3.5" />
        Conditions
      </button>
      {open && (
        <div
          id={id}
          role="dialog"
          aria-label={`${instrument.symbol} trading conditions`}
          className="absolute top-full right-2 left-2 z-30 mt-1 flex animate-pop flex-col gap-1.5 rounded-lg border border-border bg-panel p-3 text-xs shadow-float"
        >
          <InstrumentConditions accountId={accountId} instrument={instrument} />
        </div>
      )}
    </div>
  );
}

/** Instead of the ticket on an account that takes no orders: why, and what the trader can still do. */
function AccountStateNotice({ state }: { state: "ended" | "paused" }) {
  return (
    <div className="m-3 flex flex-col gap-1.5 rounded-lg border border-border bg-background/40 p-3 text-xs">
      <p className="flex items-center gap-2 text-sm font-medium">
        <LockIcon className="size-4 text-muted" />
        {state === "ended" ? "Trading on this account has ended" : "Trading is paused"}
      </p>
      <p className="leading-relaxed text-muted">
        {state === "ended" ? "No new orders are taken. The chart, the history and the account's figures stay for you to look at." : suspendedHelp}
      </p>
    </div>
  );
}

/** Notes about the market and the trader's own limits, which say why orders wait. */
function MarketNotes({
  instrument,
  blocked,
  marketText,
  priceAge,
  closing,
  ownText,
}: {
  instrument: InstrumentInfo;
  blocked: OrderBlock | null;
  marketText: string | null;
  priceAge: number | null;
  closing: { minutes: number; at: string } | null;
  ownText: string | null;
}) {
  const note =
    blocked === "closed"
      ? { tone: "muted", text: `The ${instrument.symbol} market is closed.${marketText ? ` ${marketText}.` : ""}` }
      : blocked === "stale" && priceAge !== null
        ? { tone: "warning", text: `No new ${instrument.symbol} price for ${ageText(priceAge)}. Orders wait for one, so nothing fills at an old price.` }
        : (blocked === "locked" || blocked === "trades") && ownText
          ? { tone: "accent", text: ownText }
          : blocked === "offline"
            ? { tone: "warning", text: "Not connected to the trading service. Orders wait until the connection is back." }
            : blocked === "waiting"
              ? { tone: "muted", text: "Waiting for prices." }
              : closing
                ? { tone: "warning", text: `The market closes in ${closing.minutes} min, at ${closing.at}. Open positions stay open over the close.` }
                : null;
  if (!note) {
    return null;
  }

  const tones: Record<string, string> = { muted: "bg-raised text-muted", warning: "bg-warning/10 text-warning", accent: "bg-accent/10 text-accent" };
  return (
    <p role="note" className={`rounded-md px-3 py-2 text-xs leading-relaxed ${tones[note.tone]}`}>
      {note.text}
    </p>
  );
}

/** What risking an amount or a share means for each risk unit, for the size's unit list. */
function riskUnitLabels(currency: string): Record<RiskUnit, string> {
  return { money: `Risk in ${currency}`, balance: "Risk, % of balance", room: "Risk, % of today's limit" };
}

/**
 * The order's size: lots, or what it may lose at its stop loss, in the account currency, as a share of the balance or
 * of the room left to the nearest loss limit (ADR 0052). One field, with its unit chosen beside it.
 */
function SizeField({
  sizing,
  volume,
  currency,
  riskAvailable,
  roomAvailable,
  onSizing,
  onVolume,
  onStepVolume,
  onRisk,
  onStepRisk,
  below,
}: {
  sizing: Sizing;
  volume: string;
  currency: string;
  riskAvailable: boolean;
  roomAvailable: boolean;
  onSizing: (sizing: Sizing) => void;
  onVolume: (volume: string) => void;
  onStepVolume: (direction: 1 | -1) => void;
  onRisk: (risk: string) => void;
  onStepRisk: (direction: 1 | -1) => void;
  below: React.ReactNode;
}) {
  const labels = riskUnitLabels(currency);
  const selected = sizing.mode === "lots" ? "lots" : sizing.unit;
  const options = [
    { value: "lots", label: "Lots" },
    ...(riskAvailable
      ? (["money", "balance", ...(roomAvailable || sizing.unit === "room" ? ["room" as const] : [])] as RiskUnit[]).map((u) => ({ value: u, label: labels[u] }))
      : []),
  ];
  const risk = sizing.mode === "risk";
  return (
    <div className="flex flex-col gap-1">
      <Field
        label={risk ? "Risk" : "Volume"}
        value={risk ? sizing.risks[sizing.unit] : volume}
        onChange={risk ? onRisk : onVolume}
        onStep={risk ? onStepRisk : onStepVolume}
        placeholder={risk ? (sizing.unit === "money" ? "Amount" : "Percent") : undefined}
        aside={
          options.length > 1 ? (
            <UnitSelect
              label="Size in"
              value={selected}
              options={options}
              onChange={(next) => onSizing(next === "lots" ? { ...sizing, mode: "lots" } : { ...sizing, mode: "risk", unit: next as RiskUnit })}
            />
          ) : (
            <span className="text-[11px] text-muted">Lots</span>
          )
        }
      />
      {below}
    </div>
  );
}

/** The volume the risk gives and what it then risks, or why there is none. */
function RiskResult({ size, currency }: { size: Sized; currency: string }) {
  if (!size.ok) {
    return (
      <span role="status" className={`text-xs ${"prompt" in size ? "text-muted" : "text-warning"}`}>
        {size.reason}
      </span>
    );
  }

  return (
    <span role="status" className="text-xs text-muted" title="Estimated at the stop loss, before commission">
      = <span className="text-foreground">{formatVolume(size.lots)}</span> lots, risks {formatMoney(size.risk)} {currency}
      {size.atMost && ", the largest volume allowed"}
    </span>
  );
}

const riskColors = { ok: "text-foreground", warning: "text-warning", danger: "text-loss" };
const riskBars = { ok: "bg-accent", warning: "bg-warning", danger: "bg-loss" };

/**
 * What the order means before it is placed: the margin it takes from the free margin, what a pip is worth at the
 * volume, and with a stop loss what the order risks, also as a share of the room left to the nearest loss limit.
 * Without a stop loss, how many pips the room lasts at the volume.
 */
function OrderSummary({
  instrument,
  lots,
  price,
  pointValue,
  side,
  entry,
  stops,
  unitName,
}: {
  instrument: InstrumentInfo;
  lots: number | undefined;
  price: number | undefined;
  pointValue: PointValue | undefined;
  side: Side;
  entry: number | undefined;
  stops: ResolvedStops;
  unitName: "pips" | "points";
}) {
  const freeMargin = useTradingStore((s) => s.account?.freeMargin);
  const floors = useTradingStore((s) => s.account?.floors);
  const { digits } = instrument;
  const ready = lots !== undefined && pointValue !== undefined;
  const currency = pointValue?.currency ?? "";

  const margin =
    ready && price !== undefined
      ? estimatedMargin({
          lots,
          price,
          digits,
          contractSize: instrument.contractSize,
          leverage: instrument.leverage,
          pointValuePerLot: pointValue.perLot,
          baseIsAccountCurrency: instrument.baseCurrency === pointValue.currency,
        })
      : null;
  const tooMuchMargin = margin !== null && freeMargin !== undefined && margin > freeMargin;
  const pip = ready ? pipValue(lots, digits, pointValue.perLot) : null;
  const risk =
    ready && entry !== undefined && stops.ok && stops.stopLoss !== null ? stopLossRisk(side, lots, entry, stops.stopLoss, digits, pointValue.perLot) : null;
  const hasStopLoss = stops.ok && stops.stopLoss !== null;
  // Only a known volume and entry tell a stop loss on the wrong side from one that cannot be priced yet.
  const wrongSide = ready && entry !== undefined && hasStopLoss && risk === null;
  const limit = nearestLimit(floors ?? []);
  const level = risk !== null && limit ? shareRisk(shareOfRoom(risk, limit)) : "ok";
  const one = unitName.slice(0, -1);

  return (
    <section aria-label="What this order means" className="flex flex-col gap-2 border-t border-border pt-3 text-xs">
      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
        <dt className="text-muted">Margin</dt>
        <dd
          className={`text-right ${tooMuchMargin ? "text-loss" : ""}`}
          title={
            tooMuchMargin
              ? `More than the free margin of ${formatMoney(freeMargin ?? 0)} ${currency}, so the order would be refused.`
              : `About what the order takes from the free margin at leverage 1:${instrument.leverage}.`
          }
        >
          {margin === null ? "-" : `${formatMoney(margin)} ${currency}`}
        </dd>
        <dt className="text-muted">1 {one}</dt>
        <dd className="text-right" title={`What a move of one ${one} (${pipSize(digits).toFixed(Math.max(0, digits - 1))}) is worth at this volume`}>
          {pip === null ? "-" : `${formatMoney(pip)} ${currency}`}
        </dd>
      </dl>
      {risk !== null ? (
        <div
          className="flex flex-col gap-1.5"
          title={`Estimated at the stop loss, before commission.${limit ? " The room is how far equity can fall before the limit is broken." : ""}`}
        >
          <p className={riskColors[level]}>{riskText(risk, currency, limit)}</p>
          {limit && (
            <span aria-hidden="true" className="h-1 overflow-hidden rounded-full bg-border">
              <span
                className={`block h-full origin-left rounded-full transition-[width,background-color] duration-300 ease-out-soft ${riskBars[level]}`}
                style={{ width: `${Math.min(100, Math.max(2, shareOfRoom(risk, limit)))}%` }}
              />
            </span>
          )}
        </div>
      ) : wrongSide ? (
        <p className="text-warning">The stop loss is on the wrong side of the price for a {side.toLowerCase()}.</p>
      ) : hasStopLoss ? null : limit && pip ? (
        <p className="text-muted">
          No stop loss: {roomName(limit)} lasts {pipsOfRoom(limit.headroom, pip).toLocaleString("en-US")} {unitName} at this volume.
        </p>
      ) : (
        <p className="text-muted">Set a stop loss to see what the order risks.</p>
      )}
    </section>
  );
}

/** A unit chosen beside a field, small and quiet: lots or a risk, prices, pips or an amount. */
function UnitSelect({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: string;
  options: readonly { value: string; label: string }[];
  onChange: (value: string) => void;
}) {
  return (
    <span className="relative flex items-center">
      <select
        aria-label={label}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="cursor-pointer appearance-none rounded-md bg-transparent py-0.5 pr-5 pl-1.5 text-right text-[11px] font-medium text-muted transition-colors duration-150 outline-none hover:bg-raised hover:text-foreground focus-visible:text-foreground"
      >
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
      <ChevronDownIcon className="pointer-events-none absolute right-1 size-3 text-muted" />
    </span>
  );
}

/** A field with buttons that step it, and what is beside its label, such as its unit. */
function Field({
  label,
  value,
  onChange,
  onStep,
  placeholder,
  compact = false,
  aside,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  onStep: (direction: 1 | -1) => void;
  placeholder?: string;
  compact?: boolean;
  aside?: React.ReactNode;
}) {
  const id = useId();
  const name = label.toLowerCase();
  const buttonClass = `flex shrink-0 items-center justify-center text-muted transition-colors duration-150 hover:bg-foreground/5 hover:text-foreground active:bg-foreground/10 ${compact ? "w-7" : "w-9"}`;
  // Not a wrapping label: it would also label the buttons, and a click on the text would press one.
  return (
    <div className="flex flex-col gap-1">
      <span className="flex min-h-5 items-center justify-between gap-2">
        <label htmlFor={id} className="text-xs text-muted">
          {label}
        </label>
        {aside}
      </span>
      <span className="flex h-9 overflow-hidden rounded-lg border border-border bg-raised transition-colors duration-150 focus-within:border-accent">
        {compact && (
          <button type="button" className={`${buttonClass} border-r border-border`} aria-label={`Lower ${name}`} onClick={() => onStep(-1)}>
            <MinusIcon className="size-3.5" />
          </button>
        )}
        <input
          id={id}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          inputMode="decimal"
          className={`w-full min-w-0 bg-transparent outline-none placeholder:text-muted ${compact ? "text-center" : "px-3"}`}
        />
        {!compact && (
          <button type="button" className={`${buttonClass} border-l border-border`} aria-label={`Lower ${name}`} onClick={() => onStep(-1)}>
            <MinusIcon className="size-3.5" />
          </button>
        )}
        <button type="button" className={`${buttonClass} border-l border-border`} aria-label={`Raise ${name}`} onClick={() => onStep(1)}>
          <PlusIcon className="size-3.5" />
        </button>
      </span>
    </div>
  );
}

/** A choice turned on or off, the same switch as in Settings. */
export function Switch({ checked, onChange, label, title }: { checked: boolean; onChange: (checked: boolean) => void; label: string; title?: string }) {
  const id = useId();
  return (
    <span className="flex w-fit items-center gap-2 text-xs text-muted" title={title}>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        aria-labelledby={id}
        onClick={() => onChange(!checked)}
        className={`relative flex h-4 w-7 shrink-0 items-center rounded-full transition-colors duration-150 ${checked ? "bg-accent" : "bg-border"}`}
      >
        <span
          className={`absolute left-0.5 size-3 rounded-full bg-foreground shadow transition-transform duration-150 ease-out-soft ${checked ? "translate-x-3" : ""}`}
        />
      </button>
      <span id={id} className={checked ? "text-foreground" : ""}>
        {label}
      </span>
    </span>
  );
}

/**
 * The order the trader is asked about, in place of the buy and sell buttons. The question comes first, so the second
 * click of a double click lands on it and not on Confirm. Esc cancels.
 */
export function ConfirmOrder({
  order,
  digits,
  disabled,
  onConfirm,
  onCancel,
}: {
  order: PlaceOrderRequest;
  digits: number;
  disabled: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const question = orderQuestion(order, digits);
  // The question takes the focus once, so a screen reader reads it, and keeps it while prices move.
  useEffect(() => ref.current?.focus(), []);
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onCancel();
      }
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onCancel]);

  return (
    <div
      ref={ref}
      role="alertdialog"
      aria-labelledby={titleId}
      tabIndex={-1}
      className="flex animate-pop flex-col gap-3 rounded-lg border border-border bg-background p-3 outline-none"
    >
      <div className="flex flex-col gap-0.5">
        <p id={titleId} className="font-medium">
          {question.title}?
        </p>
        <p className="text-xs text-muted">{question.stops}</p>
      </div>
      <div className="grid grid-cols-2 gap-2">
        <button
          type="button"
          onClick={onCancel}
          className="rounded-lg bg-raised py-2 font-medium ring-1 ring-border transition duration-150 hover:ring-muted active:translate-y-px"
        >
          Cancel
        </button>
        <button
          type="button"
          onClick={onConfirm}
          disabled={disabled}
          className={`rounded-lg py-2 font-semibold text-background transition duration-150 hover:brightness-110 active:translate-y-px disabled:pointer-events-none disabled:opacity-40 ${order.side === "Buy" ? "bg-buy" : "bg-sell"}`}
        >
          Confirm {order.side.toLowerCase()}
        </button>
      </div>
    </div>
  );
}

/** What the button sends: "Buy" at the market, or a limit or a stop, such as "Buy limit". */
function buttonLabel(side: Side, type: PendingType | "Market"): string {
  return type === "Market" ? side : `${side} ${type.toLowerCase()}`;
}

function TradeButton({
  side,
  label,
  price,
  disabled,
  reason,
  onClick,
  onPoint,
}: {
  side: Side;
  label: string;
  price: string;
  disabled: boolean;
  /** Why the order cannot be sent, shown on hover, also for a side whose reason is not said under the buttons. */
  reason?: string;
  onClick: () => void;
  onPoint: (pointed: boolean) => void;
}) {
  // Dark text, which reads better than white on the green and red, a little calmer than full strength.
  const color = side === "Buy" ? "bg-buy/90" : "bg-sell/90";
  return (
    <button
      type="button"
      onClick={onClick}
      onPointerEnter={() => onPoint(true)}
      onPointerLeave={() => onPoint(false)}
      onFocus={() => onPoint(true)}
      onBlur={() => onPoint(false)}
      disabled={disabled}
      title={disabled ? reason : undefined}
      className={`flex flex-col items-center rounded-lg py-2 text-background shadow-[inset_0_1px_0_rgb(255_255_255/0.2)] transition duration-150 ease-out-soft hover:brightness-110 active:translate-y-px active:brightness-95 disabled:cursor-not-allowed disabled:opacity-40 ${color}`}
    >
      <span className="text-xs font-semibold">{label}</span>
      <span className="live text-base font-semibold">{price}</span>
    </button>
  );
}

/** The distance one pip up or down, never below one, with one decimal. */
function stepPips(input: string, direction: 1 | -1): string {
  const text = input.trim().replace(",", ".");
  const current = /^\d+(\.\d+)?$/.test(text) ? Number(text) : 0;
  return Math.max(1, Math.round(current) + direction).toFixed(0);
}

/**
 * Why the stops are on the wrong side for the side's order, or null: a buy's stop loss below and its take profit above
 * the reference, a sell's the other way. The reference is the pending order's price, or the price a market position
 * closes at.
 */
function wrongSide(side: Side, stops: ResolvedStops, reference: number | undefined, digits: number): string | null {
  if (!stops.ok || reference === undefined) {
    return null;
  }

  const at = formatPrice(reference, digits);
  const below = side === "Buy";
  if (stops.stopLoss !== null && (below ? stops.stopLoss >= reference : stops.stopLoss <= reference)) {
    return `The stop loss must be ${below ? "below" : "above"} ${at} for a ${side.toLowerCase()}.`;
  }

  if (stops.takeProfit !== null && (below ? stops.takeProfit <= reference : stops.takeProfit >= reference)) {
    return `The take profit must be ${below ? "above" : "below"} ${at} for a ${side.toLowerCase()}.`;
  }

  return null;
}

/** How a ticket is sized until the trader chooses: as last chosen, or from the firm's profile (ADR 0058). */
function startSizing(start: { kind: string; value: number | null }): Sizing {
  const stored = readSetting("trading.sizing");
  const sizing = parseSizing(stored);
  if (stored !== null || start.kind !== "RiskOfRoom" || start.value === null) {
    return sizing;
  }

  return { mode: "risk", unit: "room", risks: { ...sizing.risks, room: String(start.value) } };
}
