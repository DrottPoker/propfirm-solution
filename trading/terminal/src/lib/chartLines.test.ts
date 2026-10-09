import { describe, expect, it } from "vitest";

import { offscreenLines, priceRangeOf, type ChartLine } from "./chartLines";

const line = (price: number, label = String(price)): ChartLine => ({ price, label, color: "#fff" });

describe("offscreenLines", () => {
  it("tells the lines above and below the view, the nearest first", () => {
    const result = offscreenLines([line(1.13), line(1.125), line(1.1225), line(1.118), line(1.115)], 1.124, 1.12);

    expect(result.above.map((l) => l.price)).toEqual([1.125, 1.13]);
    expect(result.below.map((l) => l.price)).toEqual([1.118, 1.115]);
  });

  it("leaves out the lines in view", () => {
    expect(offscreenLines([line(1.122)], 1.124, 1.12)).toEqual({ above: [], below: [] });
  });
});

describe("priceRangeOf", () => {
  it("spans the lowest and the highest line", () => {
    expect(priceRangeOf([line(2), line(1), line(3)])).toEqual({ minValue: 1, maxValue: 3 });
  });

  it("is null without lines", () => {
    expect(priceRangeOf([])).toBeNull();
  });
});
