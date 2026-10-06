import { describe, expect, it } from "vitest";

import { parseSetting } from "./settings";

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
