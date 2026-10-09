import { describe, expect, it } from "vitest";

import type { EventEnvelope } from "./api/types";
import { accountOf, describeEvent } from "./events";

const digits = (symbol: string) => ({ XAUUSD: 2, EURUSD: 5 })[symbol];
const at = "2026-10-08T14:31:58Z";

describe("events", () => {
  it("describes a closed position with its price, profit and why", () => {
    const envelope: EventEnvelope = {
      sequence: 1_204_643,
      event: {
        kind: "PositionClosed",
        timestamp: at,
        accountId: "10482",
        positionId: "P1",
        symbol: "XAUUSD",
        side: "Buy",
        volume: 1,
        openPrice: 2650,
        closePrice: 2661.4,
        profit: 412,
        commission: 3.5,
        reason: "StopLoss",
        balanceAfter: 100_412,
      },
    };

    expect(describeEvent(envelope, digits)).toEqual({
      sequence: 1_204_643,
      time: at,
      what: "Position closed",
      accountId: "10482",
      detail: "XAUUSD, 1.00 lot at 2,661.40, +412.00, stop loss",
      tone: "normal",
    });
  });

  it("marks a broken loss limit and a refused order, with the refused order's account", () => {
    const breach: EventEnvelope = {
      sequence: 2,
      event: { kind: "EquityFloorBreached", timestamp: at, accountId: "10390", floorId: "daily", level: 95_000, equity: 94_982.1, prices: [], positions: [] },
    };
    const refused: EventEnvelope = {
      sequence: 3,
      event: {
        kind: "InputRejected",
        timestamp: at,
        reason: "StalePrice",
        input: {
          kind: "PlaceOrder",
          timestamp: at,
          accountId: "10433",
          orderId: "O1",
          symbol: "UK100",
          side: "Buy",
          type: "Market",
          volume: 1,
          price: null,
          stopLoss: null,
          takeProfit: null,
          trailingStop: false,
        },
      },
    };

    expect(describeEvent(breach, digits)).toMatchObject({ what: "Loss limit broken", detail: "daily at 95,000.00, equity 94,982.10", tone: "loss" });
    expect(describeEvent(refused, digits)).toMatchObject({ what: "Refused", detail: "Order: no fresh price", tone: "warning", accountId: "10433" });
    expect(accountOf(refused.event)).toBe("10433");
  });

  it("shows a price as it came for a symbol it does not know", () => {
    const opened: EventEnvelope = {
      sequence: 4,
      event: {
        kind: "PositionOpened",
        timestamp: at,
        accountId: "1",
        positionId: "P1",
        symbol: "NEW1",
        side: "Sell",
        volume: 0.5,
        openPrice: 12.345,
        stopLoss: null,
        takeProfit: null,
        commission: 0,
        balanceAfter: 1000,
        trailingDistance: null,
      },
    };

    expect(describeEvent(opened, digits).detail).toBe("NEW1, sell 0.50 lot at 12.345");
  });
});
