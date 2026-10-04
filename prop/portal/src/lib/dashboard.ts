import type { AccountDetails, ChallengeStatus, FloorFigure, StageSummary } from "./api/types";
import { daysBetween, dayBefore, formatDate, formatDateTime, formatMoney } from "./format";

// What the trader's dashboard shows, worked out from what Prop.Api sends. Money is only shown here, never
// calculated: ratios decide colors, and dates decide how many days are left.

/** The colors a figure can have. */
export type Tone = "neutral" | "profit" | "loss" | "warning";

/** A result is green above zero and red below it. */
export function resultTone(value: number | null | undefined): Tone {
  return value == null || value === 0 ? "neutral" : value > 0 ? "profit" : "loss";
}

/** The Tailwind text color of a tone. Kept in one place so a firm's own look can change it later. */
export const toneText: Record<Tone, string> = {
  neutral: "text-foreground",
  profit: "text-profit",
  loss: "text-loss",
  warning: "text-warning",
};

/** How close a loss limit is. As in the terminal: under 25 % of its distance left is a warning, under 10 % danger. */
export type FloorState = "ok" | "warning" | "danger";

export function floorState(floor: Pick<FloorFigure, "headroom" | "distance">): FloorState {
  if (floor.distance == null || floor.distance <= 0) {
    return "ok";
  }

  const left = floor.headroom / floor.distance;
  return left < 0.1 ? "danger" : left < 0.25 ? "warning" : "ok";
}

export const floorStateTone: Record<FloorState, Tone> = { ok: "neutral", warning: "warning", danger: "loss" };

/** The account's trading account is open for trading. */
export function isTrading(details: AccountDetails): boolean {
  return details.account.status === "Active" && details.account.tradingAccountId !== null;
}

export function hasEnded(status: ChallengeStatus): boolean {
  return status === "Failed" || status === "Cancelled";
}

/** The live loss limit with the id, while the account is valued. */
export function liveFloor(details: AccountDetails, floorId: string): FloorFigure | null {
  return details.live?.floors.find((f) => f.floorId === floorId) ?? null;
}

/** The trading day it is in the challenge's time zone, as a plain date, while the account trades. */
export function currentTradingDay(details: AccountDetails): string | null {
  const started = details.results.dayStartedAt;
  if (!started) {
    return null;
  }

  // A trading day is named by the local date it starts on, so the date where it started is its name.
  return new Intl.DateTimeFormat("en-CA", { timeZone: details.challenge.tradingDay.timeZone, year: "numeric", month: "2-digit", day: "2-digit" }).format(
    new Date(started),
  );
}

/** A day the challenge ends on unless the trader acts before: the last day to act, and the days left including today. */
export type Deadline = { lastDay: string; daysLeft: number | null };

export function deadlineOf(details: AccountDetails, endsOn: string | null): Deadline | null {
  if (!endsOn) {
    return null;
  }

  const today = currentTradingDay(details);
  return { lastDay: dayBefore(endsOn), daysLeft: today ? daysBetween(today, endsOn) : null };
}

export function daysLeftText(daysLeft: number): string {
  return daysLeft <= 0 ? "Ends today" : daysLeft === 1 ? "Last day" : `${daysLeft} days left`;
}

/** The two letters on the trader's menu button, from the email address. */
export function initials(email: string): string {
  const name = email.split("@")[0] ?? "";
  const parts = name.split(/[._+-]+/).filter((p) => p.length > 0);
  const letters = parts.length > 1 ? `${parts[0][0]}${parts[1][0]}` : name.slice(0, 2);
  return letters.toUpperCase() || "?";
}

/** The challenge's name with the account number, for example "Two-step 100000 USD · #1003". */
export function accountTitle(details: AccountDetails): string {
  return `${details.challenge.name} · #${details.account.number}`;
}

/** How a stage looks in the stepper: passed, the current one and how it stands, or still to come. */
export type StepState = "passed" | "current" | "waiting" | "failed" | "cancelled" | "upcoming";

export function stepState(stage: StageSummary, status: ChallengeStatus): StepState {
  if (stage.progress === "Passed") {
    return "passed";
  }

  if (stage.progress === "Upcoming") {
    return "upcoming";
  }

  switch (status) {
    case "Failed":
      return "failed";
    case "Cancelled":
      return "cancelled";
    case "AwaitingFunding":
      return "waiting";
    default:
      return "current";
  }
}

/** The badge for the account: where it is, or how it ended. */
export function statusText(details: AccountDetails): string {
  const { account } = details;
  switch (account.status) {
    case "OpeningAccount":
      return `${account.stageName} · Opening`;
    case "AwaitingFunding":
      return "Passed · Under review";
    case "Failed":
      return details.expiry ? "Ended" : "Failed";
    case "Cancelled":
      return "Cancelled";
    default:
      return account.paused ? `${account.stageName} · Paused` : account.stageName;
  }
}

/** Why an ended account ended, in a few words, for example "daily loss limit". */
export function endingText(details: AccountDetails): string {
  if (details.breach) {
    return details.breach.reason === "DailyLoss" ? "daily loss limit" : details.breach.reason === "MaxLoss" ? "max loss limit" : "a loss limit";
  }

  if (details.expiry) {
    return details.expiry.reason === "Inactivity" ? "no new trade in time" : "time limit";
  }

  return details.account.status === "Cancelled" ? "cancelled by the firm" : "";
}

/** Something on one of the trader's accounts that needs the trader's attention, most urgent first. */
export type AttentionItem = {
  key: string;
  tone: "danger" | "warning" | "profit" | "info";
  accountId: string;
  title: string;
  detail: string;
  action: string;
};

const urgency: Record<AttentionItem["tone"], number> = { danger: 0, warning: 1, profit: 2, info: 3 };

/** Deadlines closer than this many days are shown at the top of the dashboard. */
export const deadlineWarningDays = 5;

export function attentionItems(accounts: AccountDetails[]): AttentionItem[] {
  const items: AttentionItem[] = [];
  for (const details of accounts) {
    const { account } = details;
    const id = account.id;
    if (isTrading(details)) {
      for (const floor of details.live?.floors ?? []) {
        const state = floorState(floor);
        if (state === "ok") {
          continue;
        }

        const daily = floor.floorId === "daily";
        items.push({
          key: `${id}-floor-${floor.floorId}`,
          tone: state === "danger" ? "danger" : "warning",
          accountId: id,
          title: `#${account.number} is ${formatMoney(floor.headroom)} ${account.currency} from its ${daily ? "daily" : "max"} loss limit.`,
          detail:
            daily && details.results.nextDayStartsAt
              ? `Equity may not fall below ${formatMoney(floor.level)} today. The limit starts again ${formatDateTime(details.results.nextDayStartsAt)}.`
              : `Equity may not fall below ${formatMoney(floor.level)}.`,
          action: "View",
        });
      }

      const quote = account.nextPayout;
      if (quote?.canRequest) {
        items.push({
          key: `${id}-payout`,
          tone: "profit",
          accountId: id,
          title: `A payout of ${formatMoney(quote.amount)} ${account.currency} is ready on #${account.number}.`,
          detail: `${quote.tradingDays} trading days since your last payout.`,
          action: "Request payout",
        });
      }

      const inactivity = deadlineOf(details, account.inactivityDeadline);
      if (inactivity?.daysLeft != null && inactivity.daysLeft <= deadlineWarningDays) {
        items.push({
          key: `${id}-inactivity`,
          tone: "warning",
          accountId: id,
          title: `Open a trade on #${account.number} by ${formatDate(inactivity.lastDay)}.`,
          detail: `The challenge ends after ${details.challenge.inactivityDays} days without a new trade.`,
          action: "View",
        });
      }

      const timeLimit = deadlineOf(details, account.stageDeadline);
      if (timeLimit?.daysLeft != null && timeLimit.daysLeft <= deadlineWarningDays) {
        items.push({
          key: `${id}-time-limit`,
          tone: "warning",
          accountId: id,
          title: `Pass ${account.stageName} on #${account.number} by ${formatDate(timeLimit.lastDay)}.`,
          detail: "The stage ends if its target is not reached in time.",
          action: "View",
        });
      }

      if (account.paused) {
        items.push({
          key: `${id}-paused`,
          tone: "info",
          accountId: id,
          title: `#${account.number} is paused by the firm.`,
          detail: "You can close positions but not open new ones until it goes on. The days do not count meanwhile.",
          action: "View",
        });
      }
    }

    if (account.status === "AwaitingFunding") {
      items.push({
        key: `${id}-passed`,
        tone: "profit",
        accountId: id,
        title: `#${account.number} passed every stage.`,
        detail: "The firm is reviewing your funded account.",
        action: "View",
      });
    }
  }

  return items.sort((a, b) => urgency[a.tone] - urgency[b.tone]);
}

/** A rule the current stage must keep or reach, and how it stands. */
export type Objective = {
  key: string;
  title: string;
  state: "reached" | "progress" | "kept" | "warning" | "danger" | "broken" | "info";
  stateText: string;
  detail: string;
  progress?: number;
  segments?: { filled: number; total: number };
};

export function objectivesOf(details: AccountDetails): Objective[] {
  const { account, results, breach } = details;
  const rules = account.stage < details.challenge.evaluation.length ? details.challenge.evaluation[account.stage] : details.challenge.funded;
  const ended = hasEnded(account.status);
  const objectives: Objective[] = [];

  if (results.targetRequired != null && results.targetGained != null && results.targetPercent != null) {
    const reached = results.targetPercent >= 100;
    objectives.push({
      key: "target",
      title: "Profit target",
      state: reached ? "reached" : ended ? "info" : "progress",
      stateText: reached ? "Reached" : ended ? "Not reached" : "In progress",
      detail: `${formatMoney(results.targetGained)} of ${formatMoney(results.targetRequired)} · reach a balance of ${formatMoney(account.profitTarget)}`,
      progress: results.targetPercent,
    });
  }

  objectives.push(lossLimit(details, "daily", "Daily loss limit"));
  objectives.push(lossLimit(details, "max-loss", rules.maxLoss.kind === "Trailing" ? "Max loss limit (trailing)" : "Max loss limit"));

  if (account.funded) {
    const quote = account.nextPayout;
    if (quote && quote.minTradingDays > 0) {
      objectives.push({
        key: "days",
        title: "Trading days for a payout",
        state: quote.tradingDays >= quote.minTradingDays ? "reached" : ended ? "info" : "progress",
        stateText: `${quote.tradingDays} of ${quote.minTradingDays}`,
        detail: "A day counts when you open a trade on it. They count from zero again after each payout.",
        segments: { filled: Math.min(quote.tradingDays, quote.minTradingDays), total: quote.minTradingDays },
      });
    }
  } else if (account.minTradingDays > 0) {
    objectives.push({
      key: "days",
      title: "Minimum trading days",
      state: account.tradingDays >= account.minTradingDays ? "reached" : ended ? "info" : "progress",
      stateText: `${account.tradingDays} of ${account.minTradingDays}`,
      detail: "A day counts when you open a trade on it.",
      segments: { filled: Math.min(account.tradingDays, account.minTradingDays), total: account.minTradingDays },
    });
  }

  const timeLimit = deadlineOf(details, account.stageDeadline);
  if (timeLimit) {
    objectives.push(deadline("time-limit", "Time limit", timeLimit, `Pass ${account.stageName} by ${formatDate(timeLimit.lastDay)}.`));
  } else if (details.expiry?.reason === "TimeLimit") {
    objectives.push({ key: "time-limit", title: "Time limit", state: "broken", stateText: "Ran out", detail: `The stage was not passed in time.` });
  }

  const inactivity = deadlineOf(details, account.inactivityDeadline);
  if (inactivity) {
    objectives.push(deadline("activity", "Stay active", inactivity, `Open a new trade by ${formatDate(inactivity.lastDay)}.`));
  } else if (details.expiry?.reason === "Inactivity") {
    objectives.push({ key: "activity", title: "Stay active", state: "broken", stateText: "Ran out", detail: "No new trade was opened for too long." });
  }

  // A breach that failed the account on a floor the rule engine did not set is shown too.
  if (breach && breach.reason === "OtherFloor") {
    objectives.push({
      key: "other",
      title: "Loss limit",
      state: "broken",
      stateText: "Broken",
      detail: `Equity ${formatMoney(breach.equity)} fell below ${formatMoney(breach.level)} on ${formatDateTime(breach.time)}.`,
    });
  }

  return objectives;
}

function lossLimit(details: AccountDetails, floorId: string, title: string): Objective {
  const { breach } = details;
  if (breach && breach.floorId === floorId) {
    return {
      key: floorId,
      title,
      state: "broken",
      stateText: "Broken",
      detail: `Equity ${formatMoney(breach.equity)} fell below ${formatMoney(breach.level)} on ${formatDateTime(breach.time)}.`,
    };
  }

  const floor = liveFloor(details, floorId);
  if (floor) {
    const state = floorState(floor);
    const reset = floorId === "daily" && details.results.nextDayStartsAt ? ` It starts again ${formatDateTime(details.results.nextDayStartsAt)}.` : "";
    return {
      key: floorId,
      title,
      state: state === "ok" ? "kept" : state,
      stateText: state === "ok" ? "Kept" : "Close",
      detail: `Equity may fall ${formatMoney(floor.headroom)} more${floorId === "daily" ? " today" : ""}, to ${formatMoney(floor.level)}.${reset}`,
    };
  }

  const level = floorId === "daily" ? details.account.dailyFloor : details.account.maxLossFloor;
  return {
    key: floorId,
    title,
    state: hasEnded(details.account.status) ? "kept" : "info",
    stateText: hasEnded(details.account.status) ? "Kept" : "",
    detail: level == null ? "Set when the stage's trading account opens." : `Equity may not fall below ${formatMoney(level)}.`,
  };
}

function deadline(key: string, title: string, due: Deadline, detail: string): Objective {
  const close = due.daysLeft != null && due.daysLeft <= deadlineWarningDays;
  return {
    key,
    title,
    state: close ? "warning" : "info",
    stateText: due.daysLeft == null ? "" : daysLeftText(due.daysLeft),
    detail,
  };
}

/** The share of a stage's profit split, for example for the funded stage in the stepper. */
export function profitSplitText(details: AccountDetails): string | null {
  const split = details.challenge.funded.profitSplitPercent;
  return split == null ? null : `${split}% of the profit is yours`;
}
