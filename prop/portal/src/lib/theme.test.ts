import { readFileSync } from "node:fs";

import { describe, expect, it } from "vitest";

import { defaultColors, themeColors, themeStyle } from "./theme";

describe("themeStyle", () => {
  it("turns the firm's colors into the theme's CSS variables", () => {
    expect(themeStyle({ colors: { accent: "#8b5cf6", loss: "#ff0000" } })).toEqual({
      "--accent": "#8b5cf6",
      "--loss": "#ff0000",
    });
  });

  it("ignores unknown names and anything that is not a plain hex color", () => {
    const colors = { accent: "red; background: url(x)", sidebar: "#000000", panel: "#12345" };

    expect(themeStyle({ colors })).toEqual({});
  });

  it("keeps the default look without a firm", () => {
    expect(themeStyle(null)).toEqual({});
  });
});

describe("defaultColors", () => {
  // The settings page shows them, so they must be the colors the portal really uses.
  it("are the colors in globals.css", () => {
    const css = readFileSync(new URL("../app/globals.css", import.meta.url), "utf8");

    for (const name of themeColors) {
      expect(css).toContain(`--${name}: ${defaultColors[name]};`);
    }
  });
});
