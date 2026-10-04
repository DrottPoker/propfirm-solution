"use client";

import { useEffect, useId, useState } from "react";

import { suspendedHelp } from "@/lib/account";
import { CommandRejectedError } from "@/lib/api/client";
import type { InstrumentInfo, OrderType, Side } from "@/lib/api/types";
import { rejectionText } from "@/lib/events";
import { formatMoney, formatPrice, formatSignedMoney, formatUnits } from "@/lib/format";
import { ghostLines, sideOfStops, useOrderDraft } from "@/lib/orderDraft";
import { parsePrice, parseVolume, pipSize, stepPrice, stepVolume } from "@/lib/orderInput";
import { usePlaceOrder, usePointValue } from "@/lib/queries";
import { estimatedProfit, resolveStops, stepAmount, type StopKind, type StopUnit } from "@/lib/stops";
import { useTradingStore } from "@/lib/store";

import { MinusIcon, PlusIcon } from "./icons";
import { StopUnitToggle } from "./StopUnitToggle";

const orderTypes: OrderType[] = ["Market", "Limit", "Stop"];
const sides: Side[] = ["Sell", "Buy"];

type Status = { kind: "idle" } | { kind: "ok"; text: string } | { kind: "error"; text: string };
type StopFields = Record<StopKind, string>;

const noStops: StopFields = { stopLoss: "", takeProfit: "" };

export function OrderPanel({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo | null }) {
  // Nothing to preview without a symbol.
  useEffect(() => () => useOrderDraft.getState().setDraft(null, []), []);

  return instrument ? <OrderTicket accountId={accountId} instrument={instrument} /> : <aside className="rounded-lg border border-border bg-panel" />;
}

function OrderTicket({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo }) {
  const [type, setType] = useState<OrderType>("Market");
  const [volume, setVolume] = useState("1.00");
  const [price, setPrice] = useState("");
  const [unit, setUnit] = useState<StopUnit>("price");
  // One set of fields per unit, so switching never reads a price as an amount.
  const [stops, setStops] = useState<Record<StopUnit, StopFields>>({ price: noStops, money: noStops });
  const [status, setStatus] = useState<Status>({ kind: "idle" });
  // The side the trader points at or has focused, which the chart preview follows.
  const [pointedSide, setPointedSide] = useState<Side | null>(null);

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

  // Amounts are counted from where the order opens: the ask for a market buy, the bid for a market sell.
  const entryFor = (side: Side) => (type === "Market" ? (side === "Buy" ? quote?.ask : quote?.bid) : orderPrice);
  const resolve = (side: Side) =>
    resolveStops(unit, fields.stopLoss, fields.takeProfit, side, entryFor(side), lots ?? 0, digits, pointValue?.perLot);

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
      setStatus({ kind: "error", text: error ?? "Invalid order." });
      return;
    }

    setStatus({ kind: "idle" });
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
          setStatus({ kind: "ok", text: type === "Market" ? `${side} filled.` : `${side} ${type.toLowerCase()} placed.` });
          // The next order starts clean but keeps the volume, the order type and how stops are typed.
          setPrice("");
          setStops({ price: noStops, money: noStops });
        },
        onError: (e) => setStatus({ kind: "error", text: e instanceof CommandRejectedError ? rejectionText(e.reason) : "Could not reach the trading service." }),
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
  const pipAmount = pointValue && lots ? pipSize(digits) * 10 ** digits * lots * pointValue.perLot : 10;
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

  return (
    <aside className="flex min-h-0 flex-col gap-4 overflow-y-auto rounded-lg border border-border bg-panel p-3 text-sm">
      <h2 className="flex items-baseline gap-2">
        <span className="font-semibold">Trade</span>{" "}
        <span className="text-muted">{instrument.symbol}</span>
      </h2>

      <div className="grid grid-cols-3 gap-1 rounded-md bg-background p-1" role="group" aria-label="Order type">
        {orderTypes.map((t) => (
          <button
            key={t}
            type="button"
            onClick={() => setType(t)}
            aria-pressed={t === type}
            className={`rounded px-2 py-1.5 font-medium ${t === type ? "bg-accent text-white" : "text-muted hover:text-foreground"}`}
          >
            {t}
          </button>
        ))}
      </div>

      <div className="flex flex-col gap-1">
        <Stepper
          label="Volume (lots)"
          value={volume}
          onChange={setVolume}
          onStep={(direction) => setVolume(stepVolume(volume, direction, instrument))}
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

      {status.kind !== "idle" && !ended && (
        <p role="status" className={`rounded-md px-3 py-2 ${status.kind === "error" ? "bg-loss/10 text-loss" : "bg-profit/10 text-profit"}`}>
          {status.text}
        </p>
      )}

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
  const buttonClass = `flex shrink-0 items-center justify-center text-muted hover:bg-white/5 hover:text-foreground ${compact ? "w-7" : "w-9"}`;
  // Not a wrapping label: it would also label the buttons, and a click on the text would press one.
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="text-xs text-muted">
        {label}
      </label>
      <span className="flex h-9 overflow-hidden rounded-md border border-border bg-raised focus-within:border-accent">
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
  const color = side === "Buy" ? "bg-buy hover:bg-buy/90" : "bg-sell hover:bg-sell/90";
  return (
    <button
      type="button"
      onClick={onClick}
      onPointerEnter={() => onPoint(true)}
      onPointerLeave={() => onPoint(false)}
      onFocus={() => onPoint(true)}
      onBlur={() => onPoint(false)}
      disabled={disabled}
      className={`flex flex-col items-center rounded-md py-2.5 text-white transition ${color} disabled:opacity-40`}
    >
      <span className="text-xs font-semibold tracking-wide uppercase">{side}</span>{" "}
      <span className="font-mono text-base font-semibold tabular-nums">{price}</span>
    </button>
  );
}
