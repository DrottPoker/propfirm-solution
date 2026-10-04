import { describe, expect, it } from "vitest";

import { accountStatus, activityView, ageText, axisTicks, formatTotals, needsYouItems, passRateText, salesAndPayouts, shortAmount, weekBars, whenText } from "./admin";
import type { Activity, PayoutSummary, Slots } from "./api/types";
import { testAccount } from "./testAccounts";

const now = Date.parse("2026-10-07T12:00:00Z");

const noPayouts: PayoutSummary = {
  toApprove: { count: 0, totals: [], oldest: null },
  toPay: { count: 0, totals: [], oldest: null },
  paidLast30Days: { count: 0, totals: [], oldest: null },
  averageDaysToPay: null,
};

const slots: Slots = { limit: "Paid", slots: 60, used: 47, reserved: 1, free: 12, paid: true, suspended: false, warning: true };

function activity(changes: Partial<Activity>): Activity {
  return {
    kind: "ChallengeStarted",
    time: "2026-10-07T08:40:00Z",
    accountId: testAccount.id,
    accountNumber: 1001,
    email: "anna@test.example",
    challengeName: "Two-step 100K",
    stageName: null,
    amount: null,
    currency: null,
    orderNumber: null,
    tradingDays: null,
    reason: null,
    reference: null,
    ...changes,
  };
}

describe("accountStatus", () => {
  it("names where the account is, with a tone for the badge", () => {
    expect(accountStatus(testAccount)).toEqual({ label: "Trading", tone: "accent" });
    expect(accountStatus({ ...testAccount, paused: true })).toEqual({ label: "Paused", tone: "warning" });
    expect(accountStatus({ ...testAccount, status: "AwaitingFunding" })).toEqual({ label: "Waiting for your approval", tone: "warning" });
    expect(accountStatus({ ...testAccount, status: "Failed" })).toEqual({ label: "Failed", tone: "loss" });
    expect(accountStatus({ ...testAccount, status: "Cancelled", paused: true })).toEqual({ label: "Cancelled", tone: "muted" });
  });
});

describe("formatTotals", () => {
  it("writes each currency's amount, and zero in the firm's currency when there is none", () => {
    expect(formatTotals([{ currency: "USD", amount: 19_498 }], "USD")).toBe("19,498.00 USD");
    expect(formatTotals([{ currency: "EUR", amount: 50 }, { currency: "USD", amount: 120 }], "USD")).toBe("50.00 EUR + 120.00 USD");
    expect(formatTotals([], "USD")).toBe("0.00 USD");
  });
});

describe("passRateText", () => {
  it("is the share that passed, and a dash before any evaluation has ended", () => {
    expect(passRateText({ passed: 9, ended: 74 })).toBe("12%");
    expect(passRateText({ passed: 0, ended: 0 })).toBe("-");
  });
});

describe("ageText", () => {
  it("says how long ago in the largest whole unit", () => {
    expect(ageText("2026-10-07T11:59:40Z", now)).toBe("just now");
    expect(ageText("2026-10-07T11:48:00Z", now)).toBe("12 min ago");
    expect(ageText("2026-10-07T11:00:00Z", now)).toBe("1 hour ago");
    expect(ageText("2026-10-07T08:00:00Z", now)).toBe("4 hours ago");
    expect(ageText("2026-10-06T10:00:00Z", now)).toBe("1 day ago");
    expect(ageText("2026-10-05T14:20:00Z", now)).toBe("1 day ago");
    expect(ageText("2026-10-05T11:00:00Z", now)).toBe("2 days ago");
  });
});

describe("whenText", () => {
  // Shown in the browser's own time zone, so the times here are those of the machine the test runs on.
  const local = (day: number, hour: number, minute: number) => new Date(2026, 9, day, hour, minute).toISOString();

  it("is the time today, yesterday, and the date before", () => {
    const at = Date.parse(local(7, 12, 0));

    expect(whenText(local(7, 8, 40), at)).toBe("08:40");
    expect(whenText(local(6, 22, 10), at)).toBe("Yesterday");
    expect(whenText(local(3, 9, 0), at)).toBe("3 Oct");
  });
});

describe("needsYouItems", () => {
  const input = { payouts: noPayouts, waitingAccounts: [], waitingCount: 0, challengeName: () => "Two-step 100K", slots: null, currency: "USD", now };

  it("is empty when nothing waits for the firm", () => {
    expect(needsYouItems(input)).toEqual([]);
  });

  it("puts payouts first, then the accounts waiting for a funded account, then the slots", () => {
    const items = needsYouItems({
      ...input,
      payouts: {
        ...noPayouts,
        toApprove: { count: 3, totals: [{ currency: "USD", amount: 7_488 }], oldest: "2026-10-05T11:00:00Z" },
        toPay: { count: 1, totals: [{ currency: "USD", amount: 2_400 }], oldest: "2026-10-06T09:00:00Z" },
      },
      waitingAccounts: [{ ...testAccount, status: "AwaitingFunding", email: "lena.berg@example.com", number: 1018 }],
      waitingCount: 1,
      slots,
    });

    expect(items.map((i) => i.title)).toEqual([
      "3 payouts wait for your approval",
      "An approved payout to pay",
      "lena.berg@example.com passed Two-step 100K",
      "48 of 60 slots are taken",
    ]);
    expect(items[0].detail).toBe("7,488.00 USD in total. The oldest was asked for 2 days ago.");
    expect(items[1]).toMatchObject({ detail: "2,400.00 USD. Send the money, then mark it as paid.", href: "/admin/payouts?view=to-pay" });
    expect(items[2].href).toBe(`/admin/accounts/${testAccount.id}`);
  });

  it("names a few waiting accounts and counts the rest", () => {
    const waiting = [1, 2, 3, 4].map((n) => ({ ...testAccount, id: `id-${n}`, number: 1000 + n, status: "AwaitingFunding" as const }));

    const items = needsYouItems({ ...input, waitingAccounts: waiting, waitingCount: 5 });

    expect(items.map((i) => i.key)).toEqual(["approve-id-1", "approve-id-2", "approve-id-3", "approve-more"]);
    expect(items[3]).toMatchObject({ title: "2 more accounts wait for a funded account", href: "/admin/accounts?group=AwaitingFunding" });
  });

  it("puts a live shop that sells nothing first", () => {
    const items = needsYouItems({ ...input, slots, shopProblem: "Your shop sells nothing, since your Stripe keys are test keys." });

    expect(items.map((i) => i.key)).toEqual(["shop", "slots"]);
    expect(items[0]).toMatchObject({ title: "Your shop takes no payment", href: "/admin/checkout", tone: "warning" });
  });

  it("warns about slots only when they run low or out, and never without a limit", () => {
    expect(needsYouItems({ ...input, slots: { ...slots, warning: false } })).toEqual([]);
    expect(needsYouItems({ ...input, slots: { ...slots, warning: false, used: 59, reserved: 1, free: 0 } })).toHaveLength(1);
    expect(needsYouItems({ ...input, slots: { ...slots, slots: null, warning: true } })).toEqual([]);
  });
});

describe("activityView", () => {
  it("words each kind of activity", () => {
    expect(activityView(activity({ kind: "ChallengeBought", amount: 299, currency: "USD" }))).toMatchObject({ title: "Challenge bought", note: "299.00 USD" });
    expect(activityView(activity({ kind: "StagePassed", stageName: "Phase 1", amount: 10_410, tradingDays: 5 }))).toMatchObject({
      title: "Passed Phase 1",
      note: "+10,410.00 in 5 trading days",
      tone: "profit",
    });
    expect(activityView(activity({ kind: "ChallengeFailed", stageName: "Phase 1", reason: "DailyLoss" }))).toMatchObject({
      title: "Failed Phase 1",
      note: "daily loss limit",
      tone: "loss",
    });
    expect(activityView(activity({ kind: "ChallengeExpired", stageName: "Phase 2", reason: "Inactivity" })).note).toBe("no new trade for too long");
    expect(activityView(activity({ kind: "PayoutPaid", amount: 1_840, currency: "USD", reference: "TRF-88412" })).note).toBe("1,840.00 USD · reference TRF-88412");
    expect(activityView(activity({ kind: "EvaluationPassed" })).title).toBe("Passed every stage");
  });
});

describe("weekBars", () => {
  const weeks = salesAndPayouts([
    { start: "2026-09-21", sales: [{ currency: "USD", amount: 5_600 }], payouts: [{ currency: "USD", amount: 4_800 }] },
    { start: "2026-09-28", sales: [{ currency: "USD", amount: 3_698 }, { currency: "EUR", amount: 99 }], payouts: [] },
  ]);

  it("takes the amounts in the firm's currency and says when others were left out", () => {
    const chart = weekBars(weeks, "USD");

    expect(chart.bars).toEqual([
      { start: "2026-09-21", first: 5_600, second: 4_800 },
      { start: "2026-09-28", first: 3_698, second: 0 },
    ]);
    expect(chart.otherCurrencies).toBe(true);
    expect(chart.ticks).toEqual([0, 2_000, 4_000, 6_000]);
  });

  it("draws an empty chart on a unit axis", () => {
    expect(weekBars([{ start: "2026-09-28", first: [], second: [] }], "USD")).toEqual({
      bars: [{ start: "2026-09-28", first: 0, second: 0 }],
      ticks: [0, 1],
      otherCurrencies: false,
    });
  });
});

describe("axisTicks", () => {
  it("reaches at least the highest value in clean steps", () => {
    expect(axisTicks(5_600)).toEqual([0, 2_000, 4_000, 6_000]);
    expect(axisTicks(250)).toEqual([0, 100, 200, 300]);
  });
});

describe("shortAmount", () => {
  it("shortens thousands and millions", () => {
    expect([shortAmount(0), shortAmount(500), shortAmount(6_000), shortAmount(2_500), shortAmount(1_500_000)]).toEqual(["0", "500", "6k", "2.5k", "1.5M"]);
  });
});
