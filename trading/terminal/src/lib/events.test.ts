import { describe, expect, it } from "vitest";

import type { EngineEvent } from "./api/types";
import { describeEvent, isWarning, netResult, positionCommission, rejectionReason, rejectionText } from "./events";

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

    expect(describeEvent(event, digitsOf)).toBe("Closed Buy 0.10 XAUUSD at 2660.00 by its take profit, profit 97.00 before commission");
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

    expect(describeEvent(event, digitsOf)).toBe("Daily loss limit broken: equity 94,990.50 fell below 95,000.00");
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

  it("says when the firm reopens an account whose phase it reinstated", () => {
    const reopened: EngineEvent = { kind: "AccountReopened", accountId: "demo", balance: 100_000, timestamp };

    expect(describeEvent(reopened, digitsOf)).toBe("Trading reopened by the firm with a balance of 100,000.00");
    expect(isWarning(reopened)).toBe(false);
  });

  it("explains a refusal of an account that has not ended", () => {
    expect(rejectionReason("AccountNotDisabled")).toBe("trading on the account has not ended");
  });

  it("names the rejected input and the reason", () => {
    const event: EngineEvent = {
      kind: "InputRejected",
      input: { kind: "PlaceOrder", accountId: "demo", orderId: "o-2", symbol: "EURUSD", side: "Buy", type: "Market", volume: 1, price: null, stopLoss: null, takeProfit: null, trailingStop: false, timestamp },
      reason: "StalePrice",
      timestamp,
    };

    expect(describeEvent(event, digitsOf)).toBe("Order refused: the price is too old, wait for the next one");
    expect(isWarning(event)).toBe(true);
  });

  it("says in plain words that the loss limit closed a position and ended trading", () => {
    const closed: EngineEvent = {
      kind: "PositionClosed",
      accountId: "demo",
      positionId: "o-1",
      symbol: "EURUSD",
      side: "Buy",
      volume: 1,
      openPrice: 1.08,
      closePrice: 1.075,
      profit: -500,
      commission: 3.5,
      reason: "EquityFloor",
      balanceAfter: 9_451,
      timestamp,
    };
    const disabled: EngineEvent = { kind: "AccountDisabled", accountId: "demo", reason: "EquityFloor", timestamp };

    expect(describeEvent(closed, digitsOf)).toBe("Closed Buy 1.00 EURUSD at 1.07500 by the loss limit, profit -500.00 before commission");
    expect(describeEvent(disabled, digitsOf)).toBe("Trading ended: a loss limit was broken");
  });
});

describe("describeEvent for the order tools", () => {
  it("tells of a part closed, a moved order and a trailing stop", () => {
    const part: EngineEvent = {
      kind: "PositionPartiallyClosed",
      accountId: "demo",
      positionId: "p-1",
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
    const moved: EngineEvent = { kind: "OrderModified", accountId: "demo", orderId: "o-1", symbol: "EURUSD", price: 1.075, stopLoss: 1.07, takeProfit: null, trailingDistance: 0.005, timestamp };
    const trailing: EngineEvent = { kind: "PositionModified", accountId: "demo", positionId: "p-1", stopLoss: 1.079, takeProfit: null, trailingDistance: 0.001, timestamp };

    expect(describeEvent(part, digitsOf)).toBe("Closed 0.40 of Buy EURUSD at 1.08200, 0.60 left open, profit 80.00 before commission");
    expect(describeEvent(moved, digitsOf)).toBe("Moved order o-1 on EURUSD to 1.07500: SL 1.07, TP -, trailing 50.0 pips");
    expect(describeEvent(trailing, digitsOf)).toBe("Changed stops on p-1: SL 1.079, TP -, trailing");
    expect(rejectionText("NoStopLoss")).toBe("Refused: a trailing stop needs a stop loss to follow");
  });
});

describe("rejectionText", () => {
  it("explains the reason, and shows a reason it does not know as it is", () => {
    expect(rejectionText("InsufficientMargin")).toBe("Refused: not enough free margin");
    expect(rejectionText("MarketClosed")).toBe("Refused: the market is closed");
    expect(rejectionText("SomethingNew")).toBe("Refused: SomethingNew");
  });
});

describe("positionCommission", () => {
  const closed = {
    kind: "PositionClosed" as const,
    accountId: "demo",
    positionId: "o-1",
    symbol: "EURUSD",
    side: "Buy" as const,
    volume: 1,
    openPrice: 1.08,
    closePrice: 1.081,
    profit: 100,
    commission: 3.5,
    reason: "Manual" as const,
    balanceAfter: 100_093,
    timestamp,
  };
  const opened: EngineEvent = {
    kind: "PositionOpened",
    accountId: "demo",
    positionId: "o-1",
    symbol: "EURUSD",
    side: "Buy",
    volume: 1,
    openPrice: 1.08,
    stopLoss: null,
    takeProfit: null,
    commission: 3,
    balanceAfter: 99_997,
    timestamp,
  };

  it("adds the commission charged when the position opened", () => {
    expect(positionCommission(closed, [opened, closed])).toBe(6.5);
  });

  it("takes the opening commission to be the closing one when the opened event is not at hand", () => {
    expect(positionCommission(closed, [closed])).toBe(7);
  });

  // The same result as the firm's portal shows for the trade.
  it("is taken off the profit for the result", () => {
    expect(netResult(closed, [opened, closed])).toBe(93.5);
    expect(netResult({ ...closed, profit: 8, commission: 3.5 }, [{ ...opened, commission: 3.5 }, closed])).toBe(1);
  });
});
