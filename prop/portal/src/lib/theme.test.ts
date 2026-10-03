import { describe, expect, it } from "vitest";

import { themeStyle } from "./theme";

describe("themeStyle", () => {
  it("turns the firm's colors into the theme's CSS variables", () => {
    expect(themeStyle({ name: "Firm", logoUrl: null, colors: { accent: "#8b5cf6", loss: "#ff0000" } })).toEqual({
      "--accent": "#8b5cf6",
      "--loss": "#ff0000",
    });
  });

  it("ignores unknown names and anything that is not a plain hex color", () => {
    const colors = { accent: "red; background: url(x)", sidebar: "#000000", panel: "#12345" };

    expect(themeStyle({ name: "Firm", logoUrl: null, colors })).toEqual({});
  });

  it("keeps the default look without a firm", () => {
    expect(themeStyle(null)).toEqual({});
  });
});
