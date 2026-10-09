import { floorLabel, type FloorRisk } from "./account";
import type { FloorSnapshot, Side } from "./api/types";
import { formatMoney } from "./format";
import { pipSize } from "./orderInput";
import { estimatedProfit } from "./stops";

// What an order means before it is placed: the margin it needs, what a pip is worth at its volume and what its stop
// loss risks. Estimates from the point value the engine gives, like the amounts for stop loss and take profit.

/**
 * The margin a new position needs, in the account currency. The engine divides the position's value in the base
 * currency, converted to the account currency, by the leverage. The point value holds the rate of the quote currency,
 * so the base currency's rate is the price times that. The same as the engine's when the account is in the base or
 * the quote currency, and close for other pairs, which the engine converts through their own prices.
 */
export function estimatedMargin({
  lots,
  price,
  digits,
  contractSize,
  leverage,
  pointValuePerLot,
  baseIsAccountCurrency,
}: {
  lots: number;
  price: number;
  digits: number;
  contractSize: number;
  leverage: number;
  pointValuePerLot: number;
  baseIsAccountCurrency: boolean;
}): number {
  const value = baseIsAccountCurrency ? lots * contractSize : lots * price * 10 ** digits * pointValuePerLot;
  return value / leverage;
}

/** What a move of one pip is worth at the volume, in the account currency. A pip is 10 points on most pairs. */
export function pipValue(lots: number, digits: number, pointValuePerLot: number): number {
  return lots * pipSize(digits) * 10 ** digits * pointValuePerLot;
}

/** What the order loses at its stop loss, before commission. Null when the stop loss is not on the losing side. */
export function stopLossRisk(side: Side, lots: number, entry: number, stopLoss: number, digits: number, pointValuePerLot: number): number | null {
  const result = estimatedProfit(side, lots, entry, stopLoss, digits, pointValuePerLot);
  return result < 0 ? -result : null;
}

/**
 * The loss limit nearest to equity, the one with the least room left, since it is the one a loss reaches first.
 * Usually the daily loss limit. Null without limits, or when every one is broken.
 */
export function nearestLimit(floors: readonly FloorSnapshot[]): FloorSnapshot | null {
  return floors.filter((f) => f.headroom > 0).reduce<FloorSnapshot | null>((nearest, f) => (!nearest || f.headroom < nearest.headroom ? f : nearest), null);
}

/** How much of the room to the limit a loss takes, in percent. */
export function shareOfRoom(risk: number, limit: FloorSnapshot): number {
  return (risk / limit.headroom) * 100;
}

/** How worrying a share of the room is: half of it is a warning, all of it would break the limit. */
export function shareRisk(percent: number): FloorRisk {
  return percent >= 100 ? "danger" : percent >= 50 ? "warning" : "ok";
}

/** A share in percent, for example 0.4% or 35%. Tiny shares say so, rather than 0.0%. */
export function formatShare(percent: number): string {
  if (percent > 0 && percent < 0.1) {
    return "under 0.1%";
  }

  return `${percent < 10 ? percent.toFixed(1) : Math.round(percent).toString()}%`;
}

/** What the room to the limit is called: "today's limit" for the daily one, otherwise for example "the room to the max loss limit". */
export function roomName(limit: FloorSnapshot): string {
  return limit.floorId === "daily" ? "today's limit" : `the room to the ${floorLabel(limit.floorId).toLowerCase()}`;
}

/** What the stop loss risks, for example "Risks 18.00 USD, 0.4% of today's limit". */
export function riskText(risk: number, currency: string, limit: FloorSnapshot | null): string {
  const amount = `Risks ${formatMoney(risk)} ${currency}`;
  return limit ? `${amount}, ${formatShare(shareOfRoom(risk, limit))} of ${roomName(limit)}` : amount;
}
