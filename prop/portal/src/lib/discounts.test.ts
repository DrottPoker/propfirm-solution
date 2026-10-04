import { describe, expect, it } from "vitest";

import { discountStatus, discountText, endOfDay } from "./discounts";

describe("discount codes", () => {
  it("says what a code takes off", () => {
    expect(discountText({ percentOff: 20, amountOff: null, currency: null })).toBe("20% off");
    expect(discountText({ percentOff: null, amountOff: 50, currency: "USD" })).toBe("50.00 USD off");
  });

  it("says whether buyers can use a code now", () => {
    const now = Date.parse("2026-10-05T12:00:00Z");
    const code = { active: true, expiresAt: null, maxUses: null, uses: 3 };
    expect(discountStatus(code, now).label).toBe("Active");
    expect(discountStatus({ ...code, active: false }, now).label).toBe("Off");
    expect(discountStatus({ ...code, expiresAt: "2026-10-05T11:59:59Z" }, now).label).toBe("Ended");
    expect(discountStatus({ ...code, maxUses: 3 }, now).label).toBe("Used up");
  });

  it("ends a code at the end of the day picked", () => {
    const end = new Date(endOfDay("2026-10-31"));
    expect([end.getFullYear(), end.getMonth(), end.getDate(), end.getHours(), end.getMinutes()]).toEqual([2026, 9, 31, 23, 59]);
  });
});
