import { describe, expect, it } from "vitest";

import type { Account, AccountDetails } from "./api/types";
import { canCancel, canOpenTerminal, floorLabel, floorsOf, kindOf, kindsOf, targetProgress } from "./challenge";

const account: Account = {
  id: "0199a000-0000-7000-8000-000000000001",
  number: 1001,
  email: "anna@test.example",
  challengeId: "two-step-100k",
  reference: null,
  status: "Active",
  stage: 0,
  stageName: "Phase 1",
  funded: false,
  tradingAccountId: "demo-firm-1001-1",
  tradingDays: 2,
  minTradingDays: 4,
  initialBalance: 100_000,
  currency: "USD",
  profitTarget: 110_000,
  balance: 102_500,
  openPositions: 1,
  dailyFloor: 97_000,
  maxLossFloor: 90_000,
  createdAt: "2026-10-05T08:00:00Z",
  nextPayout: null,
};

describe("targetProgress", () => {
  it("is the share of the way from the start to the target", () => {
    expect(targetProgress(account, 102_500)).toBe(25);
  });

  it("stays between 0 and 100", () => {
    expect(targetProgress(account, 95_000)).toBe(0);
    expect(targetProgress(account, 115_000)).toBe(100);
  });

  it("starts at 0 before the platform has reported a balance", () => {
    expect(targetProgress(account, null)).toBe(0);
  });

  it("is null for a stage without a target", () => {
    expect(targetProgress({ ...account, profitTarget: null, funded: true }, 101_000)).toBeNull();
  });
});

describe("floorsOf", () => {
  it("shows the live valuation when there is one", () => {
    const details: AccountDetails = {
      account,
      live: { balance: 102_500, equity: 101_000, floors: [{ floorId: "daily", level: 97_000, headroom: 4_000 }] },
      breach: null,
      payouts: [],
    };

    expect(floorsOf(details)).toEqual([{ floorId: "daily", level: 97_000, headroom: 4_000 }]);
  });

  it("falls back to the last reported levels, without headroom", () => {
    expect(floorsOf({ account, live: null, breach: null, payouts: [] })).toEqual([
      { floorId: "daily", level: 97_000, headroom: null },
      { floorId: "max-loss", level: 90_000, headroom: null },
    ]);
  });
});

describe("actions", () => {
  it("opens the terminal only for an active account with a trading account", () => {
    expect(canOpenTerminal(account)).toBe(true);
    expect(canOpenTerminal({ ...account, status: "AwaitingFunding" })).toBe(false);
    expect(canOpenTerminal({ ...account, tradingAccountId: null })).toBe(false);
  });

  it("cancels anything that has not already ended", () => {
    expect(canCancel(account)).toBe(true);
    expect(canCancel({ ...account, status: "AwaitingFunding" })).toBe(true);
    expect(canCancel({ ...account, status: "Failed" })).toBe(false);
    expect(canCancel({ ...account, status: "Cancelled" })).toBe(false);
  });
});

describe("floorLabel", () => {
  it("names the rule engine's floors and shows others by id", () => {
    expect(floorLabel("daily")).toBe("Daily loss limit");
    expect(floorLabel("max-loss")).toBe("Max loss limit");
    expect(floorLabel("custom")).toBe("Floor custom");
  });
});

describe("kindOf", () => {
  it("reads the kind of recorded inputs and outputs", () => {
    expect(kindOf({ kind: "FloorBreached", floorId: "daily" })).toBe("FloorBreached");
    expect(kindsOf([{ kind: "ChallengeFailed" }, { kind: "CloseAccountRequested" }])).toEqual(["ChallengeFailed", "CloseAccountRequested"]);
  });

  it("does not fail on anything else", () => {
    expect(kindOf(null)).toBe("Unknown");
    expect(kindOf({ kind: 3 })).toBe("Unknown");
    expect(kindsOf({})).toEqual([]);
  });
});
