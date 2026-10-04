import { describe, expect, it } from "vitest";

import type { ShopItem } from "./api/types";
import { shopTables, sizeLabel } from "./shop";
import { testChallenge } from "./testAccounts";

function item(id: string, name: string, initialBalance: number, price: number, changes: Partial<ShopItem["challenge"]> = {}): ShopItem {
  return { challenge: { ...testChallenge, id, name, initialBalance, ...changes }, price, currency: "USD" };
}

describe("the shop's price tables", () => {
  it("put the sizes of a challenge with the same rules side by side, the smallest first", () => {
    const tables = shopTables([item("two-step-100k", "Two-step 100K", 100_000, 499), item("two-step-50k", "Two-step 50K", 50_000, 299)]);

    expect(tables).toHaveLength(1);
    expect(tables[0].title).toBe("Two-step");
    expect(tables[0].items.map((i) => sizeLabel(i.challenge.initialBalance))).toEqual(["50K", "100K"]);
    expect(tables[0].rows.map((r) => r.label)).toEqual([
      "Price",
      "Phase 1 target",
      "Phase 2 target",
      "Daily loss limit",
      "Max loss limit",
      "Minimum trading days",
      "Time limit",
      "Profit split",
      "Inactivity",
    ]);
    expect(tables[0].rows[0].values).toEqual(["299.00 USD", "499.00 USD"]);
    expect(tables[0].rows[1].values).toEqual(["10% · 5,000.00", "10% · 10,000.00"]);
    expect(tables[0].rows[3].values).toEqual(["5% · 2,500.00", "5% · 5,000.00"]);
  });

  it("show a consistency rule for payouts", () => {
    const tables = shopTables([item("two-step-100k", "Two-step 100K", 100_000, 499, { funded: { ...testChallenge.funded, consistencyPercent: 40 } })]);

    expect(tables[0].rows.find((r) => r.label === "Consistency")?.values).toEqual(["Best day at most 40% of a payout's profit"]);
  });

  it("give challenges with other rules a table of their own, the cheapest table first", () => {
    const instant = item("instant-50k", "Instant 50K", 50_000, 199, { evaluation: [], inactivityDays: null });
    const tables = shopTables([item("two-step-50k", "Two-step 50K", 50_000, 299), instant]);

    expect(tables.map((t) => t.title)).toEqual(["Instant 50K", "Two-step 50K"]);
    expect(tables[0].rows.find((r) => r.label === "Evaluation")?.values).toEqual(["None, funded at once"]);
  });

  it("names sizes in thousands", () => {
    expect([sizeLabel(5_000), sizeLabel(2_500), sizeLabel(200_000), sizeLabel(750)]).toEqual(["5K", "2.5K", "200K", "750.00"]);
  });
});
