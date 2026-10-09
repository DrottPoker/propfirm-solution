import { describe, expect, it } from "vitest";

import type { InstrumentInfo } from "./api/types";
import { instrumentsOf, matchesSearch, moveSymbol, nextSort, parseLists } from "./watchlists";

const instrument = (symbol: string, name: string, category: InstrumentInfo["category"]): InstrumentInfo =>
  ({ symbol, name, category }) as InstrumentInfo;

const all = [instrument("EURUSD", "Euro / US Dollar", "Forex"), instrument("XAUUSD", "Gold", "Metals"), instrument("DE40", "Germany 40", "Indices")];

describe("the watchlist's lists", () => {
  it("finds instruments by symbol or by name", () => {
    expect(all.filter((i) => matchesSearch(i, "gold")).map((i) => i.symbol)).toEqual(["XAUUSD"]);
    expect(all.filter((i) => matchesSearch(i, "euro")).map((i) => i.symbol)).toEqual(["EURUSD"]);
    expect(all.filter((i) => matchesSearch(i, "eur/usd")).map((i) => i.symbol)).toEqual(["EURUSD"]);
    expect(all.filter((i) => matchesSearch(i, " ")).length).toBe(3);
  });

  it("shows a category, the favorites or an own list, the last two in their own order", () => {
    const lists = [{ id: "a", name: "Mine", symbols: ["DE40", "EURUSD", "GONE"] }];
    expect(instrumentsOf({ kind: "category", category: "Metals" }, all, [], lists).map((i) => i.symbol)).toEqual(["XAUUSD"]);
    expect(instrumentsOf({ kind: "favorites" }, all, ["XAUUSD", "EURUSD"], lists).map((i) => i.symbol)).toEqual(["XAUUSD", "EURUSD"]);
    expect(instrumentsOf({ kind: "own", id: "a" }, all, [], lists).map((i) => i.symbol)).toEqual(["DE40", "EURUSD"]);
  });

  it("moves a symbol before another, or to the end", () => {
    expect(moveSymbol(["A", "B", "C"], "C", "A")).toEqual(["C", "A", "B"]);
    expect(moveSymbol(["A", "B", "C"], "A", null)).toEqual(["B", "C", "A"]);
  });

  it("reads stored lists and skips what is not one", () => {
    expect(parseLists('[{"id":"a","name":"Mine","symbols":["EURUSD",3]},{"name":"x"}]')).toEqual([{ id: "a", name: "Mine", symbols: ["EURUSD"] }]);
    expect(parseLists("nonsense")).toEqual([]);
  });

  it("sorts numbers high first, then low, then back to the list's order", () => {
    const first = nextSort(null, "change");
    expect(first).toEqual({ column: "change", descending: true });
    const second = nextSort(first, "change");
    expect(second).toEqual({ column: "change", descending: false });
    expect(nextSort(second, "change")).toBeNull();
    expect(nextSort(null, "symbol")).toEqual({ column: "symbol", descending: false });
  });
});
