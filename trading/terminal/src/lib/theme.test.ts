import { describe, expect, it } from "vitest";

import { hostWithoutPort, themeStyle } from "./theme";

describe("themeStyle", () => {
  it("turns the firm's colors into CSS variables", () => {
    const style = themeStyle({ displayName: "Firm", logoUrl: null, colors: { accent: "#ff8800", panel: "#101010" } });

    expect(style).toEqual({ "--accent": "#ff8800", "--panel": "#101010" });
  });

  it("ignores unknown names and anything that is not a hex color", () => {
    const style = themeStyle({
      displayName: "Firm",
      logoUrl: null,
      colors: { accent: "red; background: url(x)", unknown: "#ffffff", buy: "#00ff00" },
    });

    expect(style).toEqual({ "--buy": "#00ff00" });
  });

  it("has no variables without branding", () => {
    expect(themeStyle(null)).toEqual({});
  });
});

describe("hostWithoutPort", () => {
  it.each([
    ["localhost:3001", "localhost"],
    ["trade.firm.example", "trade.firm.example"],
    ["[::1]:3001", "[::1]"],
    [null, null],
  ])("%j becomes %j", (host, expected) => {
    expect(hostWithoutPort(host)).toBe(expected);
  });
});
