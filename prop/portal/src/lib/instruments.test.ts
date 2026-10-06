import { describe, expect, it } from "vitest";

import { instrumentGroup, instrumentMarks, instrumentName } from "./instruments";

describe("instruments as people say them", () => {
  it("names metals and currency pairs, and leaves other symbols as they are", () => {
    expect(instrumentName("XAUUSD")).toBe("Gold");
    expect(instrumentName("EURUSD")).toBe("Euro / US dollar");
    expect(instrumentName("US500")).toBe("US500");
    expect(instrumentName("ABCDEF")).toBe("ABCDEF");
  });

  it("puts them in their group", () => {
    expect(["EURUSD", "XAGUSD", "US500", "BTCUSD"].map(instrumentGroup)).toEqual(["Forex", "Metals", "Other", "Other"]);
  });

  it("has a short mark for each half, when both are known", () => {
    expect(instrumentMarks("EURUSD")).toEqual(["€", "$"]);
    expect(instrumentMarks("XAUUSD")).toEqual(["Au", "$"]);
    expect(instrumentMarks("BTCUSD")).toBeNull();
  });
});
