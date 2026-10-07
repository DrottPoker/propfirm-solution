import { describe, expect, it } from "vitest";

import {
  dayBefore,
  daysBetween,
  formatAxisMoney,
  formatClock,
  formatDate,
  formatDateTime,
  formatDay,
  formatLots,
  formatPrice,
  formatShortDateTime,
  formatSignedMoney,
  formatSignedPercent,
  priceText,
  timeZoneName,
  wholeAmount,
} from "./format";

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

  it("shows an account's times in its challenge's time zone and names the zone", () => {
    const time = "2026-10-05T22:38:00Z";
    expect(formatDateTime(time, "Europe/Stockholm")).toBe("6 Oct 2026, 00:38");
    expect(formatShortDateTime(time, "UTC")).toBe("5 Oct, 22:38");
    expect(formatClock(time, "Europe/Stockholm")).toBe("00:38");
    expect(formatDate(time, "Europe/Stockholm")).toBe("6 Oct 2026");
    expect([timeZoneName("Europe/Stockholm"), timeZoneName("America/New_York"), timeZoneName("UTC")]).toEqual(["Stockholm time", "New York time", "UTC"]);
  });

  it("counts days between plain dates", () => {
    expect([daysBetween("2026-10-06", "2026-11-02"), daysBetween("2026-10-06", "2026-10-06"), daysBetween("2026-10-06", "2026-10-05")]).toEqual([27, 0, -1]);
    expect(dayBefore("2026-11-01")).toBe("2026-10-31");
  });
});

describe("priceText and wholeAmount", () => {
  it("write prices and amounts without cents when they have none", () => {
    expect([priceText(349, "USD"), priceText(199.5, "EUR"), wholeAmount(5_000), wholeAmount(2_500.5)]).toEqual(["$349", "€199.50", "5,000", "2,500.50"]);
  });
});
