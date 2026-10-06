import { describe, expect, it } from "vitest";

import type { EngineEvent, PositionSnapshot } from "./api/types";
import { chartTrades } from "./chartTrades";

// 2026-10-05 08:00:00 UTC
const eight = Date.UTC(2026, 9, 5, 8, 0, 0) / 1000;
const at = (seconds: number) => new Date((eight + seconds) * 1000).toISOString();

function opened(positionId: string, symbol: string, seconds: number, openPrice: number): EngineEvent {
  return {
    kind: "PositionOpened",
    accountId: "demo",
    positionId,
    symbol,
    side: "Buy",
    volume: 1,
    openPrice,
    stopLoss: null,
    takeProfit: null,
    commission: 3.5,
    balanceAfter: 99_996.5,
    timestamp: at(seconds),
  };
}

function closed(positionId: string, symbol: string, seconds: number, closePrice: number): EngineEvent {
  return {
    kind: "PositionClosed",
    accountId: "demo",
    positionId,
    symbol,
    side: "Buy",
    volume: 1,
    openPrice: 1.1,
    closePrice,
    profit: 10,
    commission: 3.5,
    reason: "Manual",
    balanceAfter: 100_003,
    timestamp: at(seconds),
  };
}

function position(positionId: string, symbol: string, seconds: number, openPrice: number): PositionSnapshot {
  return {
    positionId,
    symbol,
    side: "Sell",
    volume: 1,
    openPrice,
    stopLoss: null,
    takeProfit: null,
    openTime: at(seconds),
    currentPrice: openPrice,
    profit: 0,
    margin: 1_000,
    trailingDistance: null,
  };
}

describe("chartTrades", () => {
  it("pairs the opening and closing of each position", () => {
    const trades = chartTrades([opened("a", "EURUSD", 10, 1.1), closed("a", "EURUSD", 70, 1.2)], [], "EURUSD");

    expect(trades).toEqual([
      { positionId: "a", side: "Buy", open: { time: eight + 10, price: 1.1 }, close: { time: eight + 70, price: 1.2 }, parts: [] },
    ]);
  });

  it("shows open positions without a close", () => {
    const trades = chartTrades([], [position("b", "EURUSD", 30, 1.15)], "EURUSD");

    expect(trades).toEqual([{ positionId: "b", side: "Sell", open: { time: eight + 30, price: 1.15 }, close: null, parts: [] }]);
  });

  it("keeps a close whose opening is older than the events", () => {
    const trades = chartTrades([closed("c", "EURUSD", 70, 1.2)], [], "EURUSD");

    expect(trades).toEqual([{ positionId: "c", side: "Buy", open: null, close: { time: eight + 70, price: 1.2 }, parts: [] }]);
  });

  it("marks where parts of a position closed before the rest", () => {
    const part: EngineEvent = {
      kind: "PositionPartiallyClosed",
      accountId: "demo",
      positionId: "f",
      symbol: "EURUSD",
      side: "Buy",
      volume: 0.4,
      remainingVolume: 0.6,
      openPrice: 1.1,
      closePrice: 1.15,
      profit: 2_000,
      commission: 1.4,
      reason: "Manual",
      balanceAfter: 101_998.6,
      timestamp: at(40),
    };

    const trades = chartTrades([opened("f", "EURUSD", 10, 1.1), part, closed("f", "EURUSD", 70, 1.2)], [], "EURUSD");

    expect(trades[0].parts).toEqual([{ time: eight + 40, price: 1.15 }]);
    expect(trades[0].close).toEqual({ time: eight + 70, price: 1.2 });
  });

  it("leaves out other symbols", () => {
    const trades = chartTrades([opened("d", "GBPUSD", 10, 1.3)], [position("e", "XAUUSD", 30, 2650)], "EURUSD");

    expect(trades).toEqual([]);
  });
});
