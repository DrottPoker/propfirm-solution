import { describe, expect, it } from "vitest";

import type { EngineEvent, EventEnvelope } from "./api/types";
import { createNewEvents, noticeLine, tradeNotice } from "./notices";

const digitsOf = (symbol: string) => (symbol === "XAUUSD" ? 2 : 5);
const timestamp = "2026-10-06T08:00:00+00:00";

const opened: EngineEvent = {
  kind: "PositionOpened",
  accountId: "demo",
  positionId: "849ede3a-0000",
  symbol: "EURUSD",
  side: "Buy",
  volume: 1,
  openPrice: 1.08724,
  stopLoss: null,
  takeProfit: null,
  commission: 3.5,
  balanceAfter: 99_996.5,
  timestamp,
};

describe("tradeNotice", () => {
  it("tells of a part closed, with what stays open and the part's result", () => {
    const part: EngineEvent = {
      kind: "PositionPartiallyClosed",
      accountId: "demo",
      positionId: "849ede3a-0000",
      symbol: "EURUSD",
      side: "Buy",
      volume: 0.4,
      remainingVolume: 0.6,
      openPrice: 1.08,
      closePrice: 1.082,
      profit: 80,
      commission: 1.4,
      reason: "Manual",
      balanceAfter: 100_075.1,
      timestamp,
    };

    expect(tradeNotice(part, digitsOf, [])).toEqual({
      id: `part-849ede3a-0000-${timestamp}`,
      kind: "closed",
      title: "0.40 of Buy EURUSD closed at 1.08200, 0.60 still open",
      result: 78.6,
    });
  });

  it("says what was bought or sold and at what price", () => {
    expect(tradeNotice(opened, digitsOf, [])).toEqual({ id: "filled-849ede3a-0000", kind: "filled", title: "Bought 1.00 EURUSD at 1.08724" });
    expect(tradeNotice({ ...opened, side: "Sell", symbol: "XAUUSD", volume: 0.1, openPrice: 2403.1 }, digitsOf, [])?.title).toBe(
      "Sold 0.10 XAUUSD at 2403.10",
    );
  });

  it("says where a pending order waits", () => {
    const placed: EngineEvent = {
      kind: "OrderPlaced",
      accountId: "demo",
      orderId: "o-2",
      symbol: "EURUSD",
      side: "Buy",
      type: "Limit",
      volume: 0.5,
      price: 1.08,
      stopLoss: null,
      takeProfit: null,
      timestamp,
    };

    expect(tradeNotice(placed, digitsOf, [])).toEqual({ id: "placed-o-2", kind: "placed", title: "Buy limit 0.50 EURUSD placed at 1.08000" });
  });

  it("says how a position closed, with the result after commission", () => {
    const closed: EngineEvent = {
      kind: "PositionClosed",
      accountId: "demo",
      positionId: "849ede3a-0000",
      symbol: "EURUSD",
      side: "Buy",
      volume: 1,
      openPrice: 1.08724,
      closePrice: 1.08524,
      profit: -200,
      commission: 3.5,
      reason: "StopLoss",
      balanceAfter: 99_793,
      timestamp,
    };

    expect(tradeNotice(closed, digitsOf, [opened])).toEqual({
      id: "closed-849ede3a-0000",
      kind: "closed",
      title: "Buy 1.00 EURUSD closed at 1.08524 by its stop loss",
      result: -207,
    });
    expect(tradeNotice({ ...closed, reason: "Manual" }, digitsOf, [opened])?.title).toBe("Buy 1.00 EURUSD closed at 1.08524");
  });

  it("has nothing to say about other events", () => {
    expect(tradeNotice({ kind: "AccountResumed", accountId: "demo", timestamp }, digitsOf, [])).toBeNull();
  });
});

describe("createNewEvents", () => {
  const envelope = (sequence: number): EventEnvelope => ({ sequence, event: opened });

  it("passes each event on once, whichever way it arrives first", () => {
    const events = createNewEvents(100);
    const received: number[] = [];
    events.subscribe((list) => received.push(...list.map((e) => e.sequence)));

    events.announce([envelope(9)]);
    events.announce([envelope(8), envelope(9), envelope(10)]);
    events.announce([envelope(10)]);

    expect(received).toEqual([9, 8, 10]);
  });

  it("forgets the oldest beyond the limit and everything on reset", () => {
    const events = createNewEvents(2);
    const received: number[] = [];
    events.subscribe((list) => received.push(...list.map((e) => e.sequence)));

    events.announce([envelope(1), envelope(2), envelope(3)]);
    events.announce([envelope(1), envelope(3)]);
    events.reset();
    events.announce([envelope(3)]);

    expect(received).toEqual([1, 2, 3, 1, 3]);
  });

  it("stops passing events on after unsubscribing", () => {
    const events = createNewEvents(10);
    const received: number[] = [];
    const stop = events.subscribe((list) => received.push(...list.map((e) => e.sequence)));

    stop();
    events.announce([envelope(1)]);

    expect(received).toEqual([]);
  });
});

describe("noticeLine", () => {
  it("puts the firm's notice on one line, with its paragraphs joined by dots", () => {
    expect(noticeLine("We are working on it.\n\nDemo Firm: Accounts that broke a limit are reinstated.\n")).toBe(
      "We are working on it. · Demo Firm: Accounts that broke a limit are reinstated.",
    );
    expect(noticeLine("One line")).toBe("One line");
  });
});
