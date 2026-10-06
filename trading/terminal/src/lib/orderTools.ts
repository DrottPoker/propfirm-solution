import type { InstrumentLimits, PositionSnapshot } from "./api/types";
import { pipSize } from "./orderInput";

/**
 * The stop loss that moves a position to break even: its open price. Null when the price has not moved past the open
 * price, since the engine refuses a stop loss on the winning side, or when the stop loss already protects it.
 */
export function breakEvenStop(position: PositionSnapshot): number | null {
  const isBuy = position.side === "Buy";
  const inProfit = isBuy ? position.currentPrice > position.openPrice : position.currentPrice < position.openPrice;
  const protectedAlready = position.stopLoss !== null && (isBuy ? position.stopLoss >= position.openPrice : position.stopLoss <= position.openPrice);
  return inProfit && !protectedAlready ? position.openPrice : null;
}

// Volumes in whole steps, so 0.1 + 0.2 never matters.
const units = (volume: number, step: number) => Math.round(volume / step);

/**
 * The part a close part starts from: half the position, rounded down to the volume step, and never so much that less
 * than the smallest volume is left. Null when the position is too small to split.
 */
export function defaultPart(volume: number, limits: InstrumentLimits): number | null {
  const step = limits.volumeStep;
  const total = units(volume, step);
  const smallest = Math.max(1, units(limits.volumeMin, step));
  const half = Math.max(smallest, Math.floor(total / 2));
  return total - half >= smallest ? half * step : null;
}

/** Why the part cannot be closed, or null: it must be a volume the instrument allows, and leave at least the smallest. */
export function partProblem(part: number, volume: number, limits: InstrumentLimits): string | null {
  const step = limits.volumeStep;
  if (!Number.isFinite(part) || Math.abs(part / step - units(part, step)) > 1e-9 || part < limits.volumeMin) {
    return `The part must be at least ${limits.volumeMin} lots in steps of ${step}.`;
  }

  return units(volume, step) - units(part, step) < Math.max(1, units(limits.volumeMin, step))
    ? `Leave at least ${limits.volumeMin} lots open, or close the whole position.`
    : null;
}

/** A trailing stop's distance in pips, for example 10.0 for 0.00100 on EURUSD, and whole pips from 100, such as 3000 on BTCUSD. */
export function trailingPips(distance: number, digits: number): string {
  const pips = distance / pipSize(digits);
  return pips >= 100 ? pips.toFixed(0) : pips.toFixed(1);
}
