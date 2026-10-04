import { describe, expect, it } from "vitest";

import { balanceDomain, levelAt, linearScale, nearestIndex, niceTicks, stepPath, timeAxis, timeTicks, valueDomain } from "./chart";

describe("niceTicks", () => {
  it("picks clean values inside the range", () => {
    expect(niceTicks(95_200, 104_900, 5)).toEqual([97_500, 100_000, 102_500]);
    expect(niceTicks(0, 10, 6)).toEqual([0, 2, 4, 6, 8, 10]);
  });

  it("has a single value for an empty range", () => {
    expect(niceTicks(100, 100)).toEqual([100]);
  });
});

describe("valueDomain", () => {
  it("holds every value with a little room above and below", () => {
    expect(valueDomain([100, 200], 10)).toEqual([92, 208]);
  });

  it("centres a flat line in a range of at least the minimum span", () => {
    expect(valueDomain([100_000, 100_000], 2_000)).toEqual([98_840, 101_160]);
  });
});

describe("stepPath", () => {
  it("holds each balance until the next change, and the last one to the end", () => {
    const x = linearScale([0, 10], [0, 100]);

    expect(stepPath([{ x: x(0), y: 50 }, { x: x(4), y: 40 }, { x: x(6), y: 45 }], x(10))).toBe("M0 50H40V40H60V45H100");
    expect(stepPath([], 100)).toBe("");
  });
});

describe("nearestIndex", () => {
  it("finds the closest time, the earlier one on a tie", () => {
    const times = [0, 10, 20];

    expect([nearestIndex(times, -5), nearestIndex(times, 4), nearestIndex(times, 5), nearestIndex(times, 16), nearestIndex(times, 99)]).toEqual([0, 0, 0, 2, 2]);
    expect(nearestIndex([], 3)).toBe(-1);
  });
});

describe("levelAt", () => {
  it("is the level set last at or before the time", () => {
    const levels = [
      { time: 10, level: 95_000 },
      { time: 20, level: 97_000 },
    ];

    expect([levelAt(levels, 5), levelAt(levels, 10), levelAt(levels, 19), levelAt(levels, 25)]).toEqual([null, 95_000, 95_000, 97_000]);
  });
});

describe("timeTicks", () => {
  it("spreads times evenly from start to end", () => {
    expect(timeTicks(0, 100, 5)).toEqual([0, 25, 50, 75, 100]);
    expect(timeTicks(5, 5, 5)).toEqual([5]);
  });
});

describe("balanceDomain", () => {
  const near = { balances: [100_000, 101_000], equity: 101_200, target: 105_000, daily: [96_000], minSpan: 2_000 };

  it("holds the max loss limit when it does not flatten the rest", () => {
    const { domain, maxLossShown } = balanceDomain({ ...near, maxLoss: [94_000] });

    expect(maxLossShown).toBe(true);
    expect(domain[0]).toBeLessThan(94_000);
  });

  it("leaves a far max loss limit out of the chart", () => {
    const { domain, maxLossShown } = balanceDomain({ ...near, maxLoss: [80_000] });

    expect(maxLossShown).toBe(false);
    expect(domain[0]).toBeGreaterThan(90_000);
  });
});

describe("timeAxis", () => {
  const start = Date.parse("2026-10-05T08:00:00Z");

  it("shows seconds when everything happened within minutes", () => {
    expect(timeAxis(start, start + 60_000, 3, "UTC").map((t) => t.label)).toEqual(["08:00:00", "08:00:30", "08:01:00"]);
  });

  it("shows the day once the range is days long, and never the same label twice", () => {
    expect(timeAxis(start, start + 5 * 86_400_000, 5, "UTC").map((t) => t.label)).toEqual(["5 Oct", "6 Oct", "7 Oct", "9 Oct", "10 Oct"]);
    expect(timeAxis(start, start + 4.5 * 86_400_000, 9, "UTC").map((t) => t.label)).toEqual(["5 Oct", "6 Oct", "7 Oct", "8 Oct", "9 Oct"]);
  });

  it("shows the day and time between a day and four", () => {
    expect(timeAxis(start, start + 2 * 86_400_000, 3, "UTC").map((t) => t.label)).toEqual(["5 Oct, 08:00", "6 Oct, 08:00", "7 Oct, 08:00"]);
  });
});
