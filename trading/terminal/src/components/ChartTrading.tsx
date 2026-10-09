"use client";

import { useCallback, useState } from "react";

import { CommandRejectedError } from "@/lib/api/client";
import type { InstrumentInfo, PlaceOrderRequest, Side } from "@/lib/api/types";
import { rejectionText } from "@/lib/events";
import { formatPrice } from "@/lib/format";
import { orderBlockText, useOrderBlock } from "@/lib/orderBlock";
import { parseVolume, stepVolume } from "@/lib/orderInput";
import { usePlaceOrder } from "@/lib/queries";
import { useTradingStore } from "@/lib/store";
import { changeVolume, useConfirmOrders, useVolume } from "@/lib/ticketStore";

import { ConfirmOrder } from "./OrderPanel";
import type { MenuPlacement } from "./PriceMenu";
import { showError } from "./TradeNotices";

/** An order from the chart waiting for the trader's yes, and where on the chart the question shows. */
interface Asking {
  order: PlaceOrderRequest;
  /** Where the right click was, or null for under the chart's buttons. */
  at: MenuPlacement | null;
}

/**
 * Orders placed from the chart (ADR 0058), from its buy and sell buttons or its right-click menu. They ask first when
 * the order panel does, so every order follows the same choice in Settings or the firm's profile.
 */
export function useChartOrders(accountId: string) {
  const placeOrder = usePlaceOrder(accountId);
  const confirmOrders = useConfirmOrders();
  const [asking, setAsking] = useState<Asking | null>(null);
  const cancel = useCallback(() => setAsking(null), []);

  const send = (order: PlaceOrderRequest) =>
    placeOrder.mutate(order, {
      onError: (e) => showError(e instanceof CommandRejectedError ? rejectionText(e.reason) : "Could not reach the trading service."),
    });

  return {
    asking,
    pending: placeOrder.isPending,
    place: (order: PlaceOrderRequest, at: Asking["at"] = null) => (confirmOrders ? setAsking({ order, at }) : send(order)),
    confirm: () => {
      if (asking) {
        send(asking.order);
      }
      setAsking(null);
    },
    cancel,
  };
}

/** A market order without stops at the volume, as the chart's buttons and the order panel's volume send it. */
export function marketOrder(symbol: string, side: Side, volume: number): PlaceOrderRequest {
  return { orderId: crypto.randomUUID(), symbol, side, type: "Market", volume, price: null, stopLoss: null, takeProfit: null, trailingStop: false };
}

/**
 * Sell and buy buttons in the chart's corner with the volume between them, the same volume as the order panel's
 * (ADR 0058). They send market orders without stops, so the trader can trade from the chart and in full screen; stops
 * are set afterwards by dragging on the chart. Turned on in Settings.
 */
export function QuickTrade({ accountId, instrument, onOrder }: { accountId: string; instrument: InstrumentInfo; onOrder: (order: PlaceOrderRequest) => void }) {
  const { symbol, digits } = instrument;
  const quote = useTradingStore((s) => s.prices[symbol]);
  const volume = useVolume(instrument);
  const parsed = parseVolume(volume, instrument);
  const lots = parsed.ok ? parsed.value : null;
  const { block } = useOrderBlock(accountId, symbol);
  const volumeWrong = lots === null;
  const reason = block
    ? orderBlockText(block, symbol)
    : volumeWrong
      ? `The volume goes from ${instrument.volumeMin} to ${instrument.volumeMax} lots in steps of ${instrument.volumeStep}.`
      : null;

  const button = (side: Side) => (
    <button
      type="button"
      disabled={reason !== null}
      onClick={() => lots !== null && onOrder(marketOrder(symbol, side, lots))}
      title={reason ?? `${side} ${volume} lots at the market, without stops`}
      className={`flex min-w-[4.75rem] flex-col px-2.5 py-1 text-background transition duration-150 hover:brightness-110 active:brightness-95 disabled:cursor-not-allowed disabled:opacity-40 ${side === "Buy" ? "items-end bg-buy/90" : "items-start bg-sell/90"}`}
    >
      <span className="text-[10px] leading-tight font-semibold">{side}</span>
      <span className="live text-[13px] leading-tight font-semibold tabular-nums">{formatPrice(side === "Buy" ? quote?.ask : quote?.bid, digits)}</span>
    </button>
  );

  return (
    <div role="group" aria-label="Quick trade" className="pointer-events-auto flex w-fit items-stretch overflow-hidden rounded-md shadow-card">
      {button("Sell")}
      <input
        value={volume}
        onChange={(e) => changeVolume(instrument, e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "ArrowUp" || e.key === "ArrowDown") {
            e.preventDefault();
            changeVolume(instrument, stepVolume(volume, e.key === "ArrowUp" ? 1 : -1, instrument));
          }
        }}
        inputMode="decimal"
        aria-label="Volume in lots"
        aria-invalid={volumeWrong}
        title="Volume in lots, the same as in the order panel. Arrow keys step it."
        className={`w-14 bg-panel text-center text-xs font-medium tabular-nums outline-none focus:bg-raised ${volumeWrong ? "text-loss" : ""}`}
      />
      {button("Buy")}
    </div>
  );
}

/**
 * Sell and buy under the chart on a phone (ADR 0058), at the order panel's volume, so a trade needs no trip to the
 * Trade tab. Market orders without stops, which ask first when orders do; the question takes the bar's place.
 */
export function PhoneTradeBar({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo }) {
  const { symbol, digits } = instrument;
  const quote = useTradingStore((s) => s.prices[symbol]);
  const volume = useVolume(instrument);
  const parsed = parseVolume(volume, instrument);
  const lots = parsed.ok ? parsed.value : null;
  const { block } = useOrderBlock(accountId, symbol);
  const orders = useChartOrders(accountId);
  const reason = block ? orderBlockText(block, symbol) : lots === null ? "Set a valid volume in the Trade tab." : null;

  if (orders.asking) {
    return (
      <div className="p-2">
        <ConfirmOrder
          order={orders.asking.order}
          digits={digits}
          disabled={block !== null || orders.pending}
          onConfirm={orders.confirm}
          onCancel={orders.cancel}
        />
      </div>
    );
  }

  const button = (side: Side) => (
    <button
      type="button"
      disabled={reason !== null}
      onClick={() => lots !== null && orders.place(marketOrder(symbol, side, lots))}
      className={`flex flex-col items-center rounded-lg py-1.5 text-background transition duration-150 active:translate-y-px disabled:opacity-40 ${side === "Buy" ? "bg-buy/90" : "bg-sell/90"}`}
    >
      <span className="text-[11px] font-semibold">{side}</span>
      <span className="live text-sm font-semibold tabular-nums">{formatPrice(side === "Buy" ? quote?.ask : quote?.bid, digits)}</span>
    </button>
  );

  return (
    <div className="flex flex-col gap-1 p-2">
      <div role="group" aria-label="Trade from the chart" className="grid grid-cols-[1fr_5rem_1fr] items-stretch gap-2">
        {button("Sell")}
        <input
          value={volume}
          onChange={(e) => changeVolume(instrument, e.target.value)}
          inputMode="decimal"
          aria-label="Volume in lots"
          aria-invalid={lots === null}
          className={`rounded-lg border border-border bg-background text-center text-sm font-medium tabular-nums outline-none focus:border-accent ${lots === null ? "text-loss" : ""}`}
        />
        {button("Buy")}
      </div>
      {reason && <p className="text-center text-[11px] text-muted">{reason}</p>}
    </div>
  );
}

/** The question for an order from the chart, floating where it was asked for. */
export function ChartOrderQuestion({
  asking,
  digits,
  disabled,
  onConfirm,
  onCancel,
}: {
  asking: Asking;
  digits: number;
  disabled: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  return (
    <div
      className="absolute z-30 w-72 rounded-lg shadow-float"
      style={
        asking.at
          ? { left: asking.at.x, top: asking.at.y, translate: `${asking.at.flipX ? "-100%" : "0"} ${asking.at.flipY ? "-100%" : "0"}` }
          : { left: "0.625rem", top: "3.25rem" }
      }
    >
      <ConfirmOrder order={asking.order} digits={digits} disabled={disabled} onConfirm={onConfirm} onCancel={onCancel} />
    </div>
  );
}
