import { describe, expect, it } from "vitest";

import {
  formatChartTick,
  formatDateTime,
  formatSignedMoney,
  formatSignedPercent,
  formatSignedPrice,
  formatTime,
  formatUnits,
  timeZoneName,
  usableTimeZone,
} from "./format";

describe("formatSignedPrice", () => {
  it.each([
    [0.00312, 5, "+0.00312"],
    [-0.00312, 5, "-0.00312"],
    [0, 5, "0.00000"],
    [-0.000001, 5, "0.00000"],
    [1.5, 2, "+1.50"],
  ])("formats %d with %i digits as %j", (value, digits, expected) => {
    expect(formatSignedPrice(value, digits)).toBe(expected);
  });
});

describe("formatSignedMoney", () => {
  it.each([
    [4, "+4.00"],
    [-1234.5, "-1,234.50"],
    [0, "0.00"],
  ])("formats %d as %j", (value, expected) => {
    expect(formatSignedMoney(value)).toBe(expected);
  });
});

describe("formatSignedPercent", () => {
  it("shows the sign and a dash without a value", () => {
    expect(formatSignedPercent(0.2912)).toBe("+0.29%");
    expect(formatSignedPercent(-0.14)).toBe("-0.14%");
    expect(formatSignedPercent(null)).toBe("-");
  });
});

describe("formatUnits", () => {
  it("hides floating point noise", () => {
    expect(formatUnits(0.07 * 100_000)).toBe("7,000");
  });
});

describe("times in a time zone", () => {
  const time = new Date(Date.UTC(2026, 9, 5, 14, 12, 34));

  it("shows the date and time in the zone", () => {
    expect(formatDateTime(time, "UTC")).toBe("2026-10-05 14:12:34");
    expect(formatDateTime(time, "Europe/Stockholm")).toBe("2026-10-05 16:12:34");
    expect(formatTime(time.toISOString(), "America/New_York")).toBe("10:12:34");
  });

  it("names chart tick marks in the zone", () => {
    const seconds = Date.UTC(2026, 9, 5, 22, 30) / 1000;
    expect(formatChartTick(seconds, "time", "Europe/Stockholm")).toBe("00:30");
    expect(formatChartTick(seconds, "day", "Europe/Stockholm")).toBe("6");
    expect(formatChartTick(seconds, "month", "UTC")).toBe("Oct");
    expect(formatChartTick(seconds, "year", "UTC")).toBe("2026");
  });

  it.each([
    ["Europe/Stockholm", "Stockholm time"],
    ["America/New_York", "New York time"],
    ["UTC", "UTC"],
  ])("names %j %j", (zone, expected) => {
    expect(timeZoneName(zone)).toBe(expected);
  });

  it("falls back to UTC for a zone the browser does not know", () => {
    expect(usableTimeZone("Europe/Stockholm")).toBe("Europe/Stockholm");
    expect(usableTimeZone("Mars/Olympus")).toBe("UTC");
    expect(usableTimeZone(null)).toBe("UTC");
  });
});
