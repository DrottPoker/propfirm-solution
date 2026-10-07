import { floorLabel } from "./account";
import type { FloorSnapshot } from "./api/types";
import { formatMoney } from "./format";
import { clockText, lockDescription, lockTitle, tradesUsed } from "./ownLimits";
import { deadlineText, deadlineWarning, percentText, timeLeftText, type RulebookInput } from "./rules";

// Warnings about the account's rules while the terminal is open (ADR 0052): a loss limit coming close or broken,
// the profit target reached, a deadline coming close and a best day above the consistency rule. Each is told once.
// So are the trader's own limits (ADR 0054): their daily loss limit coming close, a locked day and the day's trades used.

export type WarningLevel = "warning" | "danger" | "success";

export interface RuleWarning {
  /** The rule it is about, so a newer note about the rule replaces the one before. */
  id: string;
  /** The condition, which is told once while it holds. */
  key: string;
  level: WarningLevel;
  title: string;
  description: string;
}

/** What has been told: how close each loss limit is, and which other conditions hold. */
export interface WarningMemory {
  floors: Record<string, number>;
  told: readonly string[];
}

// A loss limit is close with under 25% of its room left and very close under 10%, as the account bar colors it.
// It counts as further away again only with 10 points more, so equity moving around a line warns once.
const enterBelow = [Infinity, 0.25, 0.1];
const leaveAbove = [Infinity, 0.35, 0.2];
const broken = 3;

/** How close equity is to the floor: 0 not close, 1 close, 2 very close, 3 broken. Floors at a fixed level are never close. */
export function floorStage(floor: FloorSnapshot, previous: number): number {
  return closenessStage(floor.headroom, "distance" in floor.rule ? floor.rule.distance : null, previous);
}

/** How close a limit with the room left of the whole distance is, as for a floor. Without a distance it is never close. */
export function closenessStage(headroom: number, distance: number | null, previous: number): number {
  if (headroom <= 0) {
    return broken;
  }

  if (!distance || distance <= 0) {
    return 0;
  }

  const left = headroom / distance;
  let stage = Math.min(previous, 2);
  while (stage < 2 && left < enterBelow[stage + 1]) {
    stage++;
  }

  while (stage > 0 && left >= leaveAbove[stage]) {
    stage--;
  }

  return stage;
}

/**
 * The warnings to tell now, and what has been told after them. The first time, with no memory, the conditions that
 * already hold are told, but not how close the loss limits are, which the account bar shows.
 */
export function nextWarnings(memory: WarningMemory | null, input: RulebookInput): { memory: WarningMemory; warnings: RuleWarning[] } {
  const { account } = input;
  const warnings: RuleWarning[] = [];
  const floors: Record<string, number> = {};
  for (const floor of account.floors) {
    const before = memory?.floors[floor.floorId] ?? 0;
    const stage = floorStage(floor, before);
    floors[floor.floorId] = stage;
    if (memory && stage > before) {
      warnings.push(floorWarning(floor, stage, account.currency));
    }
  }

  // The trader's own daily loss limit, until it locks the day, which is told on its own.
  const own = account.ownLimits;
  if (own.lossLevel !== null && own.lock === null && account.status !== "Disabled") {
    const before = memory?.floors[ownLoss] ?? 0;
    const stage = Math.min(2, closenessStage(account.equity - own.lossLevel, own.limits.dailyLoss, before));
    floors[ownLoss] = stage;
    if (memory && stage > before) {
      warnings.push(ownLossWarning(account.equity - own.lossLevel, own.lossLevel, stage, account.currency));
    }
  }

  const conditions = account.status === "Disabled" ? [] : conditionsOf(input);
  for (const condition of conditions) {
    if (!memory?.told.includes(condition.key)) {
      warnings.push(condition);
    }
  }

  return { memory: { floors, told: conditions.map((c) => c.key) }, warnings };
}

function floorWarning(floor: FloorSnapshot, stage: number, currency: string): RuleWarning {
  const name = floorLabel(floor.floorId).toLowerCase();
  const id = `floor-${floor.floorId}`;
  if (stage === broken) {
    return {
      id,
      key: `${id}-broken`,
      level: "danger",
      title: `The ${name} was broken`,
      description: "Every position was closed, and trading on the account has ended.",
    };
  }

  return {
    id,
    key: `${id}-${stage}`,
    level: stage === 2 ? "danger" : "warning",
    title: `${stage === 2 ? "Very close" : "Close"} to the ${name}`,
    description: `${formatMoney(floor.headroom)} ${currency} left before equity reaches ${formatMoney(floor.level)}.`,
  };
}

const ownLoss = "own-loss";

function ownLossWarning(left: number, level: number, stage: number, currency: string): RuleWarning {
  return {
    id: ownLoss,
    key: `${ownLoss}-${stage}`,
    level: stage === 2 ? "danger" : "warning",
    title: `${stage === 2 ? "Very close" : "Close"} to your own daily loss limit`,
    description: `${formatMoney(left)} ${currency} left before equity reaches ${formatMoney(level)}, and your positions close.`,
  };
}

function conditionsOf({ account, profitTarget, rules, now, timeZone }: RulebookInput): RuleWarning[] {
  const conditions: RuleWarning[] = [];
  // A lock the trader chose needs no warning: the note under the account bar says it.
  const own = account.ownLimits;
  if (own.lock && own.lock.reason !== "Trader") {
    conditions.push({
      id: "own-lock",
      key: `own-lock-${own.lock.until}`,
      level: own.lock.reason === "DailyTarget" ? "success" : "warning",
      title: lockTitle(own.lock, null, own.tradingDay.timeZone),
      description: lockDescription(own.lock, null, own.tradingDay.timeZone),
    });
  } else if (!own.lock && tradesUsed(own)) {
    conditions.push({
      id: "own-trades",
      key: `own-trades-${own.nextDayStart}`,
      level: "warning",
      title: "You have used your trades for today",
      description: `New orders are taken again from ${clockText(own.nextDayStart, own.tradingDay.timeZone)}.`,
    });
  }

  if (profitTarget !== null && account.balance >= profitTarget) {
    conditions.push({
      id: "target",
      key: `target-${profitTarget}`,
      level: "success",
      title: "Profit target reached",
      description:
        account.positions.length > 0
          ? "The stage passes once every position is closed with the balance still at the target."
          : "Every position is closed, so the stage passes.",
    });
  }

  const deadlines = [
    { id: "pass-by", deadline: rules?.passBy, title: "Pass the stage by", after: "After that the stage fails." },
    { id: "open-by", deadline: rules?.openPositionBy, title: "Open a position by", after: "After that the challenge ends, since nothing was traded." },
  ];
  for (const { id, deadline, title, after } of deadlines) {
    const left = deadline ? Date.parse(deadline) - now : 0;
    if (!deadline || left <= 0 || left >= deadlineWarning.warning) {
      continue;
    }

    // Told again when less than a day is left.
    const urgent = left < deadlineWarning.danger;
    conditions.push({
      id,
      key: `${id}-${deadline}-${urgent ? "day" : "days"}`,
      level: urgent ? "danger" : "warning",
      title: `${title} ${deadlineText(deadline, timeZone)}`,
      description: `${timeLeftText(deadline, now)}. ${after}`,
    });
  }

  const best = rules?.bestDayPercent;
  const allowed = rules?.consistencyPercent;
  if (best != null && allowed != null && best > allowed) {
    conditions.push({
      id: "consistency",
      key: `consistency-${allowed}`,
      level: "warning",
      title: "Best day above the consistency rule",
      description: `Your best day made ${percentText(best)} of the profit, and the rule allows ${percentText(allowed)}. A payout waits until other days make more.`,
    });
  }

  return conditions;
}
