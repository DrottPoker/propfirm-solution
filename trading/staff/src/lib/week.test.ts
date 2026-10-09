import { describe, expect, it } from "vitest";

import { weekLabel, weekSegments } from "./week";

const weekStart = "2026-10-04T00:00:00Z";

describe("week", () => {
  it("cuts a market's periods into the days of the week", () => {
    // Forex: Sunday 21:00 to Friday 21:00 UTC.
    const week = weekSegments(weekStart, [{ opens: "2026-10-04T21:00:00Z", closes: "2026-10-09T21:00:00Z" }]);

    expect(week[0]).toEqual([{ from: 21 / 24, to: 1 }]);
    expect(week[3]).toEqual([{ from: 0, to: 1 }]);
    expect(week[5]).toEqual([{ from: 0, to: 21 / 24 }]);
    expect(week[6]).toEqual([]);
    expect(weekLabel(week)).toBe("Open Sunday to Friday");
  });

  it("shows a daily break as two parts of the day", () => {
    const week = weekSegments(weekStart, [
      { opens: "2026-10-05T00:00:00Z", closes: "2026-10-05T21:00:00Z" },
      { opens: "2026-10-05T22:00:00Z", closes: "2026-10-06T00:00:00Z" },
    ]);

    expect(week[1]).toEqual([
      { from: 0, to: 21 / 24 },
      { from: 22 / 24, to: 1 },
    ]);
    expect(weekLabel(week)).toBe("Open Monday");
  });

  it("names a market that never closes, and one closed all week", () => {
    expect(weekLabel(weekSegments(weekStart, [{ opens: weekStart, closes: "2026-10-11T00:00:00Z" }]))).toBe("Open all week");
    expect(weekLabel(weekSegments(weekStart, []))).toBe("Closed all week");
  });
});
