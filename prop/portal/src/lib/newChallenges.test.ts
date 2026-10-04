import { describe, expect, it } from "vitest";

import type { ChallengeTemplate } from "./api/types";
import { challengesFromTemplate, sizeName } from "./newChallenges";
import { testChallenge } from "./testAccounts";

describe("new challenges", () => {
  it("are named after their size as traders say it", () => {
    expect([5_000, 12_500, 100_000, 1_000_000, 1_500_000, 750].map(sizeName)).toEqual(["5K", "12.5K", "100K", "1M", "1.5M", "750"]);
  });

  it("are made from a template, one for each size, in the firm's currency", () => {
    const template: ChallengeTemplate = { id: "one-step", name: "One-step", description: "", definition: testChallenge };

    const made = challengesFromTemplate(template, [10_000, 50_000], "EUR");

    expect(made.map((c) => [c.id, c.name, c.initialBalance, c.currency])).toEqual([
      ["one-step-10k", "One-step 10K", 10_000, "EUR"],
      ["one-step-50k", "One-step 50K", 50_000, "EUR"],
    ]);
    expect(made[0].evaluation).toEqual(testChallenge.evaluation);
  });
});
