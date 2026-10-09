import { describe, expect, it } from "vitest";

import { timeframeOfKey } from "./shortcuts";

describe("shortcuts", () => {
  it("chooses the timeframes in order with the digits", () => {
    expect(timeframeOfKey("1")).toBe("M1");
    expect(timeframeOfKey("7")).toBe("D1");
    expect(timeframeOfKey("9")).toBe("MN");
    expect(timeframeOfKey("0")).toBeNull();
    expect(timeframeOfKey("a")).toBeNull();
  });
});
