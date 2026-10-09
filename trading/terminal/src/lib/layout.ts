import { create } from "zustand";

import { onSettingsLoaded, readSetting, writeSetting } from "./syncedSettings";

// How the terminal's parts sit on a computer screen (ADR 0058): the watchlist on the left, the order panel on the
// right, and the chart over the positions in between. The trader drags the edges between them, hides the watchlist or
// the positions, or picks a ready-made layout. Widths and heights are in pixels, so the side panels keep their width
// when the window changes, and the chart takes what is left. Kept in the browser and on the login (ADR 0052).

export interface Layout {
  /** The watchlist's width. */
  watchlist: number;
  watchlistOpen: boolean;
  /** The order panel's width. */
  orderPanel: number;
  /** The most the positions panel grows to. It is only as tall as what it shows, up to this. */
  bottom: number;
  bottomOpen: boolean;
}

export type LayoutPart = "watchlist" | "orderPanel" | "bottom";

/** The smallest and largest each part can be dragged to. */
export const layoutLimits: Record<LayoutPart, { min: number; max: number }> = {
  watchlist: { min: 220, max: 440 },
  orderPanel: { min: 264, max: 400 },
  bottom: { min: 120, max: 640 },
};

/** The chart keeps at least this much, so dragging a side panel never squeezes it away. */
export const chartMinimum = { width: 360, height: 220 };

export const defaultLayout: Layout = { watchlist: 264, watchlistOpen: true, orderPanel: 288, bottom: 240, bottomOpen: true };

export type LayoutPreset = "standard" | "chart" | "trading";

/** Ready-made layouts: everything, the chart as large as it gets, or a wide order panel for frequent trading. */
export const layoutPresets: readonly { id: LayoutPreset; name: string; description: string; layout: Layout }[] = [
  { id: "standard", name: "Standard", description: "Watchlist, chart, positions and the order panel.", layout: defaultLayout },
  {
    id: "chart",
    name: "Chart in focus",
    description: "The watchlist hidden and the positions folded away, so the chart gets the room.",
    layout: { ...defaultLayout, watchlistOpen: false, bottomOpen: false },
  },
  {
    id: "trading",
    name: "Active trading",
    description: "A wider order panel, room for many positions, and buy and sell on the chart.",
    layout: { ...defaultLayout, watchlist: 236, orderPanel: 320, bottom: 320 },
  },
];

const storageKey = "trading.layout";

const clamp = (value: number, part: LayoutPart) => Math.round(Math.min(layoutLimits[part].max, Math.max(layoutLimits[part].min, value)));

/** Reads a stored layout, keeping what is valid and taking the default for the rest. */
export function parseLayout(raw: string | null): Layout {
  if (!raw) {
    return defaultLayout;
  }

  try {
    const value: unknown = JSON.parse(raw);
    if (typeof value !== "object" || value === null) {
      return defaultLayout;
    }

    const stored = value as Record<string, unknown>;
    const size = (part: LayoutPart) => (typeof stored[part] === "number" && Number.isFinite(stored[part]) ? clamp(stored[part], part) : defaultLayout[part]);
    const open = (key: "watchlistOpen" | "bottomOpen") => (typeof stored[key] === "boolean" ? stored[key] : defaultLayout[key]);
    return {
      watchlist: size("watchlist"),
      watchlistOpen: open("watchlistOpen"),
      orderPanel: size("orderPanel"),
      bottom: size("bottom"),
      bottomOpen: open("bottomOpen"),
    };
  } catch {
    return defaultLayout;
  }
}

/**
 * The widths the side panels get in a window of the width: as chosen, unless the chart would be narrower than its
 * least, when the watchlist gives up width first and then the order panel, down to their least.
 */
export function fitWidths(layout: Layout, windowWidth: number): { watchlist: number; orderPanel: number } {
  let watchlist = layout.watchlistOpen ? layout.watchlist : 0;
  let orderPanel = layout.orderPanel;
  let over = watchlist + orderPanel + chartMinimum.width - windowWidth;
  if (over > 0 && layout.watchlistOpen) {
    const give = Math.min(over, watchlist - layoutLimits.watchlist.min);
    watchlist -= give;
    over -= give;
  }

  if (over > 0) {
    orderPanel -= Math.min(over, orderPanel - layoutLimits.orderPanel.min);
  }

  return { watchlist, orderPanel };
}

/** Which ready-made layout this is, or null for the trader's own. */
export function presetOf(layout: Layout): LayoutPreset | null {
  return layoutPresets.find((p) => JSON.stringify(p.layout) === JSON.stringify(layout))?.id ?? null;
}

interface LayoutState extends Layout {
  /** While an edge is dragged: shown at once, saved when the drag ends. */
  resize: (part: LayoutPart, size: number) => void;
  /** Saves the layout as it is now, for example after a drag. */
  save: () => void;
  setOpen: (part: "watchlist" | "bottom", open: boolean) => void;
  apply: (layout: Layout) => void;
}

function current(state: LayoutState): Layout {
  return { watchlist: state.watchlist, watchlistOpen: state.watchlistOpen, orderPanel: state.orderPanel, bottom: state.bottom, bottomOpen: state.bottomOpen };
}

const write = (layout: Layout) => writeSetting(storageKey, JSON.stringify(layout));

export const useLayout = create<LayoutState>()((set, get) => ({
  ...parseLayout(readSetting(storageKey)),
  resize: (part, size) => set({ [part]: clamp(size, part) } as Pick<Layout, LayoutPart>),
  save: () => write(current(get())),
  setOpen: (part, open) => {
    set(part === "watchlist" ? { watchlistOpen: open } : { bottomOpen: open });
    write(current(get()));
  },
  apply: (layout) => {
    set(layout);
    write(layout);
  },
}));

onSettingsLoaded(() => useLayout.setState(parseLayout(readSetting(storageKey))));
