import { describe, expect, it } from "vitest";

import {
  byDay,
  dayHeading,
  formatAt,
  formatChartTick,
  formatDateTime,
  formatSignedMoney,
  formatSignedPercent,
  formatSignedPrice,
  formatTime,
  formatUnits,
  formatWhen,
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

describe("days relative to now", () => {
  // Friday 9 October 2026, 08:30 UTC, 10:30 in Stockholm.
  const now = new Date(Date.UTC(2026, 9, 9, 8, 30));

  it("tells today's times by the clock and earlier ones with their day", () => {
    expect(formatWhen("2026-10-09T06:37:12Z", "UTC", now)).toBe("06:37");
    expect(formatWhen("2026-10-09T06:37:12Z", "UTC", now, true)).toBe("06:37:12");
    expect(formatWhen("2026-10-08T21:17:00Z", "UTC", now)).toBe("Yesterday 21:17");
    expect(formatWhen("2026-10-03T06:50:57Z", "UTC", now)).toBe("3 Oct 06:50");
    expect(formatWhen("2025-12-30T10:00:00Z", "UTC", now)).toBe("30 Dec 2025 10:00");
  });

  it("says when in a sentence", () => {
    expect(formatAt("2026-10-09T06:50:57Z", "UTC", now)).toBe("today at 06:50:57");
    expect(formatAt("2026-10-08T21:17:00Z", "UTC", now)).toBe("yesterday at 21:17:00");
    expect(formatAt("2026-10-03T06:50:57Z", "UTC", now)).toBe("on 3 Oct at 06:50:57");
  });

  it("counts days in the time zone", () => {
    // 22:30 UTC on 8 October is already 9 October in Stockholm.
    expect(formatWhen("2026-10-08T22:30:00Z", "Europe/Stockholm", now)).toBe("00:30");
    expect(formatWhen("2026-10-08T22:30:00Z", "UTC", now)).toBe("Yesterday 22:30");
  });

  it("heads each day's rows", () => {
    expect(dayHeading("2026-10-09T01:00:00Z", "UTC", now)).toBe("Today");
    expect(dayHeading("2026-10-08T01:00:00Z", "UTC", now)).toBe("Yesterday");
    expect(dayHeading("2026-10-03T01:00:00Z", "UTC", now)).toBe("Saturday 3 October");
    expect(dayHeading("2025-10-03T01:00:00Z", "UTC", now)).toBe("Friday 3 October 2025");
  });

  it("groups rows by their day in the order given", () => {
    const rows = ["2026-10-09T07:00:00Z", "2026-10-09T01:00:00Z", "2026-10-07T23:00:00Z", "2026-10-07T01:00:00Z"];
    expect(byDay(rows, (r) => r, "UTC", now).map((g) => [g.heading, g.rows.length])).toEqual([
      ["Today", 2],
      ["Wednesday 7 October", 2],
    ]);
  });
});
