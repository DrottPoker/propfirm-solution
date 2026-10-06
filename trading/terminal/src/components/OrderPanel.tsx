"use client";

import { useEffect, useId, useState } from "react";

import { suspendedHelp } from "@/lib/account";
import { CommandRejectedError } from "@/lib/api/client";
import type { InstrumentInfo, MarketPeriod, OrderType, PointValue, Side } from "@/lib/api/types";
import { rejectionText } from "@/lib/events";
import { formatMoney, formatPrice, formatSignedMoney, formatUnits, formatVolume, timeZoneName } from "@/lib/format";
import { closingSoon, isClosed, opensText, sessionLines, useNow } from "@/lib/marketHours";
import { ghostLines, sideOfStops, useOrderDraft } from "@/lib/orderDraft";
import { parsePrice, parseVolume, pipSize, stepPrice, stepVolume } from "@/lib/orderInput";
import { estimatedMargin, nearestLimit, pipValue, riskText, shareOfRoom, shareRisk, stopLossRisk } from "@/lib/orderSummary";
import { useMarket, usePlaceOrder, usePointValue } from "@/lib/queries";
import { loadSizing, riskAmount, saveSizing, sizeForRisk, stepRisk, type RiskSize, type RiskUnit, type Sizing } from "@/lib/riskSize";
import { estimatedProfit, resolveStops, stepAmount, type ResolvedStops, type StopKind, type StopUnit } from "@/lib/stops";
import { useTradingStore } from "@/lib/store";
import { useTimeZone } from "@/lib/timeZone";
import { loadVolumes, rememberVolume, startVolume } from "@/lib/volumes";

import { ChevronDownIcon, MinusIcon, PlusIcon } from "./icons";
import { StopUnitToggle } from "./StopUnitToggle";
import { showError } from "./TradeNotices";

const orderTypes: OrderType[] = ["Market", "Limit", "Stop"];
const sides: Side[] = ["Sell", "Buy"];

type StopFields = Record<StopKind, string>;

const noStops: StopFields = { stopLoss: "", takeProfit: "" };

const panelClass = "rounded-xl border border-border bg-panel shadow-card";

/** A size from risk, or why there is none. A prompt asks for something not typed yet, rather than a problem. */
type Sized = RiskSize | { ok: false; reason: string; prompt: true };

export function OrderPanel({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo | null }) {
  // Nothing to preview without a symbol.
  useEffect(() => () => useOrderDraft.getState().setDraft(null, []), []);

  return instrument ? <OrderTicket accountId={accountId} instrument={instrument} /> : <aside className={panelClass} />;
}

function OrderTicket({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo }) {
  const [type, setType] = useState<OrderType>("Market");
  // Each symbol starts from the volume the trader last chose for it on this device, so gold does not start at 1 lot
  // because EURUSD did.
  const [volume, setVolume] = useState(() => startVolume(instrument, loadVolumes()));
  const [volumeSymbol, setVolumeSymbol] = useState(instrument.symbol);
  const [price, setPrice] = useState("");
  const [unit, setUnit] = useState<StopUnit>("price");
  // One set of fields per unit, so switching never reads a price as an amount.
  const [stops, setStops] = useState<Record<StopUnit, StopFields>>({ price: noStops, money: noStops });
  // The side the trader points at or has focused, which the chart preview and the summary follow.
  const [pointedSide, setPointedSide] = useState<Side | null>(null);
  // Kept between orders, like the unit of the stops.
  const [trailing, setTrailing] = useState(false);
  // In lots, or from what the order risks at its stop loss. Kept between orders and on every device (ADR 0052).
  const [sizing, setSizing] = useState(loadSizing);

  if (volumeSymbol !== instrument.symbol) {
    setVolumeSymbol(instrument.symbol);
    setVolume(startVolume(instrument, loadVolumes()));
  }

  const quote = useTradingStore((s) => s.prices[instrument.symbol]);
  const canTrade = useTradingStore((s) => s.connection === "connected" && s.account?.status === "Active");
  const suspended = useTradingStore((s) => s.account?.status === "Suspended");
  const ended = useTradingStore((s) => s.account?.status === "Disabled");
  const accountCurrency = useTradingStore((s) => s.account?.currency);
  const balance = useTradingStore((s) => s.account?.balance);
  const room = useTradingStore((s) => (s.account ? nearestLimit(s.account.floors)?.headroom : undefined));
  const placeOrder = usePlaceOrder(accountId);
  const pointValue = usePointValue(accountId, instrument.symbol).data ?? undefined;
  const market = useMarket(accountId, instrument.symbol);
  const closed = isClosed(market);
  const timeZone = useTimeZone();
  const closing = closingSoon(market, useNow(15_000), timeZone);

  const digits = instrument.digits;
  const riskMode = sizing.mode === "risk";
  const parsedVolume = parseVolume(volume, instrument);
  const typedLots = parsedVolume.ok ? (parsedVolume.value ?? undefined) : undefined;
  const typedPrice = type === "Market" ? null : parsePrice(price, digits);
  const orderPrice = typedPrice?.ok ? (typedPrice.value ?? undefined) : undefined;
  // Sizing from risk needs the stop loss as a price, since an amount would already be the risk.
  const stopUnit: StopUnit = riskMode ? "price" : unit;
  const fields = stops[stopUnit];

  const changeSizing = (next: Sizing) => {
    setSizing(next);
    saveSizing(next);
  };
  const changeRisk = (risk: string) => changeSizing({ ...sizing, risks: { ...sizing.risks, [sizing.unit]: risk } });

  const changeVolume = (next: string) => {
    setVolume(next);
    if (parseVolume(next, instrument).ok) {
      rememberVolume(instrument.symbol, next);
    }
  };

  // Amounts are counted from where the order opens: the ask for a market buy, the bid for a market sell.
  const entryFor = (side: Side) => (type === "Market" ? (side === "Buy" ? quote?.ask : quote?.bid) : orderPrice);

  // The volume that loses the risk at the stop loss, per side, since each side opens at its own price.
  const amount = riskMode ? riskAmount(sizing.risks[sizing.unit], sizing.unit, balance, room) : null;
  const sizeFor = (side: Side): Sized => {
    const stopLoss = parsePrice(stops.price.stopLoss, digits);
    const entry = entryFor(side);
    if (amount === null) {
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

    if (!stopLoss.ok) {
      return { ok: false, reason: `Enter the stop loss with at most ${digits} decimals.` };
    }

    if (stopLoss.value === null) {
      return { ok: false, reason: "Set a stop loss to size the order from the risk.", prompt: true };
    }

    if (entry === undefined || !pointValue) {
      return { ok: false, reason: type === "Market" ? "Waiting for a price." : `Enter a ${type.toLowerCase()} price.`, prompt: true };
    }

    return sizeForRisk({ amount, side, entry, stopLoss: stopLoss.value, digits, pointValuePerLot: pointValue.perLot, limits: instrument });
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

  // What the trader asked for comes back as a note in the corner: the fill from the trading service, or why not.
  const submit = (side: Side) => {
    const resolved = resolve(side);
    const size = riskMode ? sizeFor(side) : null;
    const lots = lotsFor(side);
    const error = size && !size.ok
      ? size.reason
      : !riskMode && !parsedVolume.ok
        ? `Volume must be between ${instrument.volumeMin} and ${instrument.volumeMax} in steps of ${instrument.volumeStep}.`
        : type !== "Market" && orderPrice === undefined
          ? `Enter a ${type.toLowerCase()} price with at most ${digits} decimals.`
          : !resolved.ok
            ? resolved.error
            : trailing && resolved.stopLoss === null
              ? "Set a stop loss for the trailing stop to follow."
              : null;
    if (error || !resolved.ok) {
      showError(error ?? "Invalid order.");
      return;
    }

    placeOrder.mutate(
      {
        orderId: crypto.randomUUID(),
        symbol: instrument.symbol,
        side,
        type,
        volume: lots ?? 0,
        price: type === "Market" ? null : orderPrice,
        stopLoss: resolved.stopLoss,
        takeProfit: resolved.takeProfit,
        trailingStop: trailing,
      },
      {
        onSuccess: () => {
          // The next order starts clean but keeps the volume, the order type and how stops are typed.
          setPrice("");
          setStops({ price: noStops, money: noStops });
        },
        onError: (e) => showError(e instanceof CommandRejectedError ? rejectionText(e.reason) : "Could not reach the trading service."),
      },
    );
  };

  // The other way of seeing the stops, per side: prices for typed amounts, and amounts for typed prices.
  const preview = (side: Side): string[] => {
    const resolved = resolve(side);
    const entry = entryFor(side);
    const lots = lotsFor(side);
    if (!resolved.ok) {
      return [];
    }

    const describe = (label: string, stop: number | null) => {
      if (stop === null) {
        return null;
      }

      if (stopUnit === "money") {
        return `${label} ${formatPrice(stop, digits)}`;
      }

      return entry !== undefined && lots !== undefined && pointValue
        ? `${label} ${formatSignedMoney(estimatedProfit(side, lots, entry, stop, digits, pointValue.perLot))}`
        : null;
    };
    return [describe("SL", resolved.stopLoss), describe("TP", resolved.takeProfit)].filter((line) => line !== null);
  };
  const previews = sides.map(preview);

  // A price step is one pip. An amount step is what one pip is worth at the volume.
  const pipAmount = pointValue && typedLots ? pipValue(typedLots, digits, pointValue.perLot) : 10;
  const startPrice = orderPrice ?? quote?.bid;
  const setStop = (kind: StopKind, value: string) => setStops((s) => ({ ...s, [stopUnit]: { ...s[stopUnit], [kind]: value } }));
  const stepStop = (kind: StopKind, direction: 1 | -1) =>
    setStop(kind, stopUnit === "price" ? stepPrice(fields[kind], direction, digits, startPrice) : stepAmount(fields[kind], direction, pipAmount));

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

  // A level picked on the chart fills in a field as a price, and a removed ghost line empties its field.
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
        } else if (request.price === null) {
          // The ghost shows whichever unit is chosen, and removing it means no stop in either.
          setStops((s) => ({ price: { ...s.price, [request.field]: "" }, money: { ...s.money, [request.field]: "" } }));
        } else {
          setUnit("price");
          setStops((s) => ({ ...s, price: { ...s.price, [request.field]: text } }));
        }
      }),
    [instrument.symbol, instrument.digits],
  );

  const disabled = !canTrade || !quote || closed || placeOrder.isPending;
  // A pending order's margin is counted at its price, a market order's at the middle of bid and ask.
  const marginPrice = orderPrice ?? (quote ? (quote.bid + quote.ask) / 2 : undefined);

  return (
    <aside className={`flex min-h-0 flex-col gap-3.5 overflow-y-auto p-3 text-sm ${panelClass}`}>
      <h2 className="flex items-baseline gap-2">
        <span className="font-semibold">Trade</span>{" "}
        <span className="text-muted">{instrument.symbol}</span>
      </h2>

      <div className="grid grid-cols-3 gap-1 rounded-lg bg-background p-1" role="group" aria-label="Order type">
        {orderTypes.map((t) => (
          <button
            key={t}
            type="button"
            onClick={() => setType(t)}
            aria-pressed={t === type}
            className={`rounded-md px-2 py-1.5 font-medium transition duration-150 ease-out-soft active:translate-y-px ${t === type ? "bg-accent text-accent-foreground shadow-[inset_0_1px_0_rgb(255_255_255/0.2)]" : "text-muted hover:bg-raised hover:text-foreground"}`}
          >
            {t}
          </button>
        ))}
      </div>

      <div className="flex flex-col gap-1.5">
        <SizeToggle sizing={sizing} currency={accountCurrency ?? "Money"} onChange={changeSizing} />
        {riskMode ? (
          <div className="flex flex-col gap-1">
            <Stepper
              label={`Risk (${riskUnitNames(accountCurrency ?? "money")[sizing.unit]})`}
              value={sizing.risks[sizing.unit]}
              onChange={changeRisk}
              placeholder={sizing.unit === "money" ? "Amount" : "Percent"}
              onStep={(direction) => changeRisk(stepRisk(sizing.risks[sizing.unit], direction, sizing.unit))}
            />
            <RiskResult size={sizeFor(ghostSide)} currency={pointValue?.currency ?? accountCurrency ?? ""} />
          </div>
        ) : (
          <div className="flex flex-col gap-1">
            <Stepper
              label="Volume (lots)"
              value={volume}
              onChange={changeVolume}
              onStep={(direction) => changeVolume(stepVolume(volume, direction, instrument))}
            />
            {typedLots !== undefined ? (
              <span className="text-xs text-muted">
                = {formatUnits(typedLots * instrument.contractSize)} {instrument.baseCurrency}
              </span>
            ) : (
              <span className="text-xs text-loss">
                {instrument.volumeMin} to {instrument.volumeMax} lots in steps of {instrument.volumeStep}
              </span>
            )}
          </div>
        )}
      </div>

      {type !== "Market" && (
        <Stepper
          label={`${type} price`}
          value={price}
          onChange={setPrice}
          placeholder={formatPrice(quote?.bid, digits)}
          onStep={(direction) => setPrice(stepPrice(price, direction, digits, quote?.bid))}
        />
      )}

      <div className="flex flex-col gap-2">
        {riskMode ? (
          <span className="text-xs text-muted" title="The volume is worked out from the risk at the stop loss price.">
            Stop loss and take profit as prices
          </span>
        ) : (
          <div className="flex items-center justify-between gap-2">
            <span className="text-xs text-muted">Stop loss and take profit in</span>
            <StopUnitToggle unit={unit} currency={pointValue?.currency ?? accountCurrency ?? "Money"} onChange={setUnit} />
          </div>
        )}
        <div className="grid grid-cols-2 gap-2">
          <Stepper
            label="Stop loss"
            value={fields.stopLoss}
            onChange={(value) => setStop("stopLoss", value)}
            placeholder="None"
            compact
            onStep={(direction) => stepStop("stopLoss", direction)}
          />
          <Stepper
            label="Take profit"
            value={fields.takeProfit}
            onChange={(value) => setStop("takeProfit", value)}
            placeholder="None"
            compact
            onStep={(direction) => stepStop("takeProfit", direction)}
          />
        </div>
        <label
          className="flex w-fit items-center gap-1.5 text-xs text-muted"
          title="The stop loss follows the price at the distance it is set at, and never moves back. The engine moves it, also when the terminal is closed."
        >
          <input type="checkbox" checked={trailing} onChange={(e) => setTrailing(e.target.checked)} className="accent-[var(--accent)]" />
          Trailing stop
        </label>
      </div>

      <div className="flex flex-col gap-1.5">
        {suspended && (
          <p role="note" className="rounded-md bg-warning/10 px-3 py-2 text-xs text-warning">
            {suspendedHelp}
          </p>
        )}
        {ended && (
          <p role="note" className="rounded-md bg-loss/10 px-3 py-2 text-xs text-loss">
            Trading on this account has ended, so no new orders are taken.
          </p>
        )}
        {!ended && closed && market && (
          <p role="note" className="rounded-md bg-raised px-3 py-2 text-xs text-muted">
            The {instrument.symbol} market is closed. {opensText(market, timeZone, timeZoneName(timeZone))}.
          </p>
        )}
        {!ended && closing && (
          <p role="note" className="rounded-md bg-warning/10 px-3 py-2 text-xs text-warning">
            The market closes in {closing.minutes} min, at {closing.at}. Open positions stay open over the close.
          </p>
        )}
        <div className="grid grid-cols-2 gap-2">
          {sides.map((side) => (
            <TradeButton
              key={side}
              side={side}
              price={formatPrice(side === "Buy" ? quote?.ask : quote?.bid, digits)}
              disabled={disabled}
              onClick={() => submit(side)}
              onPoint={(pointed) => setPointedSide(pointed ? side : null)}
            />
          ))}
        </div>
        {previews.some((lines) => lines.length > 0) && (
          <div className="grid grid-cols-2 gap-2 text-center font-mono text-[11px] text-muted tabular-nums" title="Estimated before commission">
            {sides.map((side, i) => (
              <span key={side} className="flex flex-col">
                {previews[i].map((line) => (
                  <span key={line}>{line}</span>
                ))}
              </span>
            ))}
          </div>
        )}
      </div>

      <OrderSummary
        instrument={instrument}
        lots={lotsFor(ghostSide)}
        price={marginPrice}
        pointValue={pointValue}
        side={ghostSide}
        entry={entryFor(ghostSide)}
        stops={resolve(ghostSide)}
      />

      <div className="mt-auto flex flex-col gap-1.5 border-t border-border pt-3 text-xs">
      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1.5">
        <dt className="text-muted">Leverage</dt>
        <dd className="text-right font-mono tabular-nums">1:{instrument.leverage}</dd>
        <dt className="text-muted">Spread markup</dt>
        <dd className="text-right font-mono tabular-nums">{instrument.spreadMarkupPoints} points</dd>
        <dt className="text-muted">Commission</dt>
        <dd className="text-right">
          <span className="font-mono tabular-nums">{formatMoney(instrument.commissionPerLotPerSide)}</span> per lot and side
        </dd>
        <dt className="text-muted">Contract</dt>
        <dd className="text-right">
          <span className="font-mono tabular-nums">{formatUnits(instrument.contractSize)}</span> {instrument.baseCurrency}
        </dd>
      </dl>
      <TradingHours sessions={market?.sessions ?? null} known={market !== undefined} closed={closed} timeZone={timeZone} />
      </div>
    </aside>
  );
}

/**
 * Whether the market is open, and the periods it is open this week in the account's time zone, behind a click. A
 * market that never closes, like crypto, says so.
 */
function TradingHours({ sessions, known, closed, timeZone }: { sessions: MarketPeriod[] | null; known: boolean; closed: boolean; timeZone: string }) {
  const state = !known ? "-" : sessions === null ? "Around the clock" : closed ? "Closed" : "Open";
  if (sessions === null || sessions.length === 0) {
    return (
      <p className="flex justify-between gap-3">
        <span className="text-muted">Trading hours</span>
        <span>{state}</span>
      </p>
    );
  }

  return (
    <details className="group">
      <summary className="flex cursor-pointer list-none justify-between gap-3 [&::-webkit-details-marker]:hidden">
        <span className="text-muted">Trading hours</span>
        <span className="flex items-center gap-1">
          {state}
          <ChevronDownIcon className="size-3 text-muted transition-transform duration-150 group-open:rotate-180" />
        </span>
      </summary>
      <ul className="mt-1.5 flex flex-col gap-0.5 text-right font-mono text-[11px] text-muted tabular-nums" aria-label={`Open periods, ${timeZoneName(timeZone)}`}>
        {sessionLines(sessions, timeZone).map((line) => (
          <li key={line}>{line}</li>
        ))}
      </ul>
    </details>
  );
}

/** What the risk is typed in, for the field's label. */
function riskUnitNames(currency: string): Record<RiskUnit, string> {
  return { money: currency, balance: "% of balance", room: "% of room" };
}

/**
 * Chooses how the order is sized: in lots, or from what it risks at its stop loss, as an amount, a percent of the
 * balance or a percent of the room left to the nearest loss limit.
 */
function SizeToggle({ sizing, currency, onChange }: { sizing: Sizing; currency: string; onChange: (sizing: Sizing) => void }) {
  const selected = sizing.mode === "lots" ? "lots" : sizing.unit;
  const options: { id: "lots" | RiskUnit; label: string; title: string }[] = [
    { id: "lots", label: "Lots", title: "Type the volume in lots" },
    { id: "money", label: currency, title: `Risk an amount in ${currency} at the stop loss` },
    { id: "balance", label: "% balance", title: "Risk a percent of the balance at the stop loss" },
    { id: "room", label: "% room", title: "Risk a percent of the room left to the nearest loss limit, usually today's" },
  ];
  return (
    <div className="flex items-center justify-between gap-2">
      <span className="text-xs text-muted">Size in</span>
      <div role="group" aria-label="Size in" className="flex shrink-0 rounded-lg bg-background p-0.5 text-[11px]">
        {options.map((o) => (
          <button
            key={o.id}
            type="button"
            aria-pressed={o.id === selected}
            title={o.title}
            onClick={() => onChange(o.id === "lots" ? { ...sizing, mode: "lots" } : { ...sizing, mode: "risk", unit: o.id })}
            className={`rounded-md px-2 py-0.5 font-medium whitespace-nowrap transition duration-150 active:translate-y-px ${o.id === selected ? "bg-raised text-foreground shadow-card" : "text-muted hover:text-foreground"}`}
          >
            {o.label}
          </button>
        ))}
      </div>
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
      = <span className="font-mono text-foreground tabular-nums">{formatVolume(size.lots)}</span> lots, risks{" "}
      <span className="font-mono tabular-nums">
        {formatMoney(size.risk)} {currency}
      </span>
      {size.atMost && ", the largest volume allowed"}
    </span>
  );
}

const riskColors = { ok: "text-foreground", warning: "text-warning", danger: "text-loss" };
const riskBars = { ok: "bg-accent", warning: "bg-warning", danger: "bg-loss" };

/**
 * What the order means before it is placed: the margin it takes from the free margin, what a pip is worth at the
 * volume, and with a stop loss what the order risks, also as a share of the room left to the nearest loss limit.
 */
function OrderSummary({
  instrument,
  lots,
  price,
  pointValue,
  side,
  entry,
  stops,
}: {
  instrument: InstrumentInfo;
  lots: number | undefined;
  price: number | undefined;
  pointValue: PointValue | undefined;
  side: Side;
  entry: number | undefined;
  stops: ResolvedStops;
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
    ready && entry !== undefined && stops.ok && stops.stopLoss !== null
      ? stopLossRisk(side, lots, entry, stops.stopLoss, digits, pointValue.perLot)
      : null;
  // A stop loss on the winning side would be refused, so it says so rather than ask for one.
  const wrongSide = ready && entry !== undefined && stops.ok && stops.stopLoss !== null && risk === null;
  const limit = nearestLimit(floors ?? []);
  const level = risk !== null && limit ? shareRisk(shareOfRoom(risk, limit)) : "ok";

  return (
    <section aria-label="What this order means" className="flex flex-col gap-2 rounded-lg border border-border bg-background/50 px-3 py-2.5 text-xs">
      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
        <dt className="text-muted">Margin</dt>
        <dd
          className={`text-right font-mono tabular-nums ${tooMuchMargin ? "text-loss" : ""}`}
          title={
            tooMuchMargin
              ? `More than the free margin of ${formatMoney(freeMargin ?? 0)} ${currency}, so the order would be refused.`
              : `About what the order takes from the free margin at leverage 1:${instrument.leverage}. The trading service counts it when the order fills.`
          }
        >
          {margin === null ? "-" : `${formatMoney(margin)} ${currency}`}
        </dd>
        <dt className="text-muted">Pip value</dt>
        <dd className="text-right font-mono tabular-nums" title={`What a move of one pip (${pipSize(digits).toFixed(Math.max(0, digits - 1))}) is worth at this volume`}>
          {pip === null ? "-" : `${formatMoney(pip)} ${currency}`}
        </dd>
      </dl>
      {risk !== null ? (
        <div
          className="flex flex-col gap-1.5 border-t border-border pt-2"
          title={`Estimated at the stop loss, before commission. ${limit ? "The room is how far equity can fall before the limit is broken." : ""}`.trim()}
        >
          <p className={riskColors[level]}>{riskText(risk, currency, limit)}</p>
          {limit && (
            <span aria-hidden="true" className="h-1 overflow-hidden rounded-full bg-border">
              <span
                className={`block h-full origin-left animate-grow-x rounded-full transition-[width,background-color] duration-300 ease-out-soft ${riskBars[level]}`}
                style={{ width: `${Math.min(100, Math.max(2, shareOfRoom(risk, limit)))}%` }}
              />
            </span>
          )}
        </div>
      ) : wrongSide ? (
        <p className="border-t border-border pt-2 text-warning">The stop loss is on the wrong side of the price for a {side.toLowerCase()}.</p>
      ) : (
        <p className="border-t border-border pt-2 text-muted">Set a stop loss to see what the order risks.</p>
      )}
    </section>
  );
}

function Stepper({
  label,
  value,
  onChange,
  onStep,
  placeholder,
  compact = false,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  onStep: (direction: 1 | -1) => void;
  placeholder?: string;
  compact?: boolean;
}) {
  const id = useId();
  // "Volume (lots)" gives "Lower volume".
  const name = label.replace(/\s*\(.*\)$/, "").toLowerCase();
  const buttonClass = `flex shrink-0 items-center justify-center text-muted transition-colors duration-150 hover:bg-foreground/5 hover:text-foreground active:bg-foreground/10 ${compact ? "w-7" : "w-9"}`;
  // Not a wrapping label: it would also label the buttons, and a click on the text would press one.
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="text-xs text-muted">
        {label}
      </label>
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
          className={`w-full min-w-0 bg-transparent font-mono tabular-nums outline-none placeholder:text-muted ${compact ? "text-center" : "px-3"}`}
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

function TradeButton({
  side,
  price,
  disabled,
  onClick,
  onPoint,
}: {
  side: Side;
  price: string;
  disabled: boolean;
  onClick: () => void;
  onPoint: (pointed: boolean) => void;
}) {
  // Dark text, which reads better than white on the bright green and red.
  const color = side === "Buy" ? "bg-buy" : "bg-sell";
  return (
    <button
      type="button"
      onClick={onClick}
      onPointerEnter={() => onPoint(true)}
      onPointerLeave={() => onPoint(false)}
      onFocus={() => onPoint(true)}
      onBlur={() => onPoint(false)}
      disabled={disabled}
      className={`flex flex-col items-center rounded-lg py-2.5 text-background shadow-[inset_0_1px_0_rgb(255_255_255/0.25),0_1px_2px_rgb(0_0_0/0.3)] transition duration-150 ease-out-soft hover:brightness-110 active:translate-y-px active:brightness-95 disabled:pointer-events-none disabled:opacity-40 ${color}`}
    >
      <span className="text-xs font-semibold tracking-wide uppercase">{side}</span>{" "}
      <span className="font-mono text-base font-semibold tabular-nums">{price}</span>
    </button>
  );
}
