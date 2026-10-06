import { describe, expect, it } from "vitest";

import { applyToGroup, conditionRows, conditionsRequest } from "./tradingConditions";

const eurusd = { symbol: "EURUSD", baseCurrency: "EUR", quoteCurrency: "USD", contractSize: 100_000, enabled: true, leverage: 100, spreadMarkupPoints: 2, commissionPerLotPerSide: 3.5 };
const xagusd = { symbol: "XAGUSD", baseCurrency: "XAG", quoteCurrency: "USD", contractSize: 5_000, enabled: false, leverage: null, spreadMarkupPoints: null, commissionPerLotPerSide: null };

describe("trading conditions", () => {
  it("start an instrument that is not traded from conditions to change", () => {
    expect(conditionRows([eurusd, xagusd])).toEqual([
      { symbol: "EURUSD", enabled: true, leverage: "100", spreadMarkupPoints: "2", commissionPerLotPerSide: "3.50" },
      { symbol: "XAGUSD", enabled: false, leverage: "30", spreadMarkupPoints: "0", commissionPerLotPerSide: "0.00" },
    ]);
  });

  it("send only the instruments turned on", () => {
    const rows = conditionRows([eurusd, xagusd]);

    expect(conditionsRequest(rows)).toEqual({ symbols: [{ symbol: "EURUSD", leverage: 100, spreadMarkupPoints: 2, commissionPerLotPerSide: 3.5 }] });
    expect(conditionsRequest([{ ...rows[0], commissionPerLotPerSide: "2,25" }])).toEqual({
      symbols: [{ symbol: "EURUSD", leverage: 100, spreadMarkupPoints: 2, commissionPerLotPerSide: 2.25 }],
    });
  });

  it("say what is wrong", () => {
    const [row] = conditionRows([eurusd]);

    expect(conditionsRequest([{ ...row, enabled: false }])).toEqual({ problem: "Choose at least one instrument for your traders." });
    expect(conditionsRequest([{ ...row, leverage: "1.5" }])).toMatchObject({ problem: expect.stringContaining("leverage") });
    expect(conditionsRequest([{ ...row, spreadMarkupPoints: "-1" }])).toMatchObject({ problem: expect.stringContaining("spread markup") });
    expect(conditionsRequest([{ ...row, commissionPerLotPerSide: "1.234" }])).toMatchObject({ problem: expect.stringContaining("two decimals") });
  });
});

describe("conditions for many instruments at once", () => {
  const row = (symbol: string, enabled = true) => ({ symbol, enabled, leverage: "100", spreadMarkupPoints: "2", commissionPerLotPerSide: "3.50" });

  it("go on every traded instrument in the group, and leave the rest and what was left empty", () => {
    const rows = applyToGroup([row("EURUSD"), row("GBPUSD", false), row("XAUUSD")], "Forex", { leverage: " 50 ", spreadMarkupPoints: "", commissionPerLotPerSide: "2" });

    expect(rows.map((r) => [r.symbol, r.leverage, r.spreadMarkupPoints, r.commissionPerLotPerSide])).toEqual([
      ["EURUSD", "50", "2", "2"],
      ["GBPUSD", "100", "2", "3.50"],
      ["XAUUSD", "100", "2", "3.50"],
    ]);
  });

  it("go on every group with All", () => {
    expect(applyToGroup([row("EURUSD"), row("XAUUSD")], "All", { leverage: "20", spreadMarkupPoints: "", commissionPerLotPerSide: "" }).map((r) => r.leverage)).toEqual(["20", "20"]);
  });
});
