import { describe, expect, it } from "vitest";

import { guideSteps, stepAfter, stepOf } from "./getStarted";

describe("the guide for a new firm", () => {
  it("goes through the look, the price, the payments and trying it, and stays within its steps", () => {
    expect(guideSteps.map((s) => s.key)).toEqual(["look", "price", "payments", "try"]);
    expect(stepAfter("look", 1)).toBe("price");
    expect(stepAfter("look", -1)).toBe("look");
    expect(stepAfter("try", 1)).toBe("try");
  });

  it("starts at the step in the address, or the first", () => {
    expect(stepOf("payments")).toBe("payments");
    expect(stepOf("nonsense")).toBe("look");
    expect(stepOf(undefined)).toBe("look");
  });
});
