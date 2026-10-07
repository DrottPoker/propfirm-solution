import { describe, expect, it } from "vitest";

import { equityChart } from "./miniCharts";

const box = { width: 100, height: 50, padding: 5 };

describe("equityChart", () => {
  it("draws equity, the limit, the gaps and the breach at the end", () => {
    const chart = equityChart(
      [
        { time: "2026-10-06T12:00:00Z", equity: 100 },
        { time: "2026-10-06T12:01:00Z", equity: 98 },
        { time: "2026-10-06T12:04:00Z", equity: 90 },
      ],
      95,
      [{ from: "2026-10-06T12:01:00Z", to: "2026-10-06T12:04:00Z" }],
      box,
    );

    expect(chart.line).toBe("0.0,5.0 25.0,13.0 100.0,45.0");
    expect(chart.levelY).toBe(25);
    expect(chart.gaps).toEqual([{ x: 25, width: 75 }]);
    expect(chart.breach).toEqual({ x: 100, y: 45 });
  });
});
