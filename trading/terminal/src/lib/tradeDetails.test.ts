import { describe, expect, it } from "vitest";

import type { ReceiptClose, ReceiptFill, TradeReceipt } from "./api/types";
import { detailsText, filledAtAsk, markupText, stopText } from "./tradeDetails";

const fill = (changes: Partial<ReceiptFill> = {}): ReceiptFill => ({
  at: "2026-10-06T12:02:11.284Z",
  volume: 1,
  price: 1.08723,
  commission: 3.5,
  feed: { inputSequence: 4_182_993, feed: "CapitalCom", bid: 1.08718, ask: 1.08721, receivedAt: "2026-10-06T12:02:11.100Z" },
  markupPoints: 2,
  prices: [],
  ...changes,
});

const receipt: TradeReceipt = {
  accountId: "A1",
  positionId: "4f2a9c1e",
  symbol: "EURUSD",
  side: "Buy",
  digits: 5,
  currency: "USD",
  order: null,
  opened: fill(),
  closes: [{ fill: fill({ at: "2026-10-06T13:10:03.512Z", price: 1.08524, markupPoints: 1 }), profit: -199, reason: "StopLoss", level: 1.0853 }],
  commission: 7,
  profit: -199,
  result: -206,
};

describe("filledAtAsk", () => {
  it("fills a buy's opening and a sell's close at the ask", () => {
    expect([filledAtAsk("Buy", true), filledAtAsk("Buy", false), filledAtAsk("Sell", true), filledAtAsk("Sell", false)]).toEqual([true, false, false, true]);
  });
});

describe("markupText", () => {
  it("names the points and the side, and nothing when unknown", () => {
    expect(markupText(fill(), true, 5)).toBe("0.2 pips on the ask");
    expect(markupText(fill({ markupPoints: 1 }), false, 5)).toBe("0.1 pips on the bid");
    expect(markupText(fill({ markupPoints: 10 }), false, 1, "points")).toBe("1 point on the bid");
    expect(markupText(fill({ markupPoints: null }), true, 5)).toBeNull();
  });
});

describe("stopText", () => {
  const close = (changes: Partial<ReceiptClose>, price = 1.08): ReceiptClose => ({ fill: fill({ price }), profit: 0, reason: "StopLoss", level: null, ...changes });

  it("says how far past its level a stop closed", () => {
    expect(stopText(receipt.closes[0], "Buy", 5)).toBe(
      "The stop loss was at 1.08530. The first bid at or below it was 1.08524, and a stop loss closes at the price that reaches it, here 6 points past the level.",
    );
    expect(stopText(close({ reason: "TakeProfit", level: 1.081 }, 1.081), "Sell", 5)).toBe("The take profit closed at its level, 1.08100.");
    expect(stopText(close({ reason: "TakeProfit", level: 1.081 }, 1.08098), "Sell", 5)).toContain("The first ask at or below it was 1.08098");
  });

  it("says nothing without a known level", () => {
    expect(stopText(close({ reason: "StopLoss", level: null }), "Buy", 5)).toBeNull();
    expect(stopText(close({ reason: "Manual", level: 1.08 }), "Buy", 5)).toBeNull();
  });
});

describe("detailsText", () => {
  it("has every fill with the feed's price and the result", () => {
    expect(detailsText(receipt, "Europe/Stockholm").split("\n")).toEqual([
      "Trade details: Buy 1.00 EURUSD, position 4f2a9c1e, account A1",
      "Opened 2026-10-06 14:02:11: 1.00 at 1.08723, commission 3.50 USD",
      "  Feed price bid 1.08718 ask 1.08721, journal entry 4182993, markup 0.2 pips on the ask",
      "Closed (stop loss) 2026-10-06 15:10:03: 1.00 at 1.08524, commission 3.50 USD",
      "  Feed price bid 1.08718 ask 1.08721, journal entry 4182993, markup 0.1 pips on the bid",
      "Result -206.00 USD after 7.00 commission",
    ]);
  });
});
