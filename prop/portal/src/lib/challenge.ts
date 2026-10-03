import type { Account, AccountDetails, ChallengeStatus, FailureReason } from "./api/types";

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
