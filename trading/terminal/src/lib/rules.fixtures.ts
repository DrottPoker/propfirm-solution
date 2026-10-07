import type { AccountRules, AccountSnapshot, FloorSnapshot, OwnLimitsSnapshot } from "./api/types";

// An account, its daily loss limit and its rules, for the tests of the rulebook and its warnings.

export const daily = (headroom: number): FloorSnapshot => ({
  floorId: "daily",
  rule: { kind: "AnchoredFloor", distance: 5_000, anchor: "Balance" },
  level: 95_000,
  highWaterMark: 100_000,
  headroom,
});

export const accountWith = (changes: Partial<AccountSnapshot> = {}): AccountSnapshot => ({
  accountId: "A1",
  groupId: "standard",
  currency: "USD",
  status: "Active",
  balance: 102_000,
  equity: 102_000,
  usedMargin: 0,
  freeMargin: 102_000,
  marginLevelPercent: null,
  positions: [],
  orders: [],
  floors: [daily(5_000)],
  ownLimits: ownLimitsWith(),
  ...changes,
});

// Midnight in Stockholm, when the next trading day starts.
export const nextDayStart = "2026-10-05T22:00:00Z";

export const ownLimitsWith = (changes: Partial<OwnLimitsSnapshot> = {}): OwnLimitsSnapshot => ({
  limits: { dailyLoss: null, dailyTarget: null, maxTrades: null },
  pending: null,
  tradingDay: { timeZone: "Europe/Stockholm", start: "00:00:00" },
  dayStartBalance: 100_000,
  lossLevel: null,
  targetLevel: null,
  tradesToday: 0,
  nextDayStart,
  lock: null,
  ...changes,
});

export const rulesWith = (changes: Partial<AccountRules> = {}): AccountRules => ({
  funded: false,
  tradingDaysRequired: 4,
  tradingDaysCounted: 1,
  passBy: null,
  openPositionBy: "2026-11-05T23:00:00Z",
  consistencyPercent: null,
  bestDayPercent: null,
  ...changes,
});
