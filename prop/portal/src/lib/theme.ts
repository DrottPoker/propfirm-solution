import type { Branding } from "./api/types";

/** Colors a firm may override. They match the tokens in globals.css and the ones Prop.Api accepts. */
export const themeColors = [
  "background",
  "panel",
  "border",
  "foreground",
  "muted",
  "accent",
  "accent-foreground",
  "profit",
  "loss",
  "warning",
] as const;

export type ThemeColor = (typeof themeColors)[number];

export type ThemeColors = Partial<Record<ThemeColor, string>>;

/** The portal's own look, the same as in globals.css. A firm's colors replace these. */
export const defaultColors: Record<ThemeColor, string> = {
  background: "#0b0e14",
  panel: "#11151c",
  border: "#1f2530",
  foreground: "#d6d9e0",
  muted: "#7d8590",
  accent: "#3b82f6",
  "accent-foreground": "#ffffff",
  profit: "#22c55e",
  loss: "#ef4444",
  warning: "#f59e0b",
};

/** Our own admin view's look: the portal's, with a brand color of its own so it is never taken for a firm's. */
export const opsColors: ThemeColors = { accent: "#2dd4bf", "accent-foreground": "#0b0e14" };

/** The colors a theme sets: everything but the brand color and the text on it, which the firm chooses on their own. */
export const surfaceColors = ["background", "panel", "border", "foreground", "muted", "profit", "loss", "warning"] as const;

export type SurfaceColor = (typeof surfaceColors)[number];

/** Ready-made themes a firm starts from. Dark is the portal's own look, so it overrides nothing. */
export const themePresets: { id: "dark" | "light" | "navy"; name: string; colors: Partial<Record<SurfaceColor, string>> }[] = [
  { id: "dark", name: "Dark", colors: {} },
  {
    id: "light",
    name: "Light",
    colors: {
      background: "#f5f6f8",
      panel: "#ffffff",
      border: "#dde1e7",
      foreground: "#1a1f29",
      muted: "#5d6673",
      profit: "#15803d",
      loss: "#c53030",
      warning: "#b45309",
    },
  },
  {
    id: "navy",
    name: "Navy",
    colors: {
      background: "#0a1224",
      panel: "#0f1a33",
      border: "#22304f",
      foreground: "#dbe3f2",
      muted: "#8d9ab3",
      profit: "#34d399",
      loss: "#f87171",
      warning: "#fbbf24",
    },
  },
];

/** The firm's colors with a theme's in place of its surface colors. The brand color and the text on it stay. */
export function withPreset(colors: ThemeColors, preset: (typeof themePresets)[number]): ThemeColors {
  const next: ThemeColors = {};
  for (const name of themeColors) {
    const value = (surfaceColors as readonly string[]).includes(name) ? preset.colors[name as SurfaceColor] : colors[name];
    if (value !== undefined) {
      next[name] = value;
    }
  }

  return next;
}

/** The theme whose surface colors the firm has, or null when it has changed some of them on its own. */
export function presetOf(colors: ThemeColors): (typeof themePresets)[number]["id"] | null {
  const preset = themePresets.find((p) => surfaceColors.every((name) => colors[name] === p.colors[name]));
  return preset?.id ?? null;
}

const hexColor = /^#[0-9a-fA-F]{6}$/;

/** Whether the text is a color the portal and Prop.Api accept, as #rrggbb. */
export function isHexColor(value: string): boolean {
  return hexColor.test(value);
}

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

/** How far apart two colors are in lightness, as WCAG's contrast ratio from 1 to 21. Text needs at least 4.5. */
export function contrastRatio(first: string, second: string): number {
  const [lighter, darker] = [luminance(first), luminance(second)].sort((a, b) => b - a);
  return (lighter + 0.05) / (darker + 0.05);
}

/** The contrast text needs on its background to be easy to read. */
export const readableContrast = 4.5;

function luminance(hex: string): number {
  const [r, g, b] = [1, 3, 5].map((i) => {
    const channel = parseInt(hex.slice(i, i + 2), 16) / 255;
    return channel <= 0.03928 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
