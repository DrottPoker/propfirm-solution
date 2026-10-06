import { describe, expect, it } from "vitest";

import { edgeLuminances, logoHardToSee } from "./logoContrast";

// An image of rows of pixels, each "." transparent, "w" white, "b" blue and "k" black.
function image(rows: string[]): { pixels: number[]; width: number; height: number } {
  const colors: Record<string, number[]> = { ".": [0, 0, 0, 0], w: [255, 255, 255, 255], b: [14, 165, 233, 255], k: [0, 0, 0, 255] };
  return { pixels: rows.flatMap((row) => [...row].flatMap((c) => colors[c])), width: rows[0].length, height: rows.length };
}

function edgesOf(rows: string[]): number[] | null {
  const { pixels, width, height } = image(rows);
  return edgeLuminances(pixels, width, height);
}

describe("a logo on the portal's background", () => {
  it("is judged by its outline, not by details inside its own shape", () => {
    // A blue square with a white mark inside: only blue meets the background.
    const edges = edgesOf(["......", ".bbbb.", ".bwwb.", ".bwwb.", ".bbbb.", "......"])!;

    expect(edges).toHaveLength(12);
    expect(logoHardToSee(edges, "#ffffff")).toBe(false);
    expect(logoHardToSee(edges, "#0b0e14")).toBe(false);
  });

  // A colored mark beside white text, as many logos are, disappears in part on a light theme.
  it("is hard to see when much of its outline blends into the background", () => {
    const edges = edgesOf(["...........", ".bbb.ww.ww.", ".bbb.ww.ww.", ".bbb.ww.ww.", "..........."])!;

    expect(logoHardToSee(edges, "#ffffff")).toBe(true);
    expect(logoHardToSee(edges, "#11151c")).toBe(false);
    expect(logoHardToSee(edgesOf(["....", ".kk.", ".kk.", "...."])!, "#0b0e14")).toBe(true);
  });

  it("is not judged without transparent pixels, since its own background is what shows", () => {
    expect(edgesOf(["ww", "ww"])).toBeNull();
    expect(edgesOf(["..", ".."])).toBeNull();
  });
});
