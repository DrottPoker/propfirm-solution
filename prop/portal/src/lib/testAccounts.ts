import type { Account, AccountDetails, ChallengeDefinition, StageRules } from "./api/types";

// Accounts for the unit tests, like the development firm's two-step challenge.

function stage(name: string, profitTargetPercent: number | null, minTradingDays: number, profitSplitPercent: number | null = null): StageRules {
  return {
    name,
    profitTargetPercent,
    minTradingDays,
    dailyLoss: { percent: 5, reference: "Balance" },
    maxLoss: { percent: 10, kind: "Fixed" },
    profitSplitPercent,
    maxDays: null,
    consistencyPercent: null,
  };
}

export const testChallenge: ChallengeDefinition = {
  id: "two-step-100k",
  name: "Two-step 100K",
  currency: "USD",
  initialBalance: 100_000,
  tradingDay: { timeZone: "Europe/Stockholm", start: "00:00:00" },
  evaluation: [stage("Phase 1", 10, 4), stage("Phase 2", 5, 4)],
  funded: stage("Funded", null, 5, 80),
  inactivityDays: 30,
};

export const testAccount: Account = {
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
  paused: false,
  stageDeadline: null,
  inactivityDeadline: "2026-11-05",
};

/** A trading Phase 1 account valued at 102 800 on 6 October, with the changes given. */
export function testDetails(changes: Partial<AccountDetails> = {}): AccountDetails {
  return {
    account: testAccount,
    challenge: testChallenge,
    live: {
      balance: 102_500,
      equity: 102_800,
      floors: [
        { floorId: "daily", level: 97_000, headroom: 5_800, distance: 5_000 },
        { floorId: "max-loss", level: 90_000, headroom: 12_800, distance: 10_000 },
      ],
    },
    stages: [
      { stage: 0, name: "Phase 1", progress: "Current", tradingAccountId: "demo-firm-1001-1", startedAt: "2026-10-05T08:00:00Z", passedAt: null, result: null, tradingDays: 2, profitTarget: 10_000, dailyLoss: 5_000, maxLoss: 10_000 },
      { stage: 1, name: "Phase 2", progress: "Upcoming", tradingAccountId: null, startedAt: null, passedAt: null, result: null, tradingDays: null, profitTarget: 5_000, dailyLoss: 5_000, maxLoss: 10_000 },
      { stage: 2, name: "Funded", progress: "Upcoming", tradingAccountId: null, startedAt: null, passedAt: null, result: null, tradingDays: null, profitTarget: null, dailyLoss: 5_000, maxLoss: 10_000 },
    ],
    results: {
      balance: 102_500,
      equity: 102_800,
      floating: 300,
      stageResult: 2_800,
      stageResultPercent: 2.8,
      today: 800,
      dayStartBalance: 102_000,
      dayStartedAt: "2026-10-05T22:00:00Z",
      nextDayStartsAt: "2026-10-06T22:00:00Z",
      targetGained: 2_500,
      targetRequired: 10_000,
      targetPercent: 25,
      paidOut: 0,
    },
    breach: null,
    expiry: null,
    endedAt: null,
    payouts: [],
    historyVersion: "7.4",
    ...changes,
  };
}
