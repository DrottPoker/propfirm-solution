import { describe, expect, it } from "vitest";

import { mix, withAlpha } from "./colors";

describe("withAlpha", () => {
  it("keeps the color and adds the transparency", () => {
    expect(withAlpha("#34c38f", 0.5)).toBe("rgba(52, 195, 143, 0.5)");
    expect(withAlpha("#000000", 1)).toBe("rgba(0, 0, 0, 1)");
  });
});

describe("mix", () => {
  it("mixes two colors by weight", () => {
    expect(mix("#ffffff", "#000000", 0.5)).toBe("rgb(128, 128, 128)");
    expect(mix("#c9a35b", "#15171b", 1)).toBe("rgb(201, 163, 91)");
    expect(mix("#c9a35b", "#15171b", 0)).toBe("rgb(21, 23, 27)");
  });
});
