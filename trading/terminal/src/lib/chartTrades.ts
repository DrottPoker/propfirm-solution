import type { EngineEvent, PositionSnapshot, Side } from "./api/types";

/** A point on the chart. Time is in UTC seconds. */
export interface TradePoint {
  time: number;
  price: number;
}

/** Where a position opened and, once closed, where it closed. */
export interface ChartTrade {
  positionId: string;
  side: Side;
  /** Null when the opening is older than the events the terminal has. */
  open: TradePoint | null;
  close: TradePoint | null;
}

/** The symbol's open and closed positions, from the account's events and its open positions. */
export function chartTrades(events: readonly EngineEvent[], positions: readonly PositionSnapshot[], symbol: string): ChartTrade[] {
  const trades = new Map<string, ChartTrade>();
  const tradeOf = (positionId: string, side: Side) => {
    let trade = trades.get(positionId);
    if (!trade) {
      trade = { positionId, side, open: null, close: null };
      trades.set(positionId, trade);
    }
    return trade;
  };

  // Open positions cover openings older than the events in the terminal.
  for (const position of positions) {
    if (position.symbol === symbol) {
      tradeOf(position.positionId, position.side).open = pointOf(position.openTime, position.openPrice);
    }
  }

  for (const event of events) {
    if (event.kind === "PositionOpened" && event.symbol === symbol) {
      tradeOf(event.positionId, event.side).open = pointOf(event.timestamp, event.openPrice);
    } else if (event.kind === "PositionClosed" && event.symbol === symbol) {
      tradeOf(event.positionId, event.side).close = pointOf(event.timestamp, event.closePrice);
    }
  }

  return [...trades.values()];
}

function pointOf(timestamp: string, price: number): TradePoint {
  return { time: Date.parse(timestamp) / 1000, price };
}
