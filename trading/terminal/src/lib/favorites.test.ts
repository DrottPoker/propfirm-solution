import { describe, expect, it } from "vitest";

import { parseFavorites, toggleFavorite } from "./favorites";

describe("parseFavorites", () => {
  it.each([
    [null, []],
    ["", []],
    ['["EURUSD","XAUUSD"]', ["EURUSD", "XAUUSD"]],
    ['["EURUSD",1,null]', ["EURUSD"]],
    ['{"EURUSD":true}', []],
    ["not json", []],
  ])("reads %j as %j", (raw, expected) => {
    expect(parseFavorites(raw)).toEqual(expected);
  });
});

describe("toggleFavorite", () => {
  it("adds a symbol that is not a favorite and removes one that is", () => {
    expect(toggleFavorite(["EURUSD"], "XAUUSD")).toEqual(["EURUSD", "XAUUSD"]);
    expect(toggleFavorite(["EURUSD", "XAUUSD"], "EURUSD")).toEqual(["XAUUSD"]);
  });
});
