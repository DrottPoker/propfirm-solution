import { describe, expect, it } from "vitest";

import {
  feedInText,
  feedName,
  formatAgo,
  formatBytes,
  formatDuration,
  formatLarge,
  formatMilliseconds,
  formatPrice,
  formatSignedLots,
  formatSignedWhole,
  formatWhen,
} from "./format";

describe("format", () => {
  it("shows large amounts in millions and thousands", () => {
    expect(formatLarge(32_123_456)).toBe("32.1 M");
    expect(formatLarge(-9_200_000)).toBe("-9.2 M");
    expect(formatLarge(410_000)).toBe("410 K");
    expect(formatLarge(9_876)).toBe("9,876");
    expect(formatLarge(2_500_000_000)).toBe("2.5 B");
  });

  it("shows signs only where they say something", () => {
    expect(formatSignedLots(121.2)).toBe("+121.20");
    expect(formatSignedLots(-84.5)).toBe("-84.50");
    expect(formatSignedLots(0)).toBe("0.00");
    expect(formatSignedWhole(184_210.4)).toBe("+184,210");
  });

  it("shows a price with the symbol's decimals, grouped", () => {
    expect(formatPrice(2661.4, 2)).toBe("2,661.40");
    expect(formatPrice(1.08412, 5)).toBe("1.08412");
    expect(formatPrice(null, 2)).toBe("-");
  });

  it("says how long ago in the fewest words", () => {
    const now = new Date("2026-10-08T14:32:00Z");
    expect(formatAgo("2026-10-08T14:31:59.700Z", now)).toBe("0.3 s ago");
    expect(formatAgo("2026-10-08T14:27:39Z", now)).toBe("4 min 21 s ago");
    expect(formatAgo("2026-10-08T12:30:00Z", now)).toBe("2 h 2 min ago");
    expect(formatAgo("2026-10-05T14:32:00Z", now)).toBe("3 days ago");
  });

  it("gives durations and sizes in their units", () => {
    expect(formatDuration(45)).toBe("45 s");
    expect(formatDuration(240)).toBe("4 min");
    expect(formatDuration(3_600)).toBe("1 h");
    expect(formatDuration(86_400)).toBe("1 day");
    expect(formatBytes(44_023_414_784)).toBe("41 GB");
    expect(formatBytes(1_536)).toBe("1.5 KB");
    expect(formatMilliseconds(3.04)).toBe("3 ms");
    expect(formatMilliseconds(11.6)).toBe("12 ms");
  });

  it("says when in UTC, with the day only when it is not today", () => {
    const now = new Date("2026-10-08T14:32:00Z");
    expect(formatWhen("2026-10-08T09:41:00Z", now)).toBe("09:41");
    expect(formatWhen("2026-10-07T14:52:00Z", now)).toBe("Yesterday 14:52");
    expect(formatWhen("2026-10-05T08:00:00Z", now)).toBe("Mon 08:00");
    expect(formatWhen("2025-12-24T09:12:00Z", now)).toBe("24 Dec 2025");
  });

  it("names the feeds as people know them", () => {
    expect(feedName("CapitalCom")).toBe("Capital.com");
    expect(feedName("Synthetic")).toBe("Made-up prices");
    expect(feedName("Manual")).toBe("Manual");
    expect(feedInText("Synthetic")).toBe("made-up prices");
    expect(feedInText("CapitalCom")).toBe("Capital.com");
  });
});
