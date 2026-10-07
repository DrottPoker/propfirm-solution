import { describe, expect, it } from "vitest";

import { loudness } from "./sound";

describe("loudness", () => {
  it("is the square of the volume's share, within 0 and 100", () => {
    expect(loudness(100)).toBe(1);
    expect(loudness(50)).toBe(0.25);
    expect(loudness(0)).toBe(0);
    expect(loudness(150)).toBe(1);
    expect(loudness(-10)).toBe(0);
  });
});
