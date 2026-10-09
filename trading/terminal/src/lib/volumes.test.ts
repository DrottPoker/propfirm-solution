import { describe, expect, it } from "vitest";

import { parseVolumes } from "./volumes";

describe("parseVolumes", () => {
  it.each([
    [null, {}],
    ["", {}],
    ['{"XAUUSD":"0.10","EURUSD":"2.00"}', { XAUUSD: "0.10", EURUSD: "2.00" }],
    ['{"XAUUSD":0.1,"EURUSD":"2.00"}', { EURUSD: "2.00" }],
    ['["0.10"]', {}],
    ["null", {}],
    ["not json", {}],
  ])("reads %j as %j", (raw, expected) => {
    expect(parseVolumes(raw)).toEqual(expected);
  });
});
