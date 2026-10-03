import type { Side } from "./api/types";
import { parsePrice } from "./orderInput";

// Amounts here are estimates for planning a stop loss or take profit. The engine values the position when it
// closes, at the conversion rate of that moment, and commission comes on top.

export type StopKind = "stopLoss" | "takeProfit";

/**
 * Estimated profit in account currency of a move from one price to another, before commission. The point value
 * comes from the engine: what one point on one lot is worth in the account currency.
 */
export function estimatedProfit(side: Side, volume: number, from: number, to: number, digits: number, pointValuePerLot: number): number {
  const scale = 10 ** digits;
  const points = Math.round(to * scale) - Math.round(from * scale);
  return (side === "Buy" ? points : -points) * volume * pointValuePerLot;
}

/**
 * The stop loss or take profit where the trade loses or wins about the amount, counted from the entry price. The
 * distance is rounded down to whole points, so the amount is never exceeded. Null when the amount is less than a
 * point or the price would not be positive.
 */
export function priceForAmount(
  kind: StopKind,
  side: Side,
  amount: number,
  volume: number,
  entry: number,
  digits: number,
  pointValuePerLot: number,
): number | null {
  if (!(amount > 0 && volume > 0 && pointValuePerLot > 0)) {
    return null;
  }

  // The small epsilon keeps 100 / 0.1 from becoming 999.99... points.
  const points = Math.floor(amount / (volume * pointValuePerLot) + 1e-9);
  if (points < 1) {
    return null;
  }

  const scale = 10 ** digits;
  const price = Math.round(entry * scale) + (isAbove(kind, side) ? points : -points);
  return price > 0 ? price / scale : null;
}

/**
 * Keeps a dragged stop on the side the engine accepts: at least one point from the price the position closes at,
 * the bid for a buy and the ask for a sell.
 */
export function clampStop(kind: StopKind, side: Side, price: number, closePrice: number, digits: number): number {
  const scale = 10 ** digits;
  const wanted = Math.round(price * scale);
  const reference = Math.round(closePrice * scale);
  const clamped = isAbove(kind, side) ? Math.max(wanted, reference + 1) : Math.min(wanted, reference - 1);
  return Math.max(1, clamped) / scale;
}

/**
 * What a level can be for an open position: a stop loss on the losing side of the price it closes at, the bid for a
 * buy and the ask for a sell, and a take profit on the winning side. Null at the price itself.
 */
export function stopKindAt(side: Side, price: number, closePrice: number, digits: number): StopKind | null {
  const scale = 10 ** digits;
  const level = Math.round(price * scale);
  const reference = Math.round(closePrice * scale);
  if (level === reference) {
    return null;
  }

  return (level > reference) === (side === "Buy") ? "takeProfit" : "stopLoss";
}

/** The amount one step up or down, never below one step. Empty or invalid input starts at one step. */
export function stepAmount(input: string, direction: 1 | -1, step: number): string {
  const cents = Math.max(1, Math.round(step * 100));
  const text = input.trim().replace(",", ".");
  const current = /^\d+(\.\d+)?$/.test(text) ? Math.round(Number(text) * 100) : 0;
  const next = current === 0 ? cents : Math.max(cents, Math.round(current / cents) * cents + direction * cents);
  return (next / 100).toFixed(2);
}

/** Whether the trader types stop loss and take profit as prices or as amounts in the account currency. */
export type StopUnit = "price" | "money";

export type ResolvedStops = { ok: true; stopLoss: number | null; takeProfit: number | null } | { ok: false; error: string };

/**
 * Turns the typed stop loss and take profit into prices for one side. Amounts are counted from the entry price:
 * the ask for a market buy, the bid for a market sell, or the order price of a pending order.
 */
export function resolveStops(
  unit: StopUnit,
  stopLoss: string,
  takeProfit: string,
  side: Side,
  entry: number | undefined,
  volume: number,
  digits: number,
  pointValuePerLot: number | undefined,
): ResolvedStops {
  if (unit === "price") {
    const sl = parsePrice(stopLoss, digits);
    const tp = parsePrice(takeProfit, digits);
    return sl.ok && tp.ok
      ? { ok: true, stopLoss: sl.value, takeProfit: tp.value }
      : { ok: false, error: `Stop loss and take profit need at most ${digits} decimals.` };
  }

  const sl = parsePrice(stopLoss, 2);
  const tp = parsePrice(takeProfit, 2);
  if (!sl.ok || !tp.ok) {
    return { ok: false, error: "Enter the amounts as positive numbers with at most 2 decimals." };
  }

  if (sl.value === null && tp.value === null) {
    return { ok: true, stopLoss: null, takeProfit: null };
  }

  if (entry === undefined || pointValuePerLot === undefined) {
    return { ok: false, error: "The amounts cannot be turned into prices yet. Try again in a moment." };
  }

  const toPrice = (kind: StopKind, amount: number | null) =>
    amount === null ? null : priceForAmount(kind, side, amount, volume, entry, digits, pointValuePerLot);
  const slPrice = toPrice("stopLoss", sl.value);
  const tpPrice = toPrice("takeProfit", tp.value);
  if ((sl.value !== null && slPrice === null) || (tp.value !== null && tpPrice === null)) {
    return { ok: false, error: "An amount is smaller than one point, or too large for the price." };
  }

  return { ok: true, stopLoss: slPrice, takeProfit: tpPrice };
}

// Take profit on a buy and stop loss on a sell sit above the price.
function isAbove(kind: StopKind, side: Side): boolean {
  return (side === "Buy") === (kind === "takeProfit");
}
