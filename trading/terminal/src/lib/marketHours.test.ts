import { describe, expect, it } from "vitest";

import type { MarketHours } from "./api/types";
import { formatDayTime } from "./format";
import { closingSoon, isClosed, marketRefreshDelay, opensText, sessionLines } from "./marketHours";

const now = Date.parse("2026-10-05T08:00:00Z");

function market(overrides: Partial<MarketHours>): MarketHours {
  return { symbol: "EURUSD", isOpen: true, nextChange: null, sessions: null, ...overrides };
}

describe("marketRefreshDelay", () => {
  it("waits until a second after the next market opens or closes", () => {
    const markets = [market({ nextChange: "2026-10-05T08:10:00Z" }), market({ symbol: "DE40", nextChange: "2026-10-05T08:05:00Z" })];

    expect(marketRefreshDelay(markets, now)).toBe(5 * 60_000 + 1_000);
  });

  it("asks again at least every hour, also when no market changes", () => {
    expect(marketRefreshDelay([market({ nextChange: "2026-10-09T21:00:00Z" })], now)).toBe(60 * 60_000);
    expect(marketRefreshDelay([market({ symbol: "BTCUSD" })], now)).toBe(60 * 60_000);
    expect(marketRefreshDelay([], now)).toBe(60 * 60_000);
  });

  it("waits a second when a change has already passed", () => {
    expect(marketRefreshDelay([market({ nextChange: "2026-10-05T07:59:00Z" })], now)).toBe(1_000);
  });
});

describe("isClosed", () => {
  it("is closed only when the service said so", () => {
    expect(isClosed(market({ isOpen: false }))).toBe(true);
    expect(isClosed(market({ isOpen: true }))).toBe(false);
    expect(isClosed(undefined)).toBe(false);
  });
});

describe("opensText", () => {
  it("says when the market opens in the account's time zone", () => {
    const closed = market({ isOpen: false, nextChange: "2026-10-11T21:00:00Z" });

    expect(opensText(closed, "Europe/Stockholm", "Stockholm time")).toBe("Opens Sun 11 Oct 23:00 Stockholm time");
  });

  it("says so when the market does not open within a month", () => {
    expect(opensText(market({ isOpen: false }), "Europe/Stockholm", "Stockholm time")).toBe("Closed until further notice");
  });
});

describe("closingSoon", () => {
  const open = market({ nextChange: "2026-10-09T21:00:00Z" });

  it("warns half an hour before the market closes", () => {
    expect(closingSoon(open, new Date("2026-10-09T20:48:30Z"), "Europe/Stockholm")).toEqual({ minutes: 12, at: "23:00" });
    expect(closingSoon(open, new Date("2026-10-09T20:30:00Z"), "Europe/Stockholm")).toEqual({ minutes: 30, at: "23:00" });
  });

  it("is quiet earlier, after the close, for closed markets and before the clock runs", () => {
    expect(closingSoon(open, new Date("2026-10-09T20:29:59Z"), "Europe/Stockholm")).toBeNull();
    expect(closingSoon(open, new Date("2026-10-09T21:00:00Z"), "Europe/Stockholm")).toBeNull();
    expect(closingSoon({ ...open, isOpen: false }, new Date("2026-10-09T20:50:00Z"), "Europe/Stockholm")).toBeNull();
    expect(closingSoon(open, null, "Europe/Stockholm")).toBeNull();
  });
});

describe("sessionLines", () => {
  it("shows a session within a day with one date, and a longer one with both", () => {
    const sessions = [
      { opens: "2026-10-04T22:00:00Z", closes: "2026-10-05T21:00:00Z" },
      { opens: "2026-10-11T21:00:00Z", closes: "2026-10-16T21:00:00Z" },
    ];

    expect(sessionLines(sessions, "Europe/Stockholm")).toEqual(["Mon 5 Oct 00:00 - 23:00", "Sun 11 Oct 23:00 - Fri 16 Oct 23:00"]);
  });
});

describe("formatDayTime", () => {
  it("writes the day and time in the time zone", () => {
    expect(formatDayTime("2026-10-04T22:00:00Z", "Europe/Stockholm")).toBe("Mon 5 Oct 00:00");
    expect(formatDayTime("2026-10-04T22:00:00Z", "UTC")).toBe("Sun 4 Oct 22:00");
  });
});
