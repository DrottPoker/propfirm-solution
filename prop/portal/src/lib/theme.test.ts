import { readFileSync } from "node:fs";

import { describe, expect, it } from "vitest";

import { contrastRatio, defaultColors, presetOf, readableContrast, readableTextOn, themeColors, themePresets, themeStyle, withPreset } from "./theme";

describe("themeStyle", () => {
  it("turns the firm's colors into the theme's CSS variables", () => {
    expect(themeStyle({ colors: { accent: "#7c3aed", "accent-foreground": "#0b0e14", loss: "#ff0000" } })).toEqual({
      "--accent": "#7c3aed",
      "--accent-foreground": "#0b0e14",
      "--loss": "#ff0000",
    });
  });

  it("puts the easier text to read on a brand color without its own", () => {
    expect(themeStyle({ colors: { accent: "#7c3aed" } })).toEqual({ "--accent": "#7c3aed", "--accent-foreground": "#ffffff" });
    expect(themeStyle({ colors: { accent: "#ff8800" } })).toEqual({ "--accent": "#ff8800", "--accent-foreground": "#0b0e14" });
  });

  it("ignores unknown names and anything that is not a plain hex color", () => {
    const colors = { accent: "red; background: url(x)", sidebar: "#000000", panel: "#12345" };

    expect(themeStyle({ colors })).toEqual({});
  });

  it("keeps the default look without a firm", () => {
    expect(themeStyle(null)).toEqual({});
  });
});

describe("contrastRatio", () => {
  it("is 21 for black on white and 1 for a color on itself", () => {
    expect(contrastRatio("#000000", "#ffffff")).toBeCloseTo(21, 5);
    expect(contrastRatio("#3b82f6", "#3b82f6")).toBe(1);
  });

  it("is the same either way round", () => {
    expect(contrastRatio("#ffffff", "#3b82f6")).toBe(contrastRatio("#3b82f6", "#ffffff"));
  });

  // White on a lighter blue is below the 4.5 text needs, which is why the default is a darker one.
  it("finds white on a light blue hard to read and on the default blue easy", () => {
    expect(contrastRatio("#3b82f6", "#ffffff")).toBeCloseTo(3.68, 2);
    expect(contrastRatio(defaultColors.accent, defaultColors["accent-foreground"])).toBeGreaterThan(readableContrast);
  });
});

describe("readableTextOn", () => {
  it("is white on dark colors and dark on light ones", () => {
    expect(readableTextOn("#2563eb")).toBe("#ffffff");
    expect(readableTextOn("#2dd4bf")).toBe("#0b0e14");
    expect(readableTextOn("#ffffff")).toBe("#0b0e14");
  });
});

describe("withPreset", () => {
  const light = themePresets.find((p) => p.id === "light")!;
  const dark = themePresets.find((p) => p.id === "dark")!;

  it("takes the theme's surface colors and keeps the firm's brand color and the text on it", () => {
    const colors = withPreset({ accent: "#112233", "accent-foreground": "#000000", background: "#123456" }, light);

    expect(colors).toEqual({ ...light.colors, accent: "#112233", "accent-foreground": "#000000" });
  });

  it("goes back to the portal's own look with the dark theme", () => {
    expect(withPreset({ ...light.colors, accent: "#112233" }, dark)).toEqual({ accent: "#112233" });
  });

  it("is found again from the colors, unless one of them was changed on its own", () => {
    expect(presetOf({ accent: "#112233" })).toBe("dark");
    expect(presetOf(withPreset({}, light))).toBe("light");
    expect(presetOf({ ...light.colors, panel: "#fafafa" })).toBeNull();
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
