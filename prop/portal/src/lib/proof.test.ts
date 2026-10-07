import { describe, expect, it } from "vitest";

import type { BreachStep, ReceiptClose, ReceiptFill, TradeReceipt } from "./api/types";
import { equityChart, filledAtAsk, fillsOf, markupText, priceDigits, priceText, stepText, stepTone, stopText } from "./proof";

const fill = (overrides: Partial<ReceiptFill> = {}): ReceiptFill => ({
  at: "2026-10-06T12:00:00Z",
  volume: 1,
  price: 1.08635,
  commission: 3.5,
  feed: { inputSequence: 42, feed: "CapitalCom", bid: 1.0863, ask: 1.08633, receivedAt: "2026-10-06T12:00:00Z" },
  markupPoints: 2,
  prices: [],
  ...overrides,
});

const step = (overrides: Partial<BreachStep>): BreachStep => ({
  at: "2026-10-06T12:00:00Z",
  kind: "PositionOpened",
  symbol: "EURUSD",
  side: "Buy",
  volume: 1,
  price: 1.086,
  amount: null,
  reason: null,
  floorId: null,
  level: null,
  request: null,
  ...overrides,
});

describe("a trade's details", () => {
  it("fills a buy's opening and a sell's close at the ask", () => {
    expect([filledAtAsk("Buy", true), filledAtAsk("Buy", false), filledAtAsk("Sell", true), filledAtAsk("Sell", false)]).toEqual([true, false, false, true]);
  });

  it("says the markup on the filled side, when it is known", () => {
    expect(markupText(fill(), true)).toBe("2 points on the ask");
    expect(markupText(fill({ markupPoints: 1 }), false)).toBe("1 point on the bid");
    expect(markupText(fill({ markupPoints: null }), true)).toBeNull();
  });

  it("explains a stop loss that closed past its level", () => {
    const close: ReceiptClose = { fill: fill({ price: 1.0848 }), profit: -150, reason: "StopLoss", level: 1.085 };
    expect(stopText(close, "Buy", 5)).toBe(
      "The stop loss was at 1.085. The first bid at or below it was 1.0848, and a stop loss closes at the price that reaches it, here 20 points past the level.",
    );
    expect(stopText({ ...close, fill: fill({ price: 1.085 }) }, "Buy", 5)).toBe("The stop loss closed at its level, 1.085.");
    expect(stopText({ ...close, reason: "Manual" }, "Buy", 5)).toBeNull();
  });

  it("lists the opening, then each close", () => {
    const receipt = { opened: fill(), closes: [{ fill: fill({ volume: 0.5 }), profit: 10, reason: "Manual", level: null }] } as TradeReceipt;
    expect(fillsOf(receipt).map((f) => [f.fill.volume, f.close?.reason ?? null])).toEqual([
      [1, null],
      [0.5, "Manual"],
    ]);
  });
});

describe("a breach report's steps", () => {
  it("says what happened in words", () => {
    expect(stepText(step({}), "USD")).toBe("Bought 1.00 EURUSD at 1.086.");
    expect(stepText(step({ kind: "PositionClosed", side: "Sell", price: 1.0864, amount: -35, reason: "EquityFloor" }), "USD")).toBe("Sell 1.00 EURUSD closed at 1.0864 (loss limit), -35.00 USD.");
    expect(stepText(step({ kind: "InputRejected", side: null, volume: null, price: null, reason: "StalePrice", request: "ClosePosition" }), "USD")).toBe(
      "A close on EURUSD was refused: no fresh price, so nothing was filled at an old one.",
    );
    expect(stepText(step({ kind: "EquityFloorBreached", symbol: null, side: null, volume: null, price: null, amount: 94_982.5, floorId: "daily", level: 95_000 }), "USD")).toBe(
      "Equity 94,982.50 fell below the daily loss limit at 95,000.00.",
    );
    expect(stepText(step({ kind: "AccountDisabled" }), "USD")).toBe("Trading on the account ended.");
  });

  it("marks the breach in red and refusals in amber", () => {
    expect([stepTone(step({ kind: "EquityFloorBreached" })), stepTone(step({ kind: "InputRejected" })), stepTone(step({}))]).toEqual(["loss", "warning", "muted"]);
  });
});

describe("the small charts", () => {
  const box = { width: 100, height: 50, padding: 5 };

  it("draws equity down to the breach, with the limit and the gap without prices", () => {
    const chart = equityChart(
      [
        { time: "2026-10-06T12:00:00Z", equity: 100 },
        { time: "2026-10-06T12:10:00Z", equity: 90 },
      ],
      95,
      [{ from: "2026-10-06T12:02:00Z", to: "2026-10-06T12:08:00Z" }],
      box,
    );

    expect(chart.line).toBe("0.0,5.0 100.0,45.0");
    expect(chart.levelY).toBeCloseTo(25);
    expect(chart.gaps[0].x).toBeCloseTo(20);
    expect(chart.gaps[0].width).toBeCloseTo(60);
    expect(chart.breach).toEqual({ x: 100, y: 45 });
  });
});

describe("prices", () => {
  it("show the decimals the instrument has, also when a price ends in zeros", () => {
    expect(priceDigits([0.6599, 0.65994, null])).toBe(5);
    expect(priceDigits([2641, 2641.3])).toBe(1);
    expect([priceText(0.6599, 5), priceText(2641.3, 2), priceText(1.1, 0)]).toEqual(["0.65990", "2,641.30", "1"]);
  });
});
