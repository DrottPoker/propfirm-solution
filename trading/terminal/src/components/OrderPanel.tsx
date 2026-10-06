"use client";

import { useEffect, useId, useState } from "react";

import { suspendedHelp } from "@/lib/account";
import { CommandRejectedError } from "@/lib/api/client";
import type { InstrumentInfo, OrderType, PointValue, Side } from "@/lib/api/types";
import { rejectionText } from "@/lib/events";
import { formatMoney, formatPrice, formatSignedMoney, formatUnits } from "@/lib/format";
import { ghostLines, sideOfStops, useOrderDraft } from "@/lib/orderDraft";
import { parsePrice, parseVolume, pipSize, stepPrice, stepVolume } from "@/lib/orderInput";
import { estimatedMargin, nearestLimit, pipValue, riskText, shareOfRoom, shareRisk, stopLossRisk } from "@/lib/orderSummary";
import { usePlaceOrder, usePointValue } from "@/lib/queries";
import { estimatedProfit, resolveStops, stepAmount, type ResolvedStops, type StopKind, type StopUnit } from "@/lib/stops";
import { useTradingStore } from "@/lib/store";
import { loadVolumes, rememberVolume, startVolume } from "@/lib/volumes";

import { MinusIcon, PlusIcon } from "./icons";
import { StopUnitToggle } from "./StopUnitToggle";
import { showError } from "./TradeNotices";

const orderTypes: OrderType[] = ["Market", "Limit", "Stop"];
const sides: Side[] = ["Sell", "Buy"];

type StopFields = Record<StopKind, string>;

const noStops: StopFields = { stopLoss: "", takeProfit: "" };

const panelClass = "rounded-xl border border-border bg-panel shadow-card";

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

  if (volumeSymbol !== instrument.symbol) {
    setVolumeSymbol(instrument.symbol);
    setVolume(startVolume(instrument, loadVolumes()));
  }

  const quote = useTradingStore((s) => s.prices[instrument.symbol]);
  const canTrade = useTradingStore((s) => s.connection === "connected" && s.account?.status === "Active");
  const suspended = useTradingStore((s) => s.account?.status === "Suspended");
  const ended = useTradingStore((s) => s.account?.status === "Disabled");
  const accountCurrency = useTradingStore((s) => s.account?.currency);
  const placeOrder = usePlaceOrder(accountId);
  const pointValue = usePointValue(accountId, instrument.symbol).data ?? undefined;

  const digits = instrument.digits;
  const parsedVolume = parseVolume(volume, instrument);
  const lots = parsedVolume.ok ? (parsedVolume.value ?? undefined) : undefined;
  const typedPrice = type === "Market" ? null : parsePrice(price, digits);
  const orderPrice = typedPrice?.ok ? (typedPrice.value ?? undefined) : undefined;
  const fields = stops[unit];

  const changeVolume = (next: string) => {
    setVolume(next);
    if (parseVolume(next, instrument).ok) {
      rememberVolume(instrument.symbol, next);
    }
  };

  // Amounts are counted from where the order opens: the ask for a market buy, the bid for a market sell.
  const entryFor = (side: Side) => (type === "Market" ? (side === "Buy" ? quote?.ask : quote?.bid) : orderPrice);
  const resolve = (side: Side) =>
    resolveStops(unit, fields.stopLoss, fields.takeProfit, side, entryFor(side), lots ?? 0, digits, pointValue?.perLot);

  // What the trader asked for comes back as a note in the corner: the fill from the trading service, or why not.
  const submit = (side: Side) => {
    const resolved = resolve(side);
    const error = !parsedVolume.ok
      ? `Volume must be between ${instrument.volumeMin} and ${instrument.volumeMax} in steps of ${instrument.volumeStep}.`
      : type !== "Market" && orderPrice === undefined
        ? `Enter a ${type.toLowerCase()} price with at most ${digits} decimals.`
        : !resolved.ok
          ? resolved.error
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
    if (!resolved.ok) {
      return [];
    }

    const describe = (label: string, stop: number | null) => {
      if (stop === null) {
        return null;
      }

      if (unit === "money") {
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
  const pipAmount = pointValue && lots ? pipValue(lots, digits, pointValue.perLot) : 10;
  const startPrice = orderPrice ?? quote?.bid;
  const setStop = (kind: StopKind, value: string) => setStops((s) => ({ ...s, [unit]: { ...s[unit], [kind]: value } }));
  const stepStop = (kind: StopKind, direction: 1 | -1) =>
    setStop(kind, unit === "price" ? stepPrice(fields[kind], direction, digits, startPrice) : stepAmount(fields[kind], direction, pipAmount));

  // Typed prices fit one side at most, and amounts fit both. Without a side to go by, the chart shows a buy.
  const typedStops = resolveStops("price", stops.price.stopLoss, stops.price.takeProfit, "Buy", undefined, 0, digits, undefined);
  const fittingSide =
    unit === "price" && typedStops.ok && quote
      ? sideOfStops(typedStops.stopLoss, typedStops.takeProfit, orderPrice ?? quote.bid, orderPrice ?? quote.ask)
      : null;
  const ghostSide = pointedSide ?? fittingSide ?? "Buy";
  const ghostKey = JSON.stringify(
    ghostLines({
      type,
      side: ghostSide,
      entry: entryFor(ghostSide),
      stops: resolve(ghostSide),
      lots,
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

  const disabled = !canTrade || !quote || placeOrder.isPending;
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

      <div className="flex flex-col gap-1">
        <Stepper
          label="Volume (lots)"
          value={volume}
          onChange={changeVolume}
          onStep={(direction) => changeVolume(stepVolume(volume, direction, instrument))}
        />
        {lots !== undefined ? (
          <span className="text-xs text-muted">
            = {formatUnits(lots * instrument.contractSize)} {instrument.baseCurrency}
          </span>
        ) : (
          <span className="text-xs text-loss">
            {instrument.volumeMin} to {instrument.volumeMax} lots in steps of {instrument.volumeStep}
          </span>
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
        <div className="flex items-center justify-between gap-2">
          <span className="text-xs text-muted">Stop loss and take profit in</span>
          <StopUnitToggle unit={unit} currency={pointValue?.currency ?? accountCurrency ?? "Money"} onChange={setUnit} />
        </div>
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
        lots={lots}
        price={marginPrice}
        pointValue={pointValue}
        side={ghostSide}
        entry={entryFor(ghostSide)}
        stops={resolve(ghostSide)}
      />

      <dl className="mt-auto grid grid-cols-[auto_1fr] gap-x-3 gap-y-1.5 border-t border-border pt-3 text-xs">
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
    </aside>
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
