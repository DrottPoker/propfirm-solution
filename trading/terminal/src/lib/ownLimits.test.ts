import { describe, expect, it } from "vitest";

import type { EngineEvent } from "./api/types";
import { describeEvent, isWarning, rejectionText } from "./events";
import {
  closeness,
  firmDailyLoss,
  limitsForm,
  lockDescription,
  lockEvent,
  lockTitle,
  noLimits,
  orderLockText,
  ownLimitRows,
  parseLimitsForm,
  pendingText,
  stricterOf,
  timeUntilText,
  tradesUsed,
  tradesUsedText,
} from "./ownLimits";
import { daily, nextDayStart, ownLimitsWith } from "./rules.fixtures";

const timeZone = "Europe/Stockholm";
const timestamp = "2026-10-05T14:32:08Z";

describe("stricterOf", () => {
  it("applies a stricter limit at once and keeps the stricter one where the trader loosens", () => {
    const now = { dailyLoss: 1_500, dailyTarget: 1_000, maxTrades: 6 };

    expect(stricterOf(now, { dailyLoss: 2_000, dailyTarget: 800, maxTrades: null })).toEqual({ dailyLoss: 1_500, dailyTarget: 800, maxTrades: 6 });
    expect(stricterOf(noLimits, { dailyLoss: 500, dailyTarget: null, maxTrades: 3 })).toEqual({ dailyLoss: 500, dailyTarget: null, maxTrades: 3 });
  });
});

describe("ownLimitRows", () => {
  it("shows each limit that is on, with the room left, the way to the target and the trades used", () => {
    const own = ownLimitsWith({
      limits: { dailyLoss: 1_500, dailyTarget: 1_000, maxTrades: 6 },
      lossLevel: 98_500,
      targetLevel: 101_000,
      tradesToday: 3,
    });

    const rows = ownLimitRows(own, 99_400);

    expect(rows.map((r) => [r.id, r.label, r.value, r.tone])).toEqual([
      ["loss", "Daily loss limit, 1,500.00", "900.00 left", "accent"],
      ["target", "Daily profit target, 1,000.00", "1,600.00 to go", "profit"],
      ["trades", "Trades a day", "3 of 6", "accent"],
    ]);
    expect(rows.map((r) => r.used)).toEqual([0.4, 0, 0.5]);
    expect(rows[0].note).toBe("Positions close and the day locks when equity reaches 98,500.00.");
  });

  it("colors the loss limit as it comes close, and says when a limit is reached", () => {
    const own = ownLimitsWith({ limits: { dailyLoss: 1_000, dailyTarget: null, maxTrades: 2 }, lossLevel: 99_000, tradesToday: 2 });

    expect(ownLimitRows(own, 99_200).map((r) => [r.value, r.tone])).toEqual([
      ["200.00 left", "warning"],
      ["2 of 2", "warning"],
    ]);
    expect(ownLimitRows(own, 98_900)[0].value).toBe("Reached");
    expect([closeness(50, 1_000), closeness(0, 1_000), closeness(600, 1_000)]).toEqual(["danger", "danger", "ok"]);
  });
});

describe("the lock and the trades used", () => {
  it("says what locked the day, until when and that the challenge goes on", () => {
    const loss = { until: nextDayStart, reason: "DailyLoss" as const };
    const trader = { until: nextDayStart, reason: "Trader" as const };

    expect(lockTitle(loss, timestamp, timeZone)).toBe("Your own daily loss limit was reached at 16:32:08 Stockholm time");
    expect(lockTitle(trader, null, timeZone)).toBe("You locked the rest of the day");
    expect(lockDescription(loss, 2, timeZone)).toBe(
      "Every position was closed at that price. New orders are locked until the next trading day starts at 00:00 Stockholm time. This was your limit, not the firm's. The challenge goes on.",
    );
    expect(lockDescription({ ...loss, reason: "DailyTarget" }, 0, timeZone, "The account stays open.")).toBe(
      "New orders are locked until the next trading day starts at 00:00 Stockholm time. This was your limit, not the firm's. The account stays open.",
    );
    expect(orderLockText(trader, timeZone)).toBe(
      "You locked the rest of the day, until 00:00 Stockholm time. You can still close positions and change their stops.",
    );
  });

  it("finds the event that locked the day", () => {
    const locked: EngineEvent = {
      kind: "TradingLocked",
      accountId: "A1",
      reason: "DailyLoss",
      until: nextDayStart,
      limit: 1_500,
      dayResult: -1_531.6,
      positionsClosed: 2,
      timestamp,
    };

    expect(lockEvent([locked], { until: nextDayStart, reason: "DailyLoss" })).toBe(locked);
    expect(lockEvent([locked], { until: "2026-10-06T22:00:00Z", reason: "Trader" })).toBeUndefined();
  });

  it("knows when the day's trades are used", () => {
    const used = ownLimitsWith({ limits: { dailyLoss: null, dailyTarget: null, maxTrades: 6 }, tradesToday: 6 });

    expect([tradesUsed(used), tradesUsed(ownLimitsWith({ tradesToday: 40 }))]).toEqual([true, false]);
    expect(tradesUsedText(used, timeZone)).toBe("You have opened your 6 trades for today. New orders are taken again from 00:00 Stockholm time.");
    expect([timeUntilText(nextDayStart, Date.parse("2026-10-05T14:32:00Z")), timeUntilText(nextDayStart, Date.parse("2026-10-05T21:00:00Z"))]).toEqual([
      "7 hours 28 minutes",
      "1 hour",
    ]);
  });
});

describe("pendingText", () => {
  it("says what changes when the next trading day starts", () => {
    const limits = { dailyLoss: 1_500, dailyTarget: 1_000, maxTrades: 6 };

    expect(pendingText(limits, { dailyLoss: 2_000, dailyTarget: 1_000, maxTrades: null }, nextDayStart, timeZone)).toBe(
      "From 00:00: daily loss limit 2,000.00, trades a day off",
    );
    expect([pendingText(limits, null, nextDayStart, timeZone), pendingText(limits, limits, nextDayStart, timeZone)]).toEqual([null, null]);
  });
});

describe("parseLimitsForm", () => {
  const form = (changes = {}) => ({ ...limitsForm(noLimits), ...changes });

  it("reads amounts, a percent of the day's start and a count, and an empty field is off", () => {
    expect(parseLimitsForm(form({ dailyLoss: "1,500", dailyTarget: "1000.50", maxTrades: "6" }), 100_000, 5_000)).toEqual({
      ok: true,
      limits: { dailyLoss: 1_500, dailyTarget: 1_000.5, maxTrades: 6 },
    });
    expect(parseLimitsForm(form({ dailyLoss: "1.5", lossUnit: "percent" }), 100_000, null)).toEqual({ ok: true, limits: { ...noLimits, dailyLoss: 1_500 } });
    expect(parseLimitsForm(form(), 100_000, null)).toEqual({ ok: true, limits: noLimits });
  });

  it("refuses what the engine would, and a loss limit as large as the firm's", () => {
    expect(parseLimitsForm(form({ dailyLoss: "0" }), 100_000, null)).toMatchObject({ ok: false, field: "dailyLoss" });
    expect(parseLimitsForm(form({ dailyLoss: "120", lossUnit: "percent" }), 100_000, null)).toMatchObject({ ok: false, field: "dailyLoss" });
    expect(parseLimitsForm(form({ dailyLoss: "5000" }), 100_000, 5_000)).toEqual({
      ok: false,
      field: "dailyLoss",
      message: "Must be smaller than the firm's daily loss limit of 5,000.00.",
    });
    expect(parseLimitsForm(form({ dailyTarget: "10.001" }), 100_000, null)).toMatchObject({ ok: false, field: "dailyTarget" });
    expect(parseLimitsForm(form({ maxTrades: "1001" }), 100_000, null)).toMatchObject({ ok: false, field: "maxTrades" });
    expect(parseLimitsForm(form({ maxTrades: "2.5" }), 100_000, null)).toMatchObject({ ok: false, field: "maxTrades" });
  });

  it("starts from the limits as they are, and knows the firm's daily loss", () => {
    expect(limitsForm({ dailyLoss: 1_500, dailyTarget: null, maxTrades: 6 })).toEqual({ dailyLoss: "1500.00", lossUnit: "amount", dailyTarget: "", maxTrades: "6" });
    expect(firmDailyLoss([daily(5_000)])).toBe(5_000);
    expect(firmDailyLoss([])).toBeNull();
  });
});

describe("the events of the trader's own limits", () => {
  const digitsOf = () => 5;

  it("are told in plain words", () => {
    const events: EngineEvent[] = [
      { kind: "OwnLimitsSet", accountId: "A1", limits: { dailyLoss: 1_500, dailyTarget: null, maxTrades: 6 }, pending: { dailyLoss: 2_000, dailyTarget: null, maxTrades: 6 }, timestamp },
      { kind: "OwnLimitReached", accountId: "A1", limit: "DailyLoss", level: 98_500, equity: 98_495.2, timestamp },
      { kind: "TradingLocked", accountId: "A1", reason: "Trader", until: nextDayStart, limit: null, dayResult: 120, positionsClosed: 0, timestamp },
      { kind: "TradingUnlocked", accountId: "A1", timestamp },
      { kind: "TradingDaySet", accountId: "A1", day: { timeZone, start: "00:00:00" }, nextDayStart, timestamp },
      { kind: "TradingDayStarted", accountId: "A1", dayStartBalance: 98_495.2, limits: noLimits, nextDayStart, timestamp },
    ];

    expect(events.map((e) => describeEvent(e, digitsOf))).toEqual([
      "Your limits: daily loss 1,500.00, 6 trades a day. From the next trading day: daily loss 2,000.00, 6 trades a day",
      "Your own daily loss limit reached: equity 98,495.20 at 98,500.00",
      "New orders locked by you until the next trading day",
      "New orders taken again",
      "Trading days start at 00:00 Stockholm time",
      "New trading day from a balance of 98,495.20",
    ]);
    expect(events.map(isWarning)).toEqual([false, true, true, false, false, false]);
  });

  it("explain why an order was refused", () => {
    expect([rejectionText("AccountLocked"), rejectionText("TradeLimitReached")]).toEqual([
      "Refused: new orders are locked until the next trading day",
      "Refused: you have used your trades for the day",
    ]);
  });
});
