import { useEffect, useSyncExternalStore } from "react";

import { palettes, type Palette } from "./colors";
import { useSettings, type ThemeChoice } from "./settings";

/** The theme a page is drawn in. */
export type ThemeName = "dark" | "light";

/** The theme the choice gives, with "as the computer" following its light or dark setting. */
export function resolveTheme(choice: ThemeChoice, computerIsLight: boolean): ThemeName {
  return choice === "system" ? (computerIsLight ? "light" : "dark") : choice;
}

const lightQuery = "(prefers-color-scheme: light)";

function subscribe(onChange: () => void): () => void {
  const query = window.matchMedia(lightQuery);
  query.addEventListener("change", onChange);
  return () => query.removeEventListener("change", onChange);
}

/** The theme in use, following the computer while the trader chose that. */
export function useTheme(): ThemeName {
  const choice = useSettings((s) => s.theme);
  const computerIsLight = useSyncExternalStore(
    subscribe,
    () => window.matchMedia(lightQuery).matches,
    () => false,
  );
  return resolveTheme(choice, computerIsLight);
}

/** The colors of the theme in use, for what draws on a canvas, such as the chart. */
export function usePalette(): Palette {
  return palettes[useTheme()];
}

/** Puts the theme on the page, where the CSS reads it. */
export function useApplyTheme() {
  const theme = useTheme();
  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);
}
