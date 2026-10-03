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

const hexColor = /^#[0-9a-fA-F]{6}$/;

/**
 * CSS variables for the firm's colors. Prop.Api already validates them; they are checked again here
 * because they end up in a style attribute.
 */
export function themeStyle(branding: Branding | null): Record<string, string> {
  const style: Record<string, string> = {};
  for (const name of themeColors) {
    const value = branding?.colors[name];
    if (value !== undefined && hexColor.test(value)) {
      style[`--${name}`] = value;
    }
  }

  return style;
}
