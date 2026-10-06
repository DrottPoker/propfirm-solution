import type { InstrumentLimits, Side } from "./api/types";
import { formatMoney } from "./format";
import { estimatedProfit } from "./stops";
import { readSetting, writeSetting } from "./syncedSettings";

// The volume from what the trader is willing to lose (ADR 0052): an amount, a share of the balance or a share of the
// room left to the nearest loss limit, lost at the stop loss. An estimate before commission, like the stop amounts.

/** Whether the ticket takes lots, or the risk the volume is worked out from. */
export type SizeMode = "lots" | "risk";

/** What the risk is typed in: the account currency, percent of the balance, or percent of the room to the nearest loss limit. */
export type RiskUnit = "money" | "balance" | "room";

/** How the trader sizes orders, with the risk last typed in each unit, so switching units never misreads one. */
export interface Sizing {
  mode: SizeMode;
  unit: RiskUnit;
  risks: Record<RiskUnit, string>;
}

export const riskUnits: readonly RiskUnit[] = ["money", "balance", "room"];

export const defaultSizing: Sizing = { mode: "lots", unit: "balance", risks: { money: "", balance: "1", room: "10" } };

const storageKey = "trading.sizing";

/** Reads a stored choice, falling back to the default for anything that is not one. */
export function parseSizing(raw: string | null): Sizing {
  if (!raw) {
    return defaultSizing;
  }

  try {
    const value: unknown = JSON.parse(raw);
    if (typeof value !== "object" || value === null) {
      return defaultSizing;
    }

    const { mode, unit, risks } = value as Record<string, unknown>;
    const typed = typeof risks === "object" && risks !== null ? (risks as Record<string, unknown>) : {};
    return {
      mode: mode === "risk" ? "risk" : "lots",
      unit: riskUnits.includes(unit as RiskUnit) ? (unit as RiskUnit) : defaultSizing.unit,
      risks: Object.fromEntries(
        riskUnits.map((u) => [u, typeof typed[u] === "string" ? (typed[u] as string) : defaultSizing.risks[u]]),
      ) as Record<RiskUnit, string>,
    };
  } catch {
    return defaultSizing;
  }
}

/** How the trader sizes orders, kept in the browser and on their login. */
export function loadSizing(): Sizing {
  return parseSizing(readSetting(storageKey));
}

export function saveSizing(sizing: Sizing) {
  writeSetting(storageKey, JSON.stringify(sizing));
}

/**
 * The amount to risk in the account currency, or null when the typed risk is not a positive number, a percent above
 * 100, or a share of something not known yet. Amounts take at most 2 decimals and percents at most 2.
 */
export function riskAmount(input: string, unit: RiskUnit, balance: number | undefined, room: number | undefined): number | null {
  const text = input.trim().replace(",", ".");
  if (!/^\d+(\.\d{1,2})?$/.test(text) || Number(text) <= 0) {
    return null;
  }

  const value = Number(text);
  if (unit === "money") {
    return value;
  }

  const base = unit === "balance" ? balance : room;
  return value <= 100 && base !== undefined && base > 0 ? (base * value) / 100 : null;
}

/** The risk one step up or down: 10 in the account currency, 0.1 percent of the balance or 1 percent of the room. */
export function stepRisk(input: string, direction: 1 | -1, unit: RiskUnit): string {
  const step = unit === "money" ? 10 : unit === "balance" ? 0.1 : 1;
  const decimals = unit === "balance" ? 1 : 0;
  const text = input.trim().replace(",", ".");
  const current = /^\d+(\.\d+)?$/.test(text) ? Number(text) : 0;
  const steps = Math.round(current / step);
  const next = Math.max(1, steps + direction) * step;
  const capped = unit === "money" ? next : Math.min(100, next);
  return capped.toFixed(decimals);
}

export type RiskSize = { ok: true; lots: number; risk: number; atMost: boolean } | { ok: false; reason: string };

/**
 * The largest volume, in whole steps of the instrument, that loses at most the amount at the stop loss, with what it
 * then risks. A volume above the instrument's largest is lowered to it, and <c>atMost</c> says so.
 */
export function sizeForRisk({
  amount,
  side,
  entry,
  stopLoss,
  digits,
  pointValuePerLot,
  limits,
}: {
  amount: number;
  side: Side;
  entry: number;
  stopLoss: number;
  digits: number;
  pointValuePerLot: number;
  limits: InstrumentLimits;
}): RiskSize {
  const lossPerLot = -estimatedProfit(side, 1, entry, stopLoss, digits, pointValuePerLot);
  if (!(lossPerLot > 0)) {
    return { ok: false, reason: `The stop loss is on the wrong side of the price for a ${side.toLowerCase()}.` };
  }

  // Whole steps avoid binary floating point. The small epsilon keeps 0.3 / 0.1 from becoming 2.99... steps.
  const decimals = decimalsOf(limits.volumeStep);
  const scale = 10 ** decimals;
  const step = Math.round(limits.volumeStep * scale);
  const min = Math.round(limits.volumeMin * scale);
  const max = Math.round(limits.volumeMax * scale);
  const units = Math.floor((amount / lossPerLot) * scale / step + 1e-9) * step;
  if (units < min) {
    return {
      ok: false,
      reason: `Too little to risk: the smallest volume, ${(min / scale).toFixed(decimals)} lots, risks ${formatMoney((min / scale) * lossPerLot)}.`,
    };
  }

  const lots = Math.min(units, max) / scale;
  return { ok: true, lots, risk: lots * lossPerLot, atMost: units > max };
}

function decimalsOf(value: number): number {
  const text = String(value);
  const dot = text.indexOf(".");
  return dot === -1 ? 0 : text.length - dot - 1;
}
