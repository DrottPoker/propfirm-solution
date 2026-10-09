import { describe, expect, it } from "vitest";

import { chartMinimum, defaultLayout, fitWidths, layoutLimits, layoutPresets, parseLayout, presetOf } from "./layout";

describe("parseLayout", () => {
  it("keeps what is valid, within the limits, and takes the default for the rest", () => {
    const layout = parseLayout(JSON.stringify({ watchlist: 9_999, orderPanel: 300, bottom: "tall", watchlistOpen: false }));

    expect(layout).toEqual({ ...defaultLayout, watchlist: layoutLimits.watchlist.max, orderPanel: 300, watchlistOpen: false });
    expect(parseLayout("not json")).toEqual(defaultLayout);
    expect(parseLayout(null)).toEqual(defaultLayout);
  });
});

describe("fitWidths", () => {
  it("gives the side panels their width when the chart has room", () => {
    expect(fitWidths(defaultLayout, 1440)).toEqual({ watchlist: defaultLayout.watchlist, orderPanel: defaultLayout.orderPanel });
  });

  // A small laptop window: the watchlist gives up width first, so the chart keeps its least.
  it("narrows the watchlist first, then the order panel, down to their least", () => {
    const wide = { ...defaultLayout, watchlist: 400, orderPanel: 380 };

    expect(fitWidths(wide, 1100)).toEqual({ watchlist: 1100 - 380 - chartMinimum.width, orderPanel: 380 });
    expect(fitWidths(wide, 800)).toEqual({ watchlist: layoutLimits.watchlist.min, orderPanel: layoutLimits.orderPanel.min });
  });

  it("gives the hidden watchlist no width", () => {
    expect(fitWidths({ ...defaultLayout, watchlistOpen: false }, 1024).watchlist).toBe(0);
  });
});

describe("presetOf", () => {
  it("names a ready-made layout, and none for the trader's own", () => {
    expect(presetOf(layoutPresets[1].layout)).toBe("chart");
    expect(presetOf({ ...defaultLayout, bottom: 301 })).toBeNull();
  });
});
