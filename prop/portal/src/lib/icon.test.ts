import { describe, expect, it } from "vitest";

import { monogramIcon } from "./icon";

function svgOf(url: string): string {
  expect(url.startsWith("data:image/svg+xml,")).toBe(true);
  return decodeURIComponent(url.slice("data:image/svg+xml,".length));
}

describe("monogramIcon", () => {
  it("draws the name's first letter on the brand color, with the easier text to read on it", () => {
    const svg = svgOf(monogramIcon("aurora Funded", "#2563eb"));

    expect(svg).toContain('fill="#2563eb"');
    expect(svg).toContain('fill="#ffffff">A</text>');
    expect(svgOf(monogramIcon("Nordic", "#2dd4bf"))).toContain('fill="#0b0e14">N</text>');
  });

  it("escapes the letter and falls back on the default blue for a color that is not a plain hex color", () => {
    const svg = svgOf(monogramIcon("<script>", "red"));

    expect(svg).toContain("&#60;</text>");
    expect(svg).toContain('fill="#2563eb"');
  });
});
