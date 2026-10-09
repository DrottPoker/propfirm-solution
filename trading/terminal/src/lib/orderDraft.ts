import { create } from "zustand";

import type { OrderType, Side } from "./api/types";
import { formatSignedMoney } from "./format";
import { estimatedProfit, type ResolvedStops } from "./stops";

/** A level the order being filled in would get, shown faintly on the chart before it is sent. */
export interface GhostLine {
  kind: "entry" | "stopLoss" | "takeProfit";
  price: number;
  title: string;
}

/**
 * A change the chart asks of the order panel: a level picked for a field, or null to empty it, for example when a
 * ghost line is removed. The id tells two requests apart.
 */
export interface FieldRequest {
  id: number;
  symbol: string;
  field: GhostLine["kind"];
  price: number | null;
}

interface OrderDraftState {
  symbol: string | null;
  lines: GhostLine[];
  /** The levels of a position or an order being changed from the tables, shown in place of the order panel's. */
  edit: { symbol: string; lines: GhostLine[] } | null;
  fieldRequest: FieldRequest | null;
  setDraft: (symbol: string | null, lines: GhostLine[]) => void;
  setEdit: (edit: { symbol: string; lines: GhostLine[] } | null) => void;
  requestField: (symbol: string, field: GhostLine["kind"], price: number | null) => void;
}

/** What the order panel would send, for the chart to preview, and changes the chart asks of it. */
export const useOrderDraft = create<OrderDraftState>()((set) => ({
  symbol: null,
  lines: [],
  edit: null,
  fieldRequest: null,
  setDraft: (symbol, lines) => set({ symbol, lines }),
  setEdit: (edit) => set({ edit }),
  requestField: (symbol, field, price) =>
    set((s) => ({ fieldRequest: { id: (s.fieldRequest?.id ?? 0) + 1, symbol, field, price } })),
}));

/**
 * The only side that typed stop prices fit, or null when they fit both or neither. The engine wants a buy's stop
 * loss below and its take profit above the reference, and the opposite for a sell. The reference is the order price
 * of a pending order, and for a market order the price the position closes at: the bid for a buy, the ask for a sell.
 */
export function sideOfStops(stopLoss: number | null, takeProfit: number | null, buyReference: number, sellReference: number): Side | null {
  const fitsBuy = (stopLoss === null || stopLoss < buyReference) && (takeProfit === null || takeProfit > buyReference);
  const fitsSell = (stopLoss === null || stopLoss > sellReference) && (takeProfit === null || takeProfit < sellReference);
  return fitsBuy === fitsSell ? null : fitsBuy ? "Buy" : "Sell";
}

/**
 * The levels of the order for one side: the order price of a pending order, and the stop loss and take profit with
 * the estimated result at each, counted from the entry.
 */
export function ghostLines({
  type,
  side,
  entry,
  stops,
  lots,
  digits,
  pointValuePerLot,
}: {
  type: OrderType;
  side: Side;
  entry: number | undefined;
  stops: ResolvedStops;
  lots: number | undefined;
  digits: number;
  pointValuePerLot: number | undefined;
}): GhostLine[] {
  const lines: GhostLine[] = [];
  if (type !== "Market" && entry !== undefined) {
    lines.push({ kind: "entry", price: entry, title: `${side} ${type.toLowerCase()}` });
  }

  if (!stops.ok) {
    return lines;
  }

  const amount = (price: number) =>
    entry !== undefined && lots !== undefined && pointValuePerLot !== undefined
      ? ` ${formatSignedMoney(estimatedProfit(side, lots, entry, price, digits, pointValuePerLot))}`
      : "";
  if (stops.stopLoss !== null) {
    lines.push({ kind: "stopLoss", price: stops.stopLoss, title: `${side} SL${amount(stops.stopLoss)}` });
  }

  if (stops.takeProfit !== null) {
    lines.push({ kind: "takeProfit", price: stops.takeProfit, title: `${side} TP${amount(stops.takeProfit)}` });
  }

  return lines;
}
