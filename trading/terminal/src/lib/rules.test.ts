import { describe, expect, it } from "vitest";

import { attentionOf, deadlineState, rulebook, timeLeftText } from "./rules";
import { accountWith, daily, rulesWith } from "./rules.fixtures";

const now = Date.parse("2026-10-07T10:00:00Z");
const timeZone = "Europe/Stockholm";

describe("timeLeftText", () => {
  it.each([
    ["2026-10-10T11:00:00Z", "3 days left"],
    ["2026-10-09T09:59:00Z", "47 h left"],
    ["2026-10-07T10:12:30Z", "12 min left"],
    ["2026-10-07T10:00:20Z", "1 min left"],
    ["2026-10-07T10:00:00Z", "Passed"],
  ])("counts down to %s as %j", (deadline, expected) => {
    expect(timeLeftText(deadline, now)).toBe(expected);
  });
});

describe("deadlineState", () => {
  it.each([
    ["2026-10-10T10:00:00Z", "ok"],
    ["2026-10-10T09:59:00Z", "warning"],
    ["2026-10-08T09:59:00Z", "danger"],
    ["2026-10-06T10:00:00Z", "danger"],
  ])("marks %s as %s", (deadline, expected) => {
    expect(deadlineState(deadline, now)).toBe(expected);
  });
});

describe("rulebook", () => {
  it("shows an evaluation stage's target, limits, trading days and deadlines", () => {
    const rows = rulebook({ account: accountWith(), profitTarget: 110_000, rules: rulesWith(), now, timeZone });

    expect(rows.map((r) => [r.label, r.value, r.note, r.state])).toEqual([
      ["Profit target", "110,000.00", "8,000.00 to go", "ok"],
      ["Daily loss limit", "95,000.00", "5,000.00 left", "ok"],
      ["Trading days", "1 of 4", undefined, "ok"],
      ["Time limit", "None", undefined, "ok"],
      ["Open a position by", "Fri 6 Nov 00:00", "29 days left", "ok"],
    ]);
  });

  it("marks what is met and what is close", () => {
    const rows = rulebook({
      account: accountWith({ balance: 110_500, floors: [daily(400)] }),
      profitTarget: 110_000,
      rules: rulesWith({ tradingDaysCounted: 4, passBy: "2026-10-08T22:00:00Z" }),
      now,
      timeZone,
    });

    expect(Object.fromEntries(rows.map((r) => [r.id, r.state]))).toEqual({
      target: "done",
      "floor-daily": "danger",
      "trading-days": "done",
      "pass-by": "warning",
      "open-by": "ok",
    });
    expect(attentionOf(rows)).toBe("danger");
  });

  it("shows a funded account's days toward a payout and the consistency rule", () => {
    const rows = rulebook({
      account: accountWith(),
      profitTarget: null,
      rules: rulesWith({ funded: true, tradingDaysRequired: 5, tradingDaysCounted: 2, consistencyPercent: 40, bestDayPercent: 60 }),
      now,
      timeZone,
    });

    expect(rows.map((r) => [r.label, r.value, r.note, r.state])).toEqual([
      ["Daily loss limit", "95,000.00", "5,000.00 left", "ok"],
      ["Trading days for a payout", "2 of 5", undefined, "ok"],
      ["Open a position by", "Fri 6 Nov 00:00", "29 days left", "ok"],
      ["Best day's share", "60%", "At most 40%", "warning"],
    ]);
  });

  it("tells a funded trader their share and whether a payout can be asked for", () => {
    const row = (payoutAvailable: boolean) =>
      rulebook({ account: accountWith(), profitTarget: null, rules: rulesWith({ funded: true, profitSplitPercent: 80, payoutAvailable }), now, timeZone }).find(
        (r) => r.id === "payout",
      );

    expect(row(true)).toMatchObject({ value: "80% of the profit", note: "You can ask for one now", state: "done", action: "payout" });
    expect(row(false)).toMatchObject({ note: "Not yet", state: "ok", action: undefined });
    expect(rulebook({ account: accountWith(), profitTarget: 110_000, rules: rulesWith({ profitSplitPercent: 80 }), now, timeZone }).some((r) => r.id === "payout")).toBe(false);
  });

  // An account of a firm whose system tells no rules says nothing it does not know.
  it("leaves out what the firm's system has not told", () => {
    const none = rulesWith({ tradingDaysRequired: null, tradingDaysCounted: null, openPositionBy: null });

    const rows = rulebook({ account: accountWith(), profitTarget: null, rules: none, now, timeZone });

    expect(rows.map((r) => r.id)).toEqual(["floor-daily"]);
    expect(attentionOf(rows)).toBeNull();
  });

  it("shows only a broken limit once trading has ended", () => {
    const rows = rulebook({
      account: accountWith({ status: "Disabled", floors: [daily(-20), { ...daily(300), floorId: "max-loss" }] }),
      profitTarget: 110_000,
      rules: rulesWith(),
      now,
      timeZone,
    });

    expect(rows.filter((r) => r.id.startsWith("floor")).map((r) => [r.id, r.state, r.note])).toEqual([
      ["floor-daily", "danger", "Broken"],
      ["floor-max-loss", "ok", undefined],
    ]);
    expect(rows.some((r) => r.id === "target" || r.id === "pass-by")).toBe(false);
  });
});
