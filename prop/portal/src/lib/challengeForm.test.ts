import { describe, expect, it } from "vitest";

import type { ChallengeDefinition } from "./api/types";
import { definitionOf, formOf, nextStage } from "./challengeForm";

const twoStep: ChallengeDefinition = {
  id: "two-step-100k",
  name: "Two-step 100000 USD",
  currency: "USD",
  initialBalance: 100_000,
  tradingDay: { timeZone: "Europe/Stockholm", start: "00:00:00" },
  evaluation: [
    {
      name: "Phase 1",
      profitTargetPercent: 10,
      minTradingDays: 4,
      dailyLoss: { percent: 5, reference: "Balance" },
      maxLoss: { percent: 10, kind: "Fixed" },
      profitSplitPercent: null,
      maxDays: null,
    },
    {
      name: "Phase 2",
      profitTargetPercent: 5,
      minTradingDays: 4,
      dailyLoss: { percent: 5, reference: "Balance" },
      maxLoss: { percent: 10, kind: "Fixed" },
      profitSplitPercent: null,
      maxDays: null,
    },
  ],
  funded: {
    name: "Funded",
    profitTargetPercent: null,
    minTradingDays: 5,
    dailyLoss: { percent: 5, reference: "Balance" },
    maxLoss: { percent: 10, kind: "Fixed" },
    profitSplitPercent: 80,
    maxDays: null,
  },
  inactivityDays: 30,
};

describe("challenge form", () => {
  it("gives back the definition it was made from", () => {
    expect(definitionOf(formOf(twoStep))).toEqual({ definition: twoStep });
  });

  it("reads what is typed, with a decimal comma too", () => {
    const form = formOf(twoStep);
    form.initialBalance = " 50000 ";
    form.dayStart = "17:00";
    form.evaluation[0] = { ...form.evaluation[0], profitTargetPercent: "8,5", maxLossKind: "Trailing" };
    form.funded = { ...form.funded, profitSplitPercent: "90" };

    const result = definitionOf(form);

    expect(result).toMatchObject({
      definition: {
        initialBalance: 50_000,
        tradingDay: { start: "17:00:00" },
        evaluation: [{ profitTargetPercent: 8.5, maxLoss: { kind: "Trailing" } }, { profitTargetPercent: 5 }],
        funded: { profitSplitPercent: 90, profitTargetPercent: null },
      },
    });
  });

  it("reads a time limit per stage and no limit on days without a trade", () => {
    const form = formOf(twoStep);
    form.evaluation[0] = { ...form.evaluation[0], maxDays: " 30 " };
    form.inactivityDays = "";

    expect(definitionOf(form)).toMatchObject({
      definition: { evaluation: [{ maxDays: 30 }, { maxDays: null }], funded: { maxDays: null }, inactivityDays: null },
    });
  });

  it.each([
    [(f: ReturnType<typeof formOf>) => (f.initialBalance = "a lot"), "The account size must be a number."],
    [(f: ReturnType<typeof formOf>) => (f.inactivityDays = "a month"), "The days without a new trade must be a whole number, or empty for no limit."],
    [(f: ReturnType<typeof formOf>) => (f.evaluation[0].maxDays = "2.5"), "Phase 1: the time limit must be a whole number of days, or empty for none."],
    [(f: ReturnType<typeof formOf>) => (f.evaluation[1].profitTargetPercent = ""), "Phase 2: the profit target must be a number."],
    [(f: ReturnType<typeof formOf>) => (f.evaluation[0].minTradingDays = "2.5"), "Phase 1: the trading days must be a whole number."],
    [(f: ReturnType<typeof formOf>) => (f.funded.profitSplitPercent = "most"), "Funded: the profit split must be a number."],
    [(f: ReturnType<typeof formOf>) => (f.funded.dailyLossPercent = ""), "Funded: the loss limits must be numbers."],
  ])("names the first field that is not a number", (change, problem) => {
    const form = formOf(twoStep);
    change(form);

    expect(definitionOf(form)).toEqual({ problem });
  });

  it("adds a stage like the last one", () => {
    expect(nextStage(formOf(twoStep))).toMatchObject({ name: "Phase 3", profitTargetPercent: "5", minTradingDays: "4" });
  });
});
