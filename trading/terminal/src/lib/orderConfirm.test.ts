import { describe, expect, it } from "vitest";

import type { PlaceOrderRequest } from "./api/types";
import { orderQuestion } from "./orderConfirm";

const order = (overrides: Partial<PlaceOrderRequest>): PlaceOrderRequest => ({
  orderId: "o1",
  symbol: "EURUSD",
  side: "Buy",
  type: "Market",
  volume: 1,
  price: null,
  stopLoss: null,
  takeProfit: null,
  trailingStop: false,
  ...overrides,
});

describe("orderQuestion", () => {
  it("asks about a market order without stops", () => {
    expect(orderQuestion(order({}), 5)).toEqual({ title: "Buy 1.00 EURUSD at market", stops: "No stop loss or take profit." });
  });

  it("asks about a pending order at its price, with its stops", () => {
    expect(orderQuestion(order({ side: "Sell", type: "Limit", volume: 0.5, price: 1.1, stopLoss: 1.105, takeProfit: 1.09 }), 5)).toEqual({
      title: "Sell limit 0.50 EURUSD at 1.10000",
      stops: "With stop loss 1.10500 and take profit 1.09000.",
    });
  });

  it("says when the stop loss trails", () => {
    expect(orderQuestion(order({ stopLoss: 1.08, trailingStop: true }), 5).stops).toBe("With trailing stop loss 1.08000.");
  });
});
