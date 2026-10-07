import type { PlaceOrderRequest } from "./api/types";
import { formatPrice, formatVolume } from "./format";

/**
 * What the order panel asks before it sends an order, when the trader chose so in Settings (ADR 0055): the order, for
 * example "Buy 1.00 EURUSD at market" or "Sell limit 0.50 XAUUSD at 2401.50", and its stops.
 */
export function orderQuestion(order: PlaceOrderRequest, digits: number): { title: string; stops: string } {
  const what = `${formatVolume(order.volume)} ${order.symbol}`;
  const title =
    order.type === "Market" || order.price == null
      ? `${order.side} ${what} at market`
      : `${order.side} ${order.type.toLowerCase()} ${what} at ${formatPrice(order.price, digits)}`;

  const stopLoss = order.stopLoss == null ? null : `${order.trailingStop ? "trailing stop loss" : "stop loss"} ${formatPrice(order.stopLoss, digits)}`;
  const takeProfit = order.takeProfit == null ? null : `take profit ${formatPrice(order.takeProfit, digits)}`;
  const parts = [stopLoss, takeProfit].filter((p): p is string => p !== null);
  const stops = parts.length === 0 ? "No stop loss or take profit." : `With ${parts.join(" and ")}.`;
  return { title, stops };
}
