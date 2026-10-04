import { describe, expect, it } from "vitest";

import { conditionRows, conditionsRequest } from "./tradingConditions";

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
