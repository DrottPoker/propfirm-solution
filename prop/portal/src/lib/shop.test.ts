import { describe, expect, it } from "vitest";

import type { ShopItem } from "./api/types";
import { payoutLines, shopQuestions, shopRules, shopTables, sizeLabel } from "./shop";
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
    expect(tables[0].rows[1].values).toEqual(["10% (5,000 USD)", "10% (10,000 USD)"]);
    expect(tables[0].rows[3].values).toEqual(["5% (2,500 USD)", "5% (5,000 USD)"]);
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

describe("the shop's cards", () => {
  it("show the rules a buyer compares challenges by", () => {
    const rules = shopRules(item("two-step-100k", "Two-step 100K", 100_000, 499).challenge);

    expect(rules.map((r) => [r.label, r.value])).toEqual([
      ["Profit targets", "10% then 5%"],
      ["Daily loss limit", "5%"],
      ["Max loss limit", "10%"],
      ["Profit split", "80% to you"],
      ["Trading days", "At least 4 a phase"],
      ["Time limit", "None"],
    ]);
  });

  it("answer the questions buyers ask from the firm's rules", () => {
    const questions = shopQuestions([item("two-step-100k", "Two-step 100K", 100_000, 499)], "Aurora Funded");

    expect(questions.map((q) => q.question)).toContain("When can I ask for a payout?");
    expect(questions.find((q) => q.question === "When can I ask for a payout?")?.answer).toBe(
      "Once your account is funded and in profit, with at least 5 trading days since the last payout. Aurora Funded checks it and sends the money, and you keep 80% of the profit.",
    );
  });
});

describe("payoutLines", () => {
  it("says what the firm paid out in whole amounts, never rounded up, and how soon", () => {
    expect(payoutLines({ count: 12, totals: [{ currency: "USD", amount: 12_840.75 }], averageDaysToPay: 1.5 })).toEqual([
      "$12,840 paid out to traders in the last 30 days, in 12 payouts",
      "Paid 1.5 days after the request on average",
    ]);
    expect(payoutLines({ count: 1, totals: [{ currency: "EUR", amount: 900 }], averageDaysToPay: 0.4 })).toEqual([
      "€900 paid out to traders in the last 30 days",
      "Paid within a day of the request on average",
    ]);
    expect(payoutLines({ count: 2, totals: [{ currency: "USD", amount: 500 }, { currency: "EUR", amount: 300 }], averageDaysToPay: null })).toEqual([
      "$500 + €300 paid out to traders in the last 30 days, in 2 payouts",
    ]);
  });
});
