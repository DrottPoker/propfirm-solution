import type { Branding } from "./api/types";

/** Colors a firm may override. They match the tokens in globals.css. */
export const themeColors = [
  "background",
  "panel",
  "border",
  "foreground",
  "muted",
  "accent",
  "buy",
  "sell",
  "profit",
  "loss",
  "warning",
] as const;

const hexColor = /^#[0-9a-fA-F]{6}$/;

/**
 * CSS variables for the firm's colors. The service already validates them; they are checked again
 * here because they end up in a style attribute.
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

/** The host the terminal was opened on, without port. */
export function hostWithoutPort(host: string | null): string | null {
  return host ? host.replace(/:\d+$/, "") : null;
}
