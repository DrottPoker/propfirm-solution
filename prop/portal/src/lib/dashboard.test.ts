import { describe, expect, it } from "vitest";

import type { AccountDetails } from "./api/types";
import {
  attentionItems,
  currentTradingDay,
  deadlineOf,
  endingText,
  floorState,
  initials,
  objectivesOf,
  resultTone,
  statusText,
  stepState,
} from "./dashboard";
import { testAccount, testChallenge, testDetails } from "./testAccounts";

describe("resultTone", () => {
  it("is green above zero, red below and neutral otherwise", () => {
    expect([resultTone(10), resultTone(-0.01), resultTone(0), resultTone(null)]).toEqual(["profit", "loss", "neutral", "neutral"]);
  });
});

describe("floorState", () => {
  it("warns under a quarter of the limit's distance and alarms under a tenth, as in the terminal", () => {
    expect(floorState({ headroom: 1_250, distance: 5_000 })).toBe("ok");
    expect(floorState({ headroom: 1_249, distance: 5_000 })).toBe("warning");
    expect(floorState({ headroom: 499, distance: 5_000 })).toBe("danger");
  });

  it("does not judge a floor without a distance", () => {
    expect(floorState({ headroom: 1, distance: null })).toBe("ok");
  });
});

describe("initials", () => {
  it("takes two letters from the email address", () => {
    expect([initials("anna@test.com"), initials("anna.berg@test.com"), initials("bo_ek@test.com"), initials("x@test.com")]).toEqual(["AN", "AB", "BE", "X"]);
  });
});

describe("currentTradingDay", () => {
  it("is the local date where the trading day started", () => {
    expect(currentTradingDay(testDetails())).toBe("2026-10-06");
  });

  it("follows the challenge's own time zone and start", () => {
    const newYork = { ...testChallenge, tradingDay: { timeZone: "America/New_York", start: "17:00:00" } };
    const details = testDetails({ challenge: newYork, results: { ...testDetails().results, dayStartedAt: "2026-10-04T21:00:00Z" } });

    // Sunday 17:00 in New York starts Sunday's trading day.
    expect(currentTradingDay(details)).toBe("2026-10-04");
  });

  it("is unknown while the account is not traded", () => {
    expect(currentTradingDay(testDetails({ results: { ...testDetails().results, dayStartedAt: null } }))).toBeNull();
  });
});

describe("deadlineOf", () => {
  it("gives the last day to act and the days left, today included", () => {
    expect(deadlineOf(testDetails(), "2026-11-02")).toEqual({ lastDay: "2026-11-01", daysLeft: 27 });
    expect(deadlineOf(testDetails(), "2026-10-07")).toEqual({ lastDay: "2026-10-06", daysLeft: 1 });
    expect(deadlineOf(testDetails(), null)).toBeNull();
  });
});

describe("attentionItems", () => {
  it("is empty for an account far from its limits and deadlines", () => {
    expect(attentionItems([testDetails()])).toEqual([]);
  });

  it("puts a limit about to be broken first, before good news", () => {
    const close = testDetails({
      live: {
        balance: 97_400,
        equity: 97_300,
        floors: [
          { floorId: "daily", level: 97_000, headroom: 300, distance: 5_000 },
          { floorId: "max-loss", level: 90_000, headroom: 7_300, distance: 10_000 },
        ],
      },
    });
    const ready = testDetails({
      account: {
        ...testAccount,
        id: "funded",
        number: 1002,
        funded: true,
        stage: 2,
        stageName: "Funded",
        nextPayout: { canRequest: true, refusal: null, profit: 3_120, profitSplitPercent: 80, amount: 2_496, tradingDays: 6, minTradingDays: 5 },
      },
    });

    const items = attentionItems([ready, close]);

    expect(items.map((i) => [i.tone, i.title])).toEqual([
      ["danger", "#1001 is 300.00 USD from its daily loss limit."],
      ["profit", "A payout of 2,496.00 USD is ready on #1002."],
    ]);
    expect(items[0].detail).toContain("Equity may not fall below 97,000.00 today.");
    expect(items[1].action).toBe("Request payout");
  });

  it("warns about a deadline in the next few days, and about a paused account", () => {
    const soon = testDetails({ account: { ...testAccount, inactivityDeadline: "2026-10-09", paused: true } });

    expect(attentionItems([soon]).map((i) => i.title)).toEqual(["Open a trade on #1001 by 8 Oct 2026.", "#1001 is paused by the firm."]);
  });

  it("tells the trader when every stage is passed", () => {
    const passed = testDetails({ account: { ...testAccount, status: "AwaitingFunding", tradingAccountId: null }, live: null });

    expect(attentionItems([passed]).map((i) => i.title)).toEqual(["#1001 passed every stage."]);
  });
});

describe("objectivesOf", () => {
  it("shows the target, the loss limits, the trading days and how long the trader may wait", () => {
    const objectives = objectivesOf(testDetails());

    expect(objectives.map((o) => [o.key, o.state, o.stateText])).toEqual([
      ["target", "progress", "In progress"],
      ["daily", "kept", "Kept"],
      ["max-loss", "kept", "Kept"],
      ["days", "progress", "2 of 4"],
      ["activity", "info", "30 days left"],
    ]);
    expect(objectives[0]).toMatchObject({ progress: 25, detail: "2,500.00 of 10,000.00 · reach a balance of 110,000.00" });
    expect(objectives[1].detail).toMatch(/^Equity may fall 5,800.00 more today, to 97,000.00. It starts again /);
    expect(objectives[3].segments).toEqual({ filled: 2, total: 4 });
  });

  it("shows the limit that failed the account, with the evidence", () => {
    const failed = testDetails({
      account: { ...testAccount, status: "Failed", inactivityDeadline: null },
      live: null,
      breach: { time: "2026-10-06T09:30:00Z", floorId: "daily", level: 97_000, equity: 96_950, reason: "DailyLoss" },
    });

    const objectives = objectivesOf(failed);
    const daily = objectives.find((o) => o.key === "daily");

    expect(daily).toMatchObject({ state: "broken", stateText: "Broken" });
    expect(daily?.detail).toMatch(/^Equity 96,950.00 fell below 97,000.00 on /);

    // What the account no longer works towards is not shown as in progress.
    expect(objectives.find((o) => o.key === "target")).toMatchObject({ state: "info", stateText: "Not reached" });
    expect(objectives.find((o) => o.key === "days")).toMatchObject({ state: "info", stateText: "2 of 4" });
  });

  it("counts a funded account's trading days towards the next payout", () => {
    const funded = testDetails({
      account: {
        ...testAccount,
        funded: true,
        stage: 2,
        stageName: "Funded",
        profitTarget: null,
        nextPayout: { canRequest: false, refusal: "A payout needs 5 trading days since the last one. So far: 3.", profit: 500, profitSplitPercent: 80, amount: 400, tradingDays: 3, minTradingDays: 5 },
      },
      results: { ...testDetails().results, targetGained: null, targetRequired: null, targetPercent: null },
    });

    const objectives = objectivesOf(funded);

    expect(objectives.map((o) => o.key)).toEqual(["daily", "max-loss", "days", "activity"]);
    expect(objectives[2]).toMatchObject({ title: "Trading days for a payout", stateText: "3 of 5", segments: { filled: 3, total: 5 } });
  });
});

describe("stepState and statusText", () => {
  const stages = testDetails().stages;

  it("shows where each stage is, also when the challenge has ended", () => {
    expect(stepState(stages[0], "Active")).toBe("current");
    expect(stepState(stages[0], "Failed")).toBe("failed");
    expect(stepState(stages[0], "AwaitingFunding")).toBe("waiting");
    expect(stepState({ ...stages[0], progress: "Passed" }, "Active")).toBe("passed");
    expect(stepState(stages[1], "Active")).toBe("upcoming");
  });

  it("names the stage, or how the account ended", () => {
    const expired: AccountDetails = testDetails({ account: { ...testAccount, status: "Failed" }, expiry: { time: "2026-11-05T23:00:00Z", reason: "Inactivity", day: "2026-11-06" } });

    expect(statusText(testDetails())).toBe("Phase 1");
    expect(statusText(testDetails({ account: { ...testAccount, paused: true } }))).toBe("Phase 1 · Paused");
    expect(statusText(expired)).toBe("Ended");
    expect(endingText(expired)).toBe("no new trade in time");
  });
});
