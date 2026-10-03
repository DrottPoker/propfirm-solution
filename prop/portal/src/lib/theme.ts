import type { Branding } from "./api/types";

/** Colors a firm may override. They match the tokens in globals.css and the ones Prop.Api accepts. */
export const themeColors = [
  "background",
  "panel",
  "border",
  "foreground",
  "muted",
  "accent",
  "profit",
  "loss",
  "warning",
] as const;

export type ThemeColor = (typeof themeColors)[number];

/** The portal's own look, the same as in globals.css. A firm's colors replace these. */
export const defaultColors: Record<ThemeColor, string> = {
  background: "#0b0e14",
  panel: "#11151c",
  border: "#1f2530",
  foreground: "#d6d9e0",
  muted: "#7d8590",
  accent: "#3b82f6",
  profit: "#22c55e",
  loss: "#ef4444",
  warning: "#f59e0b",
};

const hexColor = /^#[0-9a-fA-F]{6}$/;

/**
 * CSS variables for the firm's colors. Prop.Api already validates them; they are checked again here
 * because they end up in a style attribute.
 */
export function themeStyle(branding: Pick<Branding, "colors"> | null): Record<string, string> {
  const style: Record<string, string> = {};
  for (const name of themeColors) {
    const value = branding?.colors[name];
    if (value !== undefined && hexColor.test(value)) {
      style[`--${name}`] = value;
    }
  }

  return style;
}
