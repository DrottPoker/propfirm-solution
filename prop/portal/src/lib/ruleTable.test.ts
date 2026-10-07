import { describe, expect, it } from "vitest";

import { ruleTable } from "./ruleTable";
import { testDetails } from "./testAccounts";

describe("ruleTable", () => {
  it("says a rule once when every phase has the same, and per phase otherwise", () => {
    const table = ruleTable(testDetails(), "trader");
    const row = (label: string) => table.rows.find((r) => r.label === label)!;

    expect(table.columns).toEqual([
      { stage: 0, name: "Phase 1", now: true },
      { stage: 1, name: "Phase 2", now: false },
      { stage: 2, name: "Funded", now: false },
    ]);
    expect(row("Daily loss limit").same).toBe("5% (5,000.00) below the balance when the day starts");
    expect(row("Time limit").same).toBe("None");
    expect(row("Profit target")).toMatchObject({ same: null, values: ["10% (10,000.00)", "5% (5,000.00)", "None"] });
    expect(row("Minimum trading days").values).toEqual(["4", "4", "5 per payout"]);
  });

  it("leaves out a rule in the phases where it does not apply", () => {
    const table = ruleTable(testDetails(), "firm");

    expect(table.rows.find((r) => r.label === "Profit split")).toEqual({ label: "Profit split", values: [null, null, "80% to the trader"], same: null });
  });
});
