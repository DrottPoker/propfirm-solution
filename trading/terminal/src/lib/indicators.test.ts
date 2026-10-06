import { describe, expect, it } from "vitest";

import { bollinger, ema, macd, rsi, sma } from "./indicators";

const round = (series: (number | null)[], digits = 4) => series.map((v) => (v === null ? null : Number(v.toFixed(digits))));

describe("sma", () => {
  it("averages the last period values", () => {
    expect(sma([1, 2, 3, 4, 5], 3)).toEqual([null, null, 2, 3, 4]);
  });
});

describe("ema", () => {
  it("starts from the simple average and then weighs the newest value by 2 / (period + 1)", () => {
    // Weight 0.5: 2, then 4 * 0.5 + 2 * 0.5 = 3, then 5 * 0.5 + 3 * 0.5 = 4.
    expect(ema([1, 2, 3, 4, 5], 3)).toEqual([null, null, 2, 3, 4]);
    expect(round(ema([10, 10, 10, 20], 3))).toEqual([null, null, 10, 15]);
  });
});

describe("bollinger", () => {
  it("adds and takes away the standard deviation times the width", () => {
    const bands = bollinger([2, 4, 4, 4, 5, 5, 7, 9], 8, 2);

    // Mean 5 and population standard deviation 2.
    expect([bands.middle[7], bands.upper[7], bands.lower[7]]).toEqual([5, 9, 1]);
    expect(bands.upper[6]).toBeNull();
  });
});

describe("rsi", () => {
  it("is 100 when every move is up and 0 when every move is down", () => {
    expect(rsi([1, 2, 3, 4, 5], 3)).toEqual([null, null, null, 100, 100]);
    expect(rsi([5, 4, 3, 2, 1], 3)).toEqual([null, null, null, 0, 0]);
  });

  it("smooths the gains and losses as Wilder does", () => {
    // Moves +1, -1, +1, then -1: gains 2/3 and losses 1/3 over the first three, then (2/3 * 2 + 0) / 3 and (1/3 * 2 + 1) / 3.
    const values = rsi([10, 11, 10, 11, 10], 3);

    expect(round(values, 2)).toEqual([null, null, null, 66.67, 44.44]);
  });

  it("is in the middle when nothing moves", () => {
    expect(rsi([3, 3, 3, 3], 2)).toEqual([null, null, 50, 50]);
  });
});

describe("macd", () => {
  it("is the fast average minus the slow one, with a signal averaged from where it starts", () => {
    const closes = Array.from({ length: 40 }, (_, i) => 100 + i);
    const result = macd(closes, 12, 26, 9);

    // A steady rise of 1 a candle keeps the averages (period - 1) / 2 behind the close, so MACD is (25 - 11) / 2 = 7.
    expect(result.macd[24]).toBeNull();
    expect(result.macd[39]).toBeCloseTo(7, 6);
    expect(result.signal[25 + 7]).toBeNull();
    expect(result.signal[25 + 8]).toBeCloseTo(7, 6);
    expect(result.histogram[39]).toBeCloseTo(0, 6);
  });
});
