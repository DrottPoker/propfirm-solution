import { create } from "zustand";

import type { InstrumentInfo } from "./api/types";
import { loadFavorites, parseFavorites, saveFavorites } from "./favorites";
import type { Category } from "./instruments";
import { onSettingsLoaded, readSetting, writeSetting } from "./syncedSettings";

// The watchlist's lists (ADR 0058): everything, a category, the starred favorites, and lists of the trader's own, such as
// "Mine" or "Index", each in the trader's own order. Kept on the trader's login like the favorites (ADR 0052).

/** A list of the trader's own, in their order. */
export interface OwnList {
  id: string;
  name: string;
  symbols: string[];
}

/** What the watchlist shows: every instrument, a category, the favorites, or one of the trader's own lists. */
export type ListChoice = { kind: "all" } | { kind: "category"; category: Category } | { kind: "favorites" } | { kind: "own"; id: string };

const listsKey = "trading.watchlists";

export const maxListName = 30;
export const maxLists = 20;

/** Reads stored lists, keeping only well-formed ones with a name. */
export function parseLists(raw: string | null): OwnList[] {
  if (!raw) {
    return [];
  }

  try {
    const value: unknown = JSON.parse(raw);
    if (!Array.isArray(value)) {
      return [];
    }

    return value
      .filter((l): l is OwnList => typeof l?.id === "string" && typeof l?.name === "string" && Array.isArray(l?.symbols))
      .map((l) => ({ id: l.id, name: l.name.slice(0, maxListName), symbols: parseFavorites(JSON.stringify(l.symbols)) }))
      .slice(0, maxLists);
  } catch {
    return [];
  }
}

/** A symbol moved to another place in a list, for dragging rows into the trader's own order. */
export function moveSymbol(symbols: readonly string[], symbol: string, before: string | null): string[] {
  const rest = symbols.filter((s) => s !== symbol);
  const at = before === null ? rest.length : rest.indexOf(before);
  return at === -1 ? [...rest, symbol] : [...rest.slice(0, at), symbol, ...rest.slice(at)];
}

/** The instruments of the list, in the list's own order for the favorites and own lists. */
export function instrumentsOf(choice: ListChoice, instruments: readonly InstrumentInfo[], favorites: readonly string[], lists: readonly OwnList[]): InstrumentInfo[] {
  const ordered = (symbols: readonly string[]) =>
    symbols.map((s) => instruments.find((i) => i.symbol === s)).filter((i): i is InstrumentInfo => i !== undefined);
  switch (choice.kind) {
    case "all":
      return [...instruments];
    case "category":
      return instruments.filter((i) => i.category === choice.category);
    case "favorites":
      return ordered(favorites);
    case "own":
      return ordered(lists.find((l) => l.id === choice.id)?.symbols ?? []);
  }
}

/** Whether the instrument matches the search, by its symbol or its name: "gold" finds XAUUSD, "euro" EURUSD. */
export function matchesSearch(instrument: Pick<InstrumentInfo, "symbol" | "name">, search: string): boolean {
  const query = search.trim().toLowerCase();
  if (query === "") {
    return true;
  }

  return instrument.symbol.toLowerCase().includes(query.replace(/[\s/]/g, "")) || instrument.name.toLowerCase().includes(query);
}

/** How the rows are sorted: in the list's order, or by a column. */
export type WatchlistSort = { column: "symbol" | "change" | "spread"; descending: boolean } | null;

/** The next sort when a column's heading is pressed: down first for numbers, then up, then the list's own order. */
export function nextSort(current: WatchlistSort, column: NonNullable<WatchlistSort>["column"]): WatchlistSort {
  const firstDescending = column !== "symbol";
  if (current?.column !== column) {
    return { column, descending: firstDescending };
  }

  return current.descending === firstDescending ? { column, descending: !firstDescending } : null;
}

interface WatchlistsState {
  favorites: string[];
  lists: OwnList[];
  setFavorites: (symbols: string[]) => void;
  createList: (name: string) => string;
  renameList: (id: string, name: string) => void;
  removeList: (id: string) => void;
  setListSymbols: (id: string, symbols: string[]) => void;
}

function saveLists(lists: readonly OwnList[]) {
  writeSetting(listsKey, lists.length === 0 ? null : JSON.stringify(lists));
}

/** The favorites and the trader's own lists, kept on their login. */
export const useWatchlists = create<WatchlistsState>()((set, get) => ({
  favorites: loadFavorites(),
  lists: parseLists(readSetting(listsKey)),
  setFavorites: (symbols) => {
    saveFavorites(symbols);
    set({ favorites: symbols });
  },
  createList: (name) => {
    const id = `list-${Date.now().toString(36)}`;
    const lists = [...get().lists, { id, name: name.trim().slice(0, maxListName), symbols: [] }].slice(0, maxLists);
    saveLists(lists);
    set({ lists });
    return id;
  },
  renameList: (id, name) => {
    const lists = get().lists.map((l) => (l.id === id ? { ...l, name: name.trim().slice(0, maxListName) } : l));
    saveLists(lists);
    set({ lists });
  },
  removeList: (id) => {
    const lists = get().lists.filter((l) => l.id !== id);
    saveLists(lists);
    set({ lists });
  },
  setListSymbols: (id, symbols) => {
    const lists = get().lists.map((l) => (l.id === id ? { ...l, symbols } : l));
    saveLists(lists);
    set({ lists });
  },
}));

onSettingsLoaded(() => useWatchlists.setState({ favorites: loadFavorites(), lists: parseLists(readSetting(listsKey)) }));
