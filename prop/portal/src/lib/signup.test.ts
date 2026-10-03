import { describe, expect, it } from "vitest";

import { isValidFirmId, portalAddress, suggestFirmId } from "./signup";

describe("suggestFirmId", () => {
  it.each([
    ["Nordic Prop AB", "nordic-prop-ab"],
    ["  Åre Trading  ", "are-trading"],
    ["Acme & Co.", "acme-co"],
    ["---", ""],
    ["A".repeat(39) + " B", "a".repeat(39)],
  ])("makes %j into %j", (name, expected) => {
    expect(suggestFirmId(name)).toBe(expected);
  });

  it("always suggests a valid name when there are letters to use", () => {
    expect(isValidFirmId(suggestFirmId("Some Firm Name With Many Words In It That Goes On And On"))).toBe(true);
  });
});

describe("isValidFirmId", () => {
  it.each(["ab", "acme", "acme-prop-2", "a".repeat(40)])("accepts %j", (id) => {
    expect(isValidFirmId(id)).toBe(true);
  });

  it.each(["a", "Acme", "-acme", "acme-", "acme_prop", "a".repeat(41), "acme prop"])("refuses %j", (id) => {
    expect(isValidFirmId(id)).toBe(false);
  });
});

describe("portalAddress", () => {
  it("puts the short name in the template", () => {
    expect(portalAddress("https://{firm}.example.com/", "acme")).toBe("https://acme.example.com/");
  });

  it("shows a placeholder before a name is chosen", () => {
    expect(portalAddress("https://{firm}.example.com/", "")).toBe("https://your-firm.example.com/");
  });
});
