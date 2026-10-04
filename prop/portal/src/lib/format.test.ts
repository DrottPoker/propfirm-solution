import { describe, expect, it } from "vitest";

import { dayBefore, daysBetween, formatAxisMoney, formatDay, formatLots, formatPrice, formatSignedMoney, formatSignedPercent } from "./format";

describe("formatting", () => {
  it("shows results with their sign and zero without", () => {
    expect([formatSignedMoney(3_655.2), formatSignedMoney(-1_769.6), formatSignedMoney(0), formatSignedMoney(null)]).toEqual(["+3,655.20", "-1,769.60", "0.00", "-"]);
    expect([formatSignedPercent(3.66), formatSignedPercent(-3.5)]).toEqual(["+3.66%", "-3.50%"]);
  });

  it("keeps the decimals of prices and gives lots two at least", () => {
    expect([formatPrice(1.08412), formatPrice(2_412.5), formatLots(0.2), formatLots(1.255)]).toEqual(["1.08412", "2,412.5", "0.20", "1.255"]);
  });

  it("rounds axis levels to whole numbers", () => {
    expect(formatAxisMoney(102_500)).toBe("102,500");
  });

  it("shows trading days the same in every time zone", () => {
    expect(formatDay("2026-10-05")).toBe("Mon 5 Oct");
  });

  it("counts days between plain dates", () => {
    expect([daysBetween("2026-10-06", "2026-11-02"), daysBetween("2026-10-06", "2026-10-06"), daysBetween("2026-10-06", "2026-10-05")]).toEqual([27, 0, -1]);
    expect(dayBefore("2026-11-01")).toBe("2026-10-31");
  });
});
