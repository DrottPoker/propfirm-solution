import { floorLabel, floorLeftText, floorRisk, targetText } from "./account";
import type { AccountRules, AccountSnapshot, FloorSnapshot } from "./api/types";
import { formatDayTime, formatMoney, timeZoneName } from "./format";

// The account's rulebook, as the terminal shows it (ADR 0052): the profit target and loss limits from the account,
// and the trading days, deadlines and consistency rule as the firm's system last told them.

/** How a rule stands: fine, close to being broken, broken or about to be, or met. */
export type RuleState = "ok" | "warning" | "danger" | "done";

export interface RuleRow {
  id: string;
  label: string;
  value: string;
  note?: string;
  state: RuleState;
  help: string;
}

const minuteMs = 60_000;
const hourMs = 60 * minuteMs;
const dayMs = 24 * hourMs;

/** A deadline closer than this is a warning, and closer than a day an urgent one. */
export const deadlineWarning = { warning: 3 * dayMs, danger: dayMs } as const;

/** How urgent a deadline is. One that has passed is the firm's system's to act on, and counts as urgent. */
export function deadlineState(deadline: string, now: number): RuleState {
  const left = Date.parse(deadline) - now;
  return left < deadlineWarning.danger ? "danger" : left < deadlineWarning.warning ? "warning" : "ok";
}

/** The time left, rounded down: "3 days left", "30 h left", "12 min left" or "Passed". */
export function timeLeftText(deadline: string, now: number): string {
  const left = Date.parse(deadline) - now;
  if (left <= 0) {
    return "Passed";
  }

  if (left >= 2 * dayMs) {
    return `${Math.floor(left / dayMs)} days left`;
  }

  return left >= hourMs ? `${Math.floor(left / hourMs)} h left` : `${Math.max(1, Math.floor(left / minuteMs))} min left`;
}

/** A moment in the account's time zone, named, for example "Mon 19 Oct 00:00 Stockholm time". */
export function deadlineText(deadline: string, timeZone: string): string {
  return `${formatDayTime(deadline, timeZone)} ${timeZoneName(timeZone)}`;
}

/** A share in percent with at most one decimal, for example 60% or 37.5%. */
export function percentText(percent: number): string {
  return `${Number(percent.toFixed(1))}%`;
}

export interface RulebookInput {
  account: AccountSnapshot;
  /** The balance that passes the stage, from the account's details. Null on a funded account. */
  profitTarget: number | null;
  /** Null until loaded, or when they could not be. */
  rules: AccountRules | null;
  now: number;
  timeZone: string;
}

/** Every rule of the account and where it stands, in the order the trader meets them. Times are in the time zone. */
export function rulebook({ account, profitTarget, rules, now, timeZone }: RulebookInput): RuleRow[] {
  const rows: RuleRow[] = [];
  const ended = account.status === "Disabled";
  // A firm's system that tells the rules always counts the trading days, also when none is required.
  const told = rules !== null && rules.tradingDaysCounted !== null;
  const funded = rules?.funded ?? false;

  if (profitTarget !== null && !ended) {
    rows.push({
      id: "target",
      label: "Profit target",
      value: formatMoney(profitTarget),
      note: targetText(profitTarget, account),
      state: account.balance >= profitTarget ? "done" : "ok",
      help: "Reach this balance, with every position closed, to pass the stage.",
    });
  }

  for (const floor of account.floors) {
    rows.push({
      id: `floor-${floor.floorId}`,
      label: floorLabel(floor.floorId),
      value: formatMoney(floor.level),
      note: floorLeftText(floor),
      // Once trading has ended, only a broken limit still says so.
      state: ended && floor.headroom > 0 ? "ok" : floorRisk(floor),
      help: floorHelp(floor),
    });
  }

  if (rules?.tradingDaysRequired != null) {
    const counted = rules.tradingDaysCounted ?? 0;
    rows.push({
      id: "trading-days",
      label: funded ? "Trading days for a payout" : "Trading days",
      value: `${counted} of ${rules.tradingDaysRequired}`,
      state: counted >= rules.tradingDaysRequired ? "done" : "ok",
      help: funded
        ? "Days with a new position since the last payout. A payout needs at least this many."
        : "Days with a new position. The stage passes only once there are at least this many.",
    });
  }

  if (rules?.passBy) {
    rows.push({
      id: "pass-by",
      label: "Pass by",
      value: formatDayTime(rules.passBy, timeZone),
      note: timeLeftText(rules.passBy, now),
      state: deadlineState(rules.passBy, now),
      help: "The stage fails unless the profit target is reached before then.",
    });
  } else if (told && !funded && profitTarget !== null && !ended) {
    rows.push({ id: "pass-by", label: "Time limit", value: "None", state: "ok", help: "The stage can take as long as it needs." });
  }

  if (rules?.openPositionBy) {
    rows.push({
      id: "open-by",
      label: "Open a position by",
      value: formatDayTime(rules.openPositionBy, timeZone),
      note: timeLeftText(rules.openPositionBy, now),
      state: deadlineState(rules.openPositionBy, now),
      help: "The challenge ends unless a new position is opened before then. Every new position moves this further on.",
    });
  }

  if (rules?.consistencyPercent != null) {
    const best = rules.bestDayPercent;
    rows.push({
      id: "consistency",
      label: "Best day's share",
      value: best === null ? "-" : percentText(best),
      note: `At most ${percentText(rules.consistencyPercent)}`,
      state: best !== null && best > rules.consistencyPercent ? "warning" : "ok",
      help: `The consistency rule: the best trading day may make at most ${percentText(rules.consistencyPercent)} of the profit since the last payout. A payout waits until it does.`,
    });
  }

  return rows;
}

/** The most urgent state among the rules, which the rulebook's button shows. Null when nothing needs attention. */
export function attentionOf(rows: readonly RuleRow[]): "warning" | "danger" | null {
  return rows.some((r) => r.state === "danger") ? "danger" : rows.some((r) => r.state === "warning") ? "warning" : null;
}

function floorHelp(floor: FloorSnapshot): string {
  const broken = "If equity falls below it, every position is closed and trading on the account ends.";
  if (floor.rule.kind === "TrailingFloor") {
    const lock = floor.rule.lockLevel != null ? ` until it reaches ${formatMoney(floor.rule.lockLevel)}` : "";
    return `Equity must stay above this level. It rises with the highest equity${lock}, and never falls. ${broken}`;
  }

  return floor.floorId === "daily"
    ? `Equity must stay above this level today. A new level is set when the next trading day starts. ${broken}`
    : `Equity must stay above this level. ${broken}`;
}
