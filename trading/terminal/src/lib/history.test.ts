import { describe, expect, it } from "vitest";

import type { EngineEvent } from "./api/types";
import { historyCsv, historyRows, historySummary, inRange, rangeStart } from "./history";

// Friday 9 October 2026, 08:30 UTC.
const now = new Date(Date.UTC(2026, 9, 9, 8, 30));

const opened = (positionId: string, timestamp: string): EngineEvent =>
  ({
    kind: "PositionOpened",
    accountId: "demo",
    positionId,
    orderId: `o-${positionId}`,
    symbol: "EURUSD",
    side: "Buy",
    volume: 0.1,
    openPrice: 1.1,
    commission: 0.35,
    balanceAfter: 9_999.65,
    stopLoss: null,
    takeProfit: null,
    trailingDistance: null,
    timestamp,
  }) as EngineEvent;

const closed = (positionId: string, timestamp: string, profit: number): EngineEvent =>
  ({
    kind: "PositionClosed",
    accountId: "demo",
    positionId,
    symbol: "EURUSD",
    side: "Buy",
    volume: 0.1,
    openPrice: 1.1,
    closePrice: 1.101,
    profit,
    commission: 0.35,
    reason: "TakeProfit",
    balanceAfter: 10_000,
    timestamp,
  }) as EngineEvent;

const payout: EngineEvent = {
  kind: "BalanceAdjusted",
  accountId: "demo",
  operationId: "payout-1",
  amount: -500,
  balanceAfter: 9_500,
  timestamp: "2026-10-09T08:00:00+00:00",
} as EngineEvent;

const events = [
  opened("p1", "2026-10-02T07:00:00+00:00"),
  closed("p1", "2026-10-02T09:00:00+00:00", 10),
  opened("p2", "2026-10-06T07:00:00+00:00"),
  closed("p2", "2026-10-06T09:00:00+00:00", -5),
  opened("p3", "2026-10-09T07:00:00+00:00"),
  closed("p3", "2026-10-09T07:30:00+00:00", 3.2),
  payout,
];

describe("the history", () => {
  it("starts today, on this week's Monday, or not at all", () => {
    expect(rangeStart("today", "UTC", now)).toBe("2026-10-09");
    expect(rangeStart("week", "UTC", now)).toBe("2026-10-05");
    expect(rangeStart("all", "UTC", now)).toBeNull();
  });

  it("shows the closes and money of the range, newest first", () => {
    const rows = historyRows(events);
    expect(inRange(rows, "today", "UTC", now).map((r) => r.kind)).toEqual(["BalanceAdjusted", "PositionClosed"]);
    expect(inRange(rows, "week", "UTC", now)).toHaveLength(3);
    expect(inRange(rows, "all", "UTC", now)).toHaveLength(4);
  });

  it("adds up the closes after commission, leaving money in and out aside", () => {
    expect(historySummary(historyRows(events), events)).toEqual({ trades: 3, result: 6.1 });
    expect(historySummary(inRange(historyRows(events), "today", "UTC", now), events)).toEqual({ trades: 1, result: 2.5 });
  });

  it("writes the rows as CSV, oldest first, with payouts by name", () => {
    const csv = historyCsv(inRange(historyRows(events), "today", "UTC", now), events, () => 5, "UTC", { deposit: "Deposit", withdrawal: "Payout" });

    expect(csv.split("\r\n")).toEqual([
      "Time,Type,Symbol,Side,Volume,Open price,Close price,Reason,Commission,Result,Balance after,Position",
      "2026-10-09 07:30:00,Closed,EURUSD,Buy,0.10,1.10000,1.10100,Take profit,0.70,2.50,10000.00,p3",
      "2026-10-09 08:00:00,Payout,,,,,,,,-500.00,9500.00,",
      "",
    ]);
  });
});
