import { describe, expect, it } from "vitest";

import { partsShown } from "./kinds";

const none = { rulebook: false, ownLimits: false, riskSizing: false, tradeDetails: false, breachReports: false };

describe("partsShown", () => {
  it("lists the parts a terminal shows in a sentence", () => {
    expect(partsShown({ ...none, rulebook: true, riskSizing: true, tradeDetails: true })).toBe("The firm's rules, sizing from risk and trade details");
    expect(partsShown({ ...none, ownLimits: true })).toBe("The trader's own limits");
  });

  it("says when the terminal is only for trading", () => {
    expect(partsShown(none)).toBe("Only the trading");
  });
});
