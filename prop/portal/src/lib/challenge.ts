import type { Account, AccountDetails, ChallengeStatus, ExpiryReason, FailureReason } from "./api/types";
import { dayBefore, formatDate } from "./format";

export const statusLabels: Record<ChallengeStatus, string> = {
  OpeningAccount: "Opening account",
  Active: "Active",
  AwaitingFunding: "Passed",
  Failed: "Failed",
  Cancelled: "Cancelled",
};

export const failureLabels: Record<FailureReason, string> = {
  DailyLoss: "daily loss limit",
  MaxLoss: "max loss limit",
  OtherFloor: "a loss limit",
};

/** Why a challenge ran out of time, to finish "Ended on 5 Nov 2026: ...". */
export const expiryLabels: Record<ExpiryReason, string> = {
  TimeLimit: "the stage was not passed within its time limit.",
  Inactivity: "no new trade was opened for too long.",
};

const floorLabels: Record<string, string> = {
  daily: "Daily loss limit",
  "max-loss": "Max loss limit",
};

export function floorLabel(floorId: string): string {
  return floorLabels[floorId] ?? `Floor ${floorId}`;
}

/** Whether the trader can trade the account in the terminal right now. */
export function canOpenTerminal(account: Account): boolean {
  return account.status === "Active" && account.tradingAccountId !== null;
}

/**
 * The account to open the terminal on for a trader the terminal sent to log in: the one it was on while that still
 * trades, otherwise the newest that does. Null when none does.
 */
export function terminalAccountOf(accounts: Account[], tradingAccountId: string | null): Account | null {
  const trading = accounts.filter(canOpenTerminal);
  return trading.find((a) => a.tradingAccountId === tradingAccountId) ?? trading[0] ?? null;
}

/**
 * The days the trader must keep to, for example "Open a new trade by 4 Nov 2026." Each deadline is the trading day
 * the challenge ends on, so the last day to act is the one before it.
 */
export function deadlinesOf(account: Account): string[] {
  const deadlines: string[] = [];
  if (account.stageDeadline) {
    deadlines.push(`Pass ${account.stageName} by ${formatDate(dayBefore(account.stageDeadline))}.`);
  }

  if (account.inactivityDeadline) {
    deadlines.push(`Open a new trade by ${formatDate(dayBefore(account.inactivityDeadline))}.`);
  }

  return deadlines;
}

/** Whether the firm can still cancel the account. */
export function canCancel(account: Account): boolean {
  return account.status !== "Failed" && account.status !== "Cancelled";
}

/**
 * How far the balance has come from the stage's start towards its profit target, from 0 to 100. Null for a
 * stage without a target, such as the funded one.
 */
export function targetProgress(account: Account, balance: number | null): number | null {
  if (account.profitTarget === null) {
    return null;
  }

  const span = account.profitTarget - account.initialBalance;
  if (span <= 0) {
    return 100;
  }

  const gained = (balance ?? account.initialBalance) - account.initialBalance;
  return Math.min(100, Math.max(0, (gained / span) * 100));
}

export type FloorRow = { floorId: string; level: number; headroom: number | null };

/**
 * The loss limits to show. Valued at the latest prices when the trading platform answered; otherwise the
 * levels it last reported, without headroom, since the portal does not value accounts itself.
 */
export function floorsOf(details: AccountDetails): FloorRow[] {
  if (details.live) {
    return details.live.floors.map((f) => ({ floorId: f.floorId, level: f.level, headroom: f.headroom }));
  }

  const rows: FloorRow[] = [];
  if (details.account.dailyFloor !== null) {
    rows.push({ floorId: "daily", level: details.account.dailyFloor, headroom: null });
  }

  if (details.account.maxLossFloor !== null) {
    rows.push({ floorId: "max-loss", level: details.account.maxLossFloor, headroom: null });
  }

  return rows;
}

/** The kind of a recorded input or output, such as "FloorBreached". */
export function kindOf(value: unknown): string {
  return typeof value === "object" && value !== null && "kind" in value && typeof value.kind === "string" ? value.kind : "Unknown";
}

/** The kinds of a step's outputs, in order. */
export function kindsOf(values: unknown): string[] {
  return Array.isArray(values) ? values.map(kindOf) : [];
}
