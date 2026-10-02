"use client";

import { useState } from "react";

import { CommandRejectedError } from "@/lib/api/client";
import type { InstrumentInfo, OrderType, Side } from "@/lib/api/types";
import { formatMoney, formatPrice } from "@/lib/format";
import { parsePrice, parseVolume } from "@/lib/orderInput";
import { usePlaceOrder } from "@/lib/queries";
import { useTradingStore } from "@/lib/store";

const orderTypes: OrderType[] = ["Market", "Limit", "Stop"];

type Status = { kind: "idle" } | { kind: "ok"; text: string } | { kind: "error"; text: string };

export function OrderPanel({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo | null }) {
  const [type, setType] = useState<OrderType>("Market");
  const [volume, setVolume] = useState("1.00");
  const [price, setPrice] = useState("");
  const [stopLoss, setStopLoss] = useState("");
  const [takeProfit, setTakeProfit] = useState("");
  const [status, setStatus] = useState<Status>({ kind: "idle" });

  const quote = useTradingStore((s) => (instrument ? s.prices[instrument.symbol] : undefined));
  const canTrade = useTradingStore((s) => s.connection === "connected" && s.account?.status === "Active");
  const placeOrder = usePlaceOrder(accountId);

  if (!instrument) {
    return <aside className="bg-panel" />;
  }

  const digits = instrument.digits;

  const submit = (side: Side) => {
    const parsedVolume = parseVolume(volume, instrument);
    const parsedPrice = parsePrice(price, digits);
    const parsedStopLoss = parsePrice(stopLoss, digits);
    const parsedTakeProfit = parsePrice(takeProfit, digits);

    const error = !parsedVolume.ok
      ? `Volume must be between ${instrument.volumeMin} and ${instrument.volumeMax} in steps of ${instrument.volumeStep}.`
      : type !== "Market" && (!parsedPrice.ok || parsedPrice.value === null)
        ? `Enter a ${type.toLowerCase()} price with at most ${digits} decimals.`
        : !parsedStopLoss.ok || !parsedTakeProfit.ok
          ? `Stop loss and take profit need at most ${digits} decimals.`
          : null;
    if (error || !parsedVolume.ok || !parsedPrice.ok || !parsedStopLoss.ok || !parsedTakeProfit.ok) {
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
        volume: parsedVolume.value ?? 0,
        price: type === "Market" ? null : parsedPrice.value,
        stopLoss: parsedStopLoss.value,
        takeProfit: parsedTakeProfit.value,
      },
      {
        onSuccess: () => setStatus({ kind: "ok", text: type === "Market" ? `${side} filled.` : `${side} ${type.toLowerCase()} placed.` }),
        onError: (e) => setStatus({ kind: "error", text: e instanceof CommandRejectedError ? `Rejected: ${e.reason}` : "Could not reach the trading service." }),
      },
    );
  };

  const disabled = !canTrade || !quote || placeOrder.isPending;

  return (
    <aside className="flex flex-col gap-3 overflow-y-auto bg-panel p-3 text-sm">
      <div className="font-medium">{instrument.symbol}</div>

      <div className="grid grid-cols-3 gap-1" role="group" aria-label="Order type">
        {orderTypes.map((t) => (
          <button
            key={t}
            type="button"
            onClick={() => setType(t)}
            aria-pressed={t === type}
            className={`rounded px-2 py-1 ${t === type ? "bg-accent/20" : "text-muted hover:text-foreground"}`}
          >
            {t}
          </button>
        ))}
      </div>

      <Field label="Volume (lots)" value={volume} onChange={setVolume} />
      {type !== "Market" && <Field label={`${type} price`} value={price} onChange={setPrice} placeholder={formatPrice(quote?.bid, digits)} />}
      <Field label="Stop loss" value={stopLoss} onChange={setStopLoss} placeholder="None" />
      <Field label="Take profit" value={takeProfit} onChange={setTakeProfit} placeholder="None" />

      <div className="grid grid-cols-2 gap-2">
        <TradeButton side="Sell" price={formatPrice(quote?.bid, digits)} disabled={disabled} onClick={() => submit("Sell")} />
        <TradeButton side="Buy" price={formatPrice(quote?.ask, digits)} disabled={disabled} onClick={() => submit("Buy")} />
      </div>

      {status.kind !== "idle" && (
        <p role="status" className={status.kind === "error" ? "text-loss" : "text-profit"}>
          {status.text}
        </p>
      )}

      <dl className="mt-auto grid grid-cols-2 gap-x-2 gap-y-1 border-t border-border pt-3 text-xs text-muted">
        <dt>Leverage</dt>
        <dd className="text-right">1:{instrument.leverage}</dd>
        <dt>Spread markup</dt>
        <dd className="text-right">{instrument.spreadMarkupPoints} points</dd>
        <dt>Commission</dt>
        <dd className="text-right">{formatMoney(instrument.commissionPerLotPerSide)} per lot and side</dd>
        <dt>Contract</dt>
        <dd className="text-right">{instrument.contractSize.toLocaleString("en-US")} {instrument.baseCurrency}</dd>
      </dl>
    </aside>
  );
}

function Field({ label, value, onChange, placeholder }: { label: string; value: string; onChange: (value: string) => void; placeholder?: string }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-xs text-muted">{label}</span>
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        inputMode="decimal"
        className="rounded border border-border bg-background px-2 py-1.5 font-mono tabular-nums outline-none focus:border-accent"
      />
    </label>
  );
}

function TradeButton({ side, price, disabled, onClick }: { side: Side; price: string; disabled: boolean; onClick: () => void }) {
  const color = side === "Buy" ? "bg-buy" : "bg-sell";
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={`flex flex-col items-center rounded py-2 text-white ${color} disabled:opacity-40`}
    >
      <span className="text-xs uppercase">{side}</span>
      <span className="font-mono tabular-nums">{price}</span>
    </button>
  );
}
