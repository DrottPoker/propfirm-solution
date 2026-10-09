// Colors for the chart, which draws on a canvas and needs them as values rather than CSS variables.

/** The colors the chart draws with, the same as the terminal's CSS for the theme. */
export interface Palette {
  panel: string;
  raised: string;
  border: string;
  foreground: string;
  muted: string;
  brass: string;
  profit: string;
  loss: string;
}

/**
 * Kronant's colors from @kronant/design (shared/web/design/tokens.css) for the dark theme, and the light theme's
 * (ADR 0058), the same as in the terminal's globals.css: warm paper surfaces, near-black text, and brass, green and red
 * a little darker, so they read on white.
 */
export const palettes: Record<"dark" | "light", Palette> = {
  dark: {
    panel: "#15171b",
    raised: "#1c1f25",
    border: "#272a31",
    foreground: "#ece7df",
    muted: "#8f8a81",
    brass: "#c9a35b",
    profit: "#34c38f",
    loss: "#ef5a50",
  },
  light: {
    panel: "#fbfaf7",
    raised: "#efece6",
    border: "#dfd9cf",
    foreground: "#1d1c1a",
    muted: "#6e695f",
    brass: "#a8823c",
    profit: "#13895b",
    loss: "#d4443a",
  },
};

/** The dark theme's colors, Kronant's own, for what does not follow the theme. */
export const kronant = palettes.dark;

function channels(hex: string): [number, number, number] {
  const value = Number.parseInt(hex.slice(1), 16);
  return [(value >> 16) & 255, (value >> 8) & 255, value & 255];
}

/** The color with some transparency, for example withAlpha("#34c38f", 0.5) is "rgba(52, 195, 143, 0.5)". */
export function withAlpha(hex: string, alpha: number): string {
  const [r, g, b] = channels(hex);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

/**
 * The first color mixed with the second, without transparency: weight 1 is the first color and 0 the second. For
 * labels the chart draws without transparency that should look like a faint color over the panel.
 */
export function mix(hex: string, over: string, weight: number): string {
  const a = channels(hex);
  const b = channels(over);
  const [r, g, bl] = a.map((c, i) => Math.round(c * weight + b[i] * (1 - weight)));
  return `rgb(${r}, ${g}, ${bl})`;
}
