import { describe, expect, it } from "vitest";

import type { AccountDetails } from "./api/types";
import {
  attentionItems,
  breachClosesText,
  breachStory,
  currentTradingDay,
  deadlineOf,
  deadlineText,
  endingText,
  floorState,
  initials,
  latestMilestone,
  nextStepsOf,
  objectivesOf,
  roomShare,
  resultTone,
  retryOf,
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

  it("takes them from the name when there is one", () => {
    expect([initials("owner@test.com", "Maja Lind"), initials("owner@test.com", " maja  von lind "), initials("owner@test.com", "Maja"), initials("owner@test.com", " ")]).toEqual([
      "ML",
      "ML",
      "MA",
      "OW",
    ]);
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

describe("deadlineText", () => {
  it("says the last day and the days until it, as the rule counts them", () => {
    expect(deadlineText("Trade", { lastDay: "2026-11-03", daysLeft: 31 })).toBe("Trade by 3 Nov (30 days)");
    expect(deadlineText("Pass", { lastDay: "2026-10-07", daysLeft: 2 })).toBe("Pass by 7 Oct (1 day)");
    expect(deadlineText("Trade", { lastDay: "2026-10-06", daysLeft: 1 })).toBe("Trade today");
    expect(deadlineText("Trade", { lastDay: "2026-10-05", daysLeft: 0 })).toBe("Ends today");
    expect(deadlineText("Trade", { lastDay: "2026-11-03", daysLeft: null })).toBe("Trade by 3 Nov");
  });
});

describe("objectivesOf", () => {
  it("shows the target, the loss limits, the trading days and how long the trader may wait", () => {
    const objectives = objectivesOf(testDetails());

    expect(objectives.map((o) => [o.key, o.state, o.stateText])).toEqual([
      ["target", "progress", "In progress"],
      ["daily", "kept", "5,800.00 left"],
      ["max-loss", "kept", "12,800.00 left"],
      ["days", "progress", "2 of 4"],
      ["activity", "info", "Trade by 4 Nov (29 days)"],
    ]);
    expect(objectives[0]).toMatchObject({
      progress: 25,
      ghost: 28,
      detail: "2,500.00 of 10,000.00 · reach a balance of 110,000.00. Closed trades count: with your open ones closed now, it would be 2,800.00.",
    });
    expect(objectives[1].detail).toMatch(/^Equity may not fall below 97,000.00 today. It starts again /);
    expect(objectives[1].room).toEqual({ share: 100, state: "ok" });
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

    // What the account no longer works towards is not shown as in progress, nor with a bar towards it.
    expect(objectives.find((o) => o.key === "target")).toMatchObject({ state: "info", stateText: "Not reached", detail: "The stage ended before the balance reached 110,000.00." });
    expect(objectives.find((o) => o.key === "target")?.progress).toBeUndefined();
    expect(objectives.find((o) => o.key === "days")).toMatchObject({ state: "info", stateText: "2 of 4" });
  });

  it("says why a breached account's balance ended below the limit", () => {
    const failed = testDetails({
      account: { ...testAccount, status: "Failed" },
      breach: {
        time: "2026-10-06T09:30:00Z",
        floorId: "daily",
        level: 97_000,
        equity: 96_950,
        reason: "DailyLoss",
        closes: [{ symbol: "EURUSD", side: "Buy", volume: 1, closePrice: 1.07512, profit: -3_040, commission: 3.5 }],
        balanceAfter: 96_901.5,
      },
    });

    expect(breachClosesText(failed)).toBe(
      "Then every open position was closed at those prices (EURUSD at 1.07512), and 3.50 in commission was charged for closing it, so the balance ended at 96,901.50.",
    );
    expect(breachClosesText(testDetails())).toBeNull();
    expect(breachStory(failed)).toEqual([
      { when: "6 Oct 2026, 11:30", text: "Equity fell to 96,950.00, below the daily loss limit at 97,000.00." },
      { when: "Right after", text: "Every open position was closed at the prices that broke it: EURUSD at 1.07512. Closing it cost 3.50 in commission." },
      { when: "Then", text: "The challenge ended with a balance of 96,901.50." },
    ]);
    expect(breachStory(testDetails())).toEqual([]);
  });

  it("offers a new try at a failed challenge, with the firm's code for retries", () => {
    const retry = { challengeId: "two-step-100k", price: 499, currency: "USD", discountCode: "COMEBACK", amount: 349.3 };

    expect(retryOf(testDetails({ retry }))).toEqual({ href: "/buy?challenge=two-step-100k&code=COMEBACK", label: "Try again for 349.30 USD instead of 499.00" });
    expect(retryOf(testDetails({ retry: { ...retry, discountCode: null, amount: null } }))).toEqual({ href: "/buy?challenge=two-step-100k", label: "Try again for 499.00 USD" });
    expect(retryOf(testDetails())).toBeNull();
  });

  it("shows a funded account's consistency rule with its best day", () => {
    const funded = testDetails({
      account: {
        ...testAccount,
        funded: true,
        stage: 2,
        stageName: "Funded",
        profitTarget: null,
        nextPayout: {
          canRequest: false,
          refusal: "Your best day made 900.00, 90% of the profit.",
          profit: 1_000,
          profitSplitPercent: 80,
          amount: 800,
          tradingDays: 5,
          minTradingDays: 0,
          bestDayProfit: 900,
          consistencyPercent: 40,
        },
      },
      results: { ...testDetails().results, targetGained: null, targetRequired: null, targetPercent: null },
    });

    expect(objectivesOf(funded).find((o) => o.key === "consistency")).toMatchObject({ title: "Consistency", state: "warning", stateText: "Best day 90%" });
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

describe("roomShare", () => {
  it("is the share of the limit's distance left, from 0 to 100", () => {
    expect([
      roomShare({ headroom: 1_250, distance: 5_000 }),
      roomShare({ headroom: -10, distance: 5_000 }),
      roomShare({ headroom: 5_800, distance: 5_000 }),
    ]).toEqual([25, 0, 100]);
    expect(roomShare({ headroom: 10, distance: null })).toBe(100);
  });
});

describe("nextStepsOf", () => {
  it("says what is left of the phase, and the deadline", () => {
    expect(nextStepsOf(testDetails()).map((s) => s.text)).toEqual([
      "Make 7,500.00 more to reach the profit target",
      "Trade on 2 more days",
      "Open a trade by 4 Nov (29 days)",
    ]);
  });

  it("points a funded trader to the payout when it can be asked for", () => {
    const quote = { canRequest: true, refusal: null, profit: 2_000, profitSplitPercent: 80, amount: 1_600, tradingDays: 5, minTradingDays: 5 };
    const funded = testDetails({ account: { ...testAccount, funded: true, stage: 2, stageName: "Funded", nextPayout: quote, inactivityDeadline: null } });
    const early = testDetails({ account: { ...funded.account, nextPayout: { ...quote, canRequest: false, tradingDays: 4 } } });

    expect(nextStepsOf(funded)).toEqual([{ key: "payout", text: "Ask for your payout of 1,600.00 USD", tone: "profit" }]);
    expect(nextStepsOf(early).map((s) => s.text)).toEqual(["Trade on 1 more day before the next payout"]);
  });

  it("is empty while the account cannot trade", () => {
    expect(nextStepsOf(testDetails({ account: { ...testAccount, paused: true } }))).toEqual([]);
    expect(nextStepsOf(testDetails({ account: { ...testAccount, status: "Failed" } }))).toEqual([]);
  });
});

describe("latestMilestone", () => {
  const base = testDetails();

  it("is the last passed phase, with its result and what comes next", () => {
    const details = testDetails({
      account: { ...testAccount, stage: 1, stageName: "Phase 2" },
      stages: base.stages.map((s) =>
        s.stage === 0
          ? { ...s, progress: "Passed", passedAt: "2026-10-05T15:00:00Z", result: 10_200, tradingDays: 5 }
          : s.stage === 1
            ? { ...s, progress: "Current" }
            : s,
      ),
    });

    expect(latestMilestone(details, "Aurora Funded")).toEqual({
      key: `${testAccount.id}:passed:0`,
      kind: "passed",
      title: "Phase 1 passed",
      detail: "You passed it with +10,200.00 in 5 trading days. Next up: Phase 2.",
      at: "2026-10-05T15:00:00Z",
    });
  });

  it("is the funded account once it trades, and nothing before a phase is passed", () => {
    const funded = testDetails({
      account: { ...testAccount, funded: true, stage: 2, stageName: "Funded" },
      stages: base.stages.map((s) =>
        s.stage < 2 ? { ...s, progress: "Passed", passedAt: "2026-10-05T15:00:00Z" } : { ...s, progress: "Current", startedAt: "2026-10-06T09:00:00Z" },
      ),
    });

    expect(latestMilestone(funded, "Aurora Funded")).toMatchObject({
      kind: "funded",
      title: "You're funded",
      detail: "You now trade Aurora Funded's capital and keep 80% of the profit.",
    });
    expect(latestMilestone(base, "Aurora Funded")).toBeNull();
  });

  it("is the newest paid payout, with its amount", () => {
    const paid = { ...payoutFields, id: "p2", status: "Paid" as const, amount: 1_600, paidAt: "2026-11-02T09:00:00Z" };
    const funded = testDetails({ account: { ...testAccount, funded: true, stage: 2, stageName: "Funded" }, payouts: [paid] });

    expect(latestMilestone(funded, "Aurora Funded")).toMatchObject({
      key: `${testAccount.id}:paid:p2`,
      kind: "paid",
      amount: 1_600,
      detail: "Aurora Funded has sent your 1,600.00 USD. Your certificate for it is ready to share.",
    });
  });
});

const payoutFields: AccountDetails["payouts"][number] = {
  id: "p",
  accountId: testAccount.id,
  accountNumber: 1001,
  email: "anna@test.example",
  tradingAccountId: "demo-firm-1001-3",
  status: "Paid",
  profit: 2_000,
  profitSplitPercent: 80,
  amount: 1_600,
  currency: "USD",
  requestedAt: "2026-11-01T08:00:00Z",
  withdrawnAt: "2026-11-01T08:00:01Z",
  approvedAt: "2026-11-01T10:00:00Z",
  paidAt: "2026-11-02T09:00:00Z",
  rejectedAt: null,
  failedAt: null,
  reason: null,
  reference: null,
  payTo: null,
  profitReturned: false,
  timeZone: "Europe/Stockholm",
};
