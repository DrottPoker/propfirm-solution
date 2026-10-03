import { describe, expect, it } from "vitest";

import type { EngineEvent } from "./api/types";
import { describeEvent, isWarning } from "./events";

const digitsOf = (symbol: string) => (symbol === "XAUUSD" ? 2 : 5);
const timestamp = "2026-10-05T08:00:00+00:00";

describe("describeEvent", () => {
  it("describes a closed position with its price, reason and profit", () => {
    const event: EngineEvent = {
      kind: "PositionClosed",
      accountId: "demo",
      positionId: "o-1",
      symbol: "XAUUSD",
      side: "Buy",
      volume: 0.1,
      openPrice: 2650.3,
      closePrice: 2660,
      profit: 97,
      commission: 0.35,
      reason: "TakeProfit",
      balanceAfter: 100_096.65,
      timestamp,
    };

    expect(describeEvent(event, digitsOf)).toBe("Closed Buy 0.10 XAUUSD at 2660.00 (TakeProfit), profit 97.00");
  });

  it("describes a breach with equity and level", () => {
    const event: EngineEvent = {
      kind: "EquityFloorBreached",
      accountId: "demo",
      floorId: "daily",
      level: 95_000,
      equity: 94_990.5,
      prices: [],
      positions: [],
      timestamp,
    };

    expect(describeEvent(event, digitsOf)).toBe("Floor daily breached: equity 94,990.50 fell below 95,000.00");
    expect(isWarning(event)).toBe(true);
  });

  it("describes a withdrawal with its amount and the balance after", () => {
    const event: EngineEvent = {
      kind: "BalanceAdjusted",
      accountId: "demo",
      operationId: "payout-1",
      amount: -8_000,
      balanceAfter: 100_000,
      timestamp,
    };

    expect(describeEvent(event, digitsOf)).toBe("Withdrawal of 8,000.00, balance 100,000.00");
    expect(isWarning(event)).toBe(false);
  });

  it("warns when trading is paused, and not when it resumes", () => {
    const suspended: EngineEvent = { kind: "AccountSuspended", accountId: "demo", timestamp };
    const resumed: EngineEvent = { kind: "AccountResumed", accountId: "demo", timestamp };

    expect(describeEvent(suspended, digitsOf)).toBe("Trading paused: no new orders until it is resumed");
    expect(isWarning(suspended)).toBe(true);
    expect(describeEvent(resumed, digitsOf)).toBe("Trading resumed");
    expect(isWarning(resumed)).toBe(false);
  });

  it("names the rejected input and the reason", () => {
    const event: EngineEvent = {
      kind: "InputRejected",
      input: { kind: "PlaceOrder", accountId: "demo", orderId: "o-2", symbol: "EURUSD", side: "Buy", type: "Market", volume: 1, price: null, stopLoss: null, takeProfit: null, timestamp },
      reason: "StalePrice",
      timestamp,
    };

    expect(describeEvent(event, digitsOf)).toBe("Rejected PlaceOrder: StalePrice");
    expect(isWarning(event)).toBe(true);
  });
});
