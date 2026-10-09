import type { InstrumentInfo, Side } from "./api/types";
import { formatPrice } from "./format";
import { parseVolume, pipSize } from "./orderInput";
import type { TerminalProfile } from "./profile";

// The order ticket's rules (ADR 0058): which side of the market a limit and a stop wait on, the units distances are
// told in, and the volume a ticket starts with.

/** A pending order's type for the side at the price: a buy below the ask is a limit and above it a stop, a sell the opposite. */
export type PendingType = "Limit" | "Stop";

/**
 * The type a pending order at the price fits: the right-click menu offers it at the level clicked, and the order ticket
 * checks the type the trader chose against it. Null at the market price itself, where a market order is meant.
 */
export function pendingType(side: Side, price: number, quote: { bid: number; ask: number }, digits: number): PendingType | null {
  const scale = 10 ** digits;
  const level = Math.round(price * scale);
  const market = Math.round((side === "Buy" ? quote.ask : quote.bid) * scale);
  if (level === market) {
    return null;
  }

  return (level < market) === (side === "Buy") ? "Limit" : "Stop";
}

/**
 * Why an order of the type cannot wait at the price, or null when it can. A limit waits for a better price than the
 * market's, below it for a buy and above it for a sell, and a stop for a worse one, to follow a move. The market is
 * the ask for a buy and the bid for a sell, as the engine measures it.
 */
export function pendingTypeProblem(side: Side, type: PendingType, price: number, quote: { bid: number; ask: number }, digits: number): string | null {
  if (pendingType(side, price, quote, digits) === type) {
    return null;
  }

  const below = (type === "Limit") === (side === "Buy");
  const market = side === "Buy" ? quote.ask : quote.bid;
  return `A ${side.toLowerCase()} ${type.toLowerCase()} waits ${below ? "below" : "above"} the market price, now ${formatPrice(market, digits)}.`;
}

/**
 * The name of the unit distances are told in: pips for currencies and metals, points for indices, commodities and
 * crypto. Either is the instrument's pip, a tenth of its last two decimals, for example 0.0001 for EURUSD and 1 for
 * DE40.
 */
export function distanceUnit(instrument: Pick<InstrumentInfo, "category">): "pips" | "points" {
  return instrument.category === "Forex" || instrument.category === "Metals" ? "pips" : "points";
}

/** A distance in prices as pips of the instrument, for example 0.00009 on EURUSD is 0.9 pips. */
export function pipsOf(distance: number, digits: number): number {
  return Math.round((distance / pipSize(digits)) * 10) / 10;
}

/** The spread between bid and ask in the instrument's unit, for example "0.9 pips" on EURUSD or "1.2 points" on DE40. */
export function spreadText(quote: { bid: number; ask: number }, instrument: Pick<InstrumentInfo, "category" | "digits">): string {
  return pipsText(pipsOf(quote.ask - quote.bid, instrument.digits), distanceUnit(instrument));
}

/** The engine's markup in its smallest steps as pips, for example 2 points on EURUSD is 0.2 pips. */
export function markupPips(points: number, digits: number): number {
  return pipsOf(points * 10 ** -digits, digits);
}

/** A distance in pips with the unit, for example "0.9 pips" or "12 points". Whole numbers from 100 on. */
export function pipsText(pips: number, unit: "pips" | "points"): string {
  const shown = Math.abs(pips) >= 100 ? Math.round(pips).toString() : pips.toFixed(1).replace(/\.0$/, "");
  return `${shown} ${pips === 1 ? unit.slice(0, -1) : unit}`;
}

/** How many pips the room lasts at what a pip is worth, rounded down, for "Today's limit lasts 50 pips". */
export function pipsOfRoom(room: number, pipValue: number): number {
  return pipValue > 0 ? Math.floor(room / pipValue) : 0;
}

/**
 * The volume a ticket for the instrument starts with: the one the trader last chose for it, as long as the instrument
 * still allows it, and otherwise what the firm's profile says: the smallest volume, or a number of lots kept within the
 * instrument's limits and steps. A ticket that starts from the risk sizes itself, so it starts at the smallest.
 */
export function startVolume(instrument: InstrumentInfo, remembered: Record<string, string>, start: TerminalProfile["startingSize"]): string {
  const volume = remembered[instrument.symbol];
  if (volume !== undefined && parseVolume(volume, instrument).ok) {
    return volume;
  }

  const decimals = decimalsOf(instrument.volumeStep);
  if (start.kind === "Lots" && start.value !== null) {
    const steps = Math.round(start.value / instrument.volumeStep);
    const lots = Math.min(instrument.volumeMax, Math.max(instrument.volumeMin, steps * instrument.volumeStep));
    return lots.toFixed(decimals);
  }

  return instrument.volumeMin.toFixed(decimals);
}

function decimalsOf(value: number): number {
  const text = String(value);
  const dot = text.indexOf(".");
  return dot === -1 ? 0 : text.length - dot - 1;
}
