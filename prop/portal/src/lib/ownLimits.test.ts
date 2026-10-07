import { describe, expect, it } from "vitest";

import type { LockedDay, OwnLimitAmounts } from "./api/types";
import { amountLimitText, countLimitText, hasOwnLimits, lockedDayText, lockText, pendingText } from "./ownLimits";

const none: OwnLimitAmounts = { dailyLoss: null, dailyTarget: null, maxTrades: null };

function day(fields: Partial<LockedDay>): LockedDay {
  return { time: "2026-10-07T14:32:08Z", reason: "DailyLoss", until: "2026-10-07T22:00:00Z", limit: 1_500, dayResult: -1_531.6, positionsClosed: 2, ...fields };
}

describe("the trader's own limits", () => {
  it("knows whether the trader set any, today or from tomorrow", () => {
    expect([hasOwnLimits(none, null), hasOwnLimits(none, { ...none, maxTrades: 4 }), hasOwnLimits({ ...none, dailyLoss: 500 }, null)]).toEqual([false, true, true]);
  });

  it("writes each limit, and what a loosened one becomes from the next trading day", () => {
    const money = (v: number | null) => amountLimitText(v, "USD");
    const pending = { dailyLoss: 2_000, dailyTarget: null, maxTrades: 6 };

    expect([money(1_500), money(null), countLimitText(6), countLimitText(null)]).toEqual(["1,500.00 USD", "Off", "6", "Off"]);
    expect(pendingText(1_500, pending, (l) => l.dailyLoss, money)).toBe("2,000.00 USD from the next trading day");
    expect(pendingText(800, pending, (l) => l.dailyTarget, money)).toBe("Off from the next trading day");
    expect([pendingText(6, pending, (l) => l.maxTrades, countLimitText), pendingText(1_500, null, (l) => l.dailyLoss, money)]).toEqual([null, null]);
  });

  it("says what locked new orders and until when, in the trading day's time zone", () => {
    const until = "2026-10-07T22:00:00Z";
    expect([
      lockText({ until, reason: "DailyLoss" }, "Europe/Stockholm"),
      lockText({ until, reason: "DailyTarget" }, "Europe/Stockholm"),
      lockText({ until, reason: "Trader" }, "UTC"),
    ]).toEqual(["Daily loss limit reached, locked until 00:00", "Profit target reached, locked until 00:00", "Locked by the trader until 22:00"]);
  });

  it("describes each locked day", () => {
    expect(lockedDayText(day({}), "USD")).toBe("Daily loss limit of 1,500.00 USD reached. 2 positions closed.");
    expect(lockedDayText(day({ reason: "DailyTarget", limit: 1_000, positionsClosed: 1 }), "USD")).toBe("Daily profit target of 1,000.00 USD reached. 1 position closed.");
    expect(lockedDayText(day({ reason: "Trader", limit: null, positionsClosed: 0 }), "USD")).toBe("The trader locked the rest of the day.");
    expect(lockedDayText(day({ reason: "Trader", limit: null, positionsClosed: 3 }), "USD")).toBe("The trader locked the rest of the day. 3 positions closed.");
  });
});
