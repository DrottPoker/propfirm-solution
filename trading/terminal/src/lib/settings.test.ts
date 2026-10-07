import { describe, expect, it } from "vitest";

import { defaultCandleColors, parseColor, parseSetting, parseSoundVolume, presetOf } from "./settings";

describe("parseSetting", () => {
  it.each([
    ["on", false, true],
    ["off", true, false],
    [null, true, true],
    [null, false, false],
    ["yes", false, false],
  ])("reads %j with the default %j as %j", (raw, fallback, expected) => {
    expect(parseSetting(raw, fallback)).toBe(expected);
  });
});

describe("parseSoundVolume", () => {
  it.each([
    ["0", 0],
    ["35", 35],
    ["100", 100],
    ["101", 100],
    ["-5", 100],
    ["3.5", 100],
    ["loud", 100],
    [null, 100],
  ])("reads %j as %j", (raw, expected) => {
    expect(parseSoundVolume(raw)).toBe(expected);
  });
});

describe("parseColor", () => {
  it("keeps a six digit hex color in lower case, and gives the default for anything else", () => {
    expect(parseColor("#4C9BE8", "#000000")).toBe("#4c9be8");
    expect(parseColor("#fff", "#000000")).toBe("#000000");
    expect(parseColor("red", "#000000")).toBe("#000000");
    expect(parseColor(null, "#000000")).toBe("#000000");
  });
});

describe("presetOf", () => {
  it("names the ready-made pair, and gives null for the trader's own", () => {
    expect(presetOf(defaultCandleColors)).toBe("Green and red");
    expect(presetOf({ up: "#4c9be8", down: "#f0923a" })).toBe("Blue and orange");
    expect(presetOf({ up: "#4c9be8", down: "#ef5a50" })).toBeNull();
  });
});
