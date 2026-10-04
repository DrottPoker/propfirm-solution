import { describe, expect, it } from "vitest";

import { averageLuminance, logoHardToSee } from "./logoContrast";

describe("a logo on the portal's background", () => {
  it("is averaged over its visible pixels", () => {
    // A white pixel, a black one and a transparent one.
    expect(averageLuminance([255, 255, 255, 255, 0, 0, 0, 255, 255, 0, 0, 0])).toBeCloseTo(0.5, 5);
    expect(averageLuminance([255, 255, 255, 0])).toBeNull();
  });

  it("is hard to see when it is as light or dark as the background", () => {
    expect(logoHardToSee(1, "#f5f6f8")).toBe(true);
    expect(logoHardToSee(1, "#0b0e14")).toBe(false);
    expect(logoHardToSee(0.002, "#0b0e14")).toBe(true);
    expect(logoHardToSee(0.2, "#0b0e14")).toBe(false);
    expect(logoHardToSee(0.2, "#f5f6f8")).toBe(false);
  });
});
