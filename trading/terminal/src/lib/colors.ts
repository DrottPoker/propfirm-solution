// Colors for the chart, which draws on a canvas and needs them as values rather than CSS variables.

/** Kronant's colors from @kronant/design (shared/web/design/tokens.css), the same as the terminal's CSS uses. */
export const kronant = {
  panel: "#15171b",
  raised: "#1c1f25",
  border: "#272a31",
  foreground: "#ece7df",
  muted: "#8f8a81",
  brass: "#c9a35b",
  profit: "#34c38f",
  loss: "#ef5a50",
} as const;

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
