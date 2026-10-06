import type { AccountDetails, ChallengeStatus, FloorFigure, StageSummary } from "./api/types";
import { daysBetween, dayBefore, formatDate, formatDateTime, formatMoney, formatPrice, formatShortDate } from "./format";

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

/**
 * A deadline as the trader acts on it, for example "Trade by 4 Nov (30 days)": the last day and the days from today until
 * it, which is what the rule counts. The day the challenge would end is the day after.
 */
export function deadlineText(verb: string, due: Deadline): string {
  if (due.daysLeft === null) {
    return `${verb} by ${formatShortDate(due.lastDay)}`;
  }

  if (due.daysLeft <= 0) {
    return "Ends today";
  }

  return due.daysLeft === 1 ? `${verb} today` : `${verb} by ${formatShortDate(due.lastDay)} (${due.daysLeft - 1} ${due.daysLeft === 2 ? "day" : "days"})`;
}

/** The two letters on a person's menu button: from the name when there is one, else from the email address. */
export function initials(email: string, name?: string | null): string {
  // A name gives its first and last word's first letters, as "Maja Lind" gives "ML".
  const words = (name ?? "").trim().split(/\s+/).filter((w) => w.length > 0);
  if (words.length > 1) {
    return `${Array.from(words[0])[0]}${Array.from(words[words.length - 1])[0]}`.toUpperCase();
  }

  if (words.length === 1) {
    return Array.from(words[0]).slice(0, 2).join("").toUpperCase();
  }

  const local = email.split("@")[0] ?? "";
  const parts = local.split(/[._+-]+/).filter((p) => p.length > 0);
  const letters = parts.length > 1 ? `${parts[0][0]}${parts[1][0]}` : local.slice(0, 2);
  return letters.toUpperCase() || "?";
}

/** The challenge's name with the account number, for example "Two-step 100K · #1003". */
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

/**
 * Why the balance of a breached account ended where it did, often below the limit: every open position was closed at
 * the next price, and the commission for closing them was charged. Null when the breach closed nothing.
 */
export function breachClosesText(details: AccountDetails): string | null {
  const closes = details.breach?.closes ?? [];
  if (closes.length === 0) {
    return null;
  }

  const prices = closes.map((c) => `${c.symbol} at ${formatPrice(c.closePrice)}`).join(", ");
  const commission = closes.reduce((sum, c) => sum + c.commission, 0);
  const charged = commission > 0 ? `, and ${formatMoney(commission)} in commission was charged for closing ${closes.length === 1 ? "it" : "them"}` : "";
  const balance = details.breach?.balanceAfter;
  return `Then every open position was closed at the next price (${prices})${charged}${balance == null ? "" : `, so the balance ended at ${formatMoney(balance)}`}.`;
}

/** Where a failed challenge is bought again, with the firm's code for retries when it has one, and what it costs. */
export function retryOf(details: AccountDetails): { href: string; label: string } | null {
  const retry = details.retry;
  if (!retry) {
    return null;
  }

  const params = new URLSearchParams({ challenge: retry.challengeId });
  if (retry.discountCode) {
    params.set("code", retry.discountCode);
  }

  const price = retry.amount ?? retry.price;
  return {
    href: `/buy?${params.toString()}`,
    label: retry.discountCode && retry.amount != null ? `Try again for ${formatMoney(price)} ${retry.currency} instead of ${formatMoney(retry.price)}` : `Try again for ${formatMoney(price)} ${retry.currency}`,
  };
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
              ? `Equity may not fall below ${formatMoney(floor.level)} today. The limit starts again ${formatDateTime(details.results.nextDayStartsAt, details.challenge.tradingDay.timeZone)}.`
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
  /** Where the progress would be with the open trades closed now, drawn lighter behind it. */
  ghost?: number;
  segments?: { filled: number; total: number };
};

export function objectivesOf(details: AccountDetails): Objective[] {
  const { account, results, breach } = details;
  const rules = account.stage < details.challenge.evaluation.length ? details.challenge.evaluation[account.stage] : details.challenge.funded;
  const ended = hasEnded(account.status);
  const objectives: Objective[] = [];

  if (results.targetRequired != null && results.targetGained != null && results.targetPercent != null) {
    const reached = results.targetPercent >= 100;
    // The target counts the balance, so closed trades only. Open ones are shown as where it would be with them closed.
    const withOpen = isTrading(details) && results.floating ? results.targetGained + results.floating : null;
    objectives.push(
      ended && !reached
        ? {
            key: "target",
            title: "Profit target",
            state: "info",
            stateText: "Not reached",
            detail: `The stage ended before the balance reached ${formatMoney(account.profitTarget)}.`,
          }
        : {
            key: "target",
            title: "Profit target",
            state: reached ? "reached" : "progress",
            stateText: reached ? "Reached" : "In progress",
            detail:
              `${formatMoney(results.targetGained)} of ${formatMoney(results.targetRequired)} · reach a balance of ${formatMoney(account.profitTarget)}. Closed trades count` +
              (withOpen === null || reached ? "." : `: with your open ones closed now, it would be ${formatMoney(withOpen)}.`),
            progress: results.targetPercent,
            ghost: withOpen === null || reached ? undefined : Math.min(100, Math.max(0, (100 * withOpen) / results.targetRequired)),
          },
    );
  }

  objectives.push(lossLimit(details, "daily", "Daily loss limit"));
  objectives.push(lossLimit(details, "max-loss", rules.maxLoss.kind === "Trailing" ? "Max loss limit (trailing)" : "Max loss limit"));

  if (account.funded) {
    const quote = account.nextPayout;
    if (quote?.consistencyPercent != null && quote.bestDayProfit != null) {
      // The best day may have made at most the share of the profit since the last payout.
      const share = quote.profit > 0 ? Math.round((100 * quote.bestDayProfit) / quote.profit) : 0;
      const kept = quote.profit <= 0 || share <= quote.consistencyPercent;
      objectives.push({
        key: "consistency",
        title: "Consistency",
        state: quote.profit <= 0 ? "info" : kept ? "kept" : "warning",
        stateText: quote.profit <= 0 ? "" : `Best day ${share}%`,
        detail: `For a payout, your best day may have made at most ${quote.consistencyPercent}% of the profit since the last one. Your best day made ${formatMoney(quote.bestDayProfit)}.`,
      });
    }

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
    objectives.push(deadline("time-limit", "Time limit", "Pass", timeLimit, `Pass ${account.stageName} by ${formatDate(timeLimit.lastDay)}.`));
  } else if (details.expiry?.reason === "TimeLimit") {
    objectives.push({ key: "time-limit", title: "Time limit", state: "broken", stateText: "Ran out", detail: `The stage was not passed in time.` });
  }

  const inactivity = deadlineOf(details, account.inactivityDeadline);
  if (inactivity) {
    objectives.push(
      deadline("activity", "Stay active", "Trade", inactivity, `Open a new trade by ${formatDate(inactivity.lastDay)}. The challenge ends after ${details.challenge.inactivityDays} days without one.`),
    );
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
      detail: `Equity ${formatMoney(breach.equity)} fell below ${formatMoney(breach.level)} on ${formatDateTime(breach.time, details.challenge.tradingDay.timeZone)}.`,
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
      detail: `Equity ${formatMoney(breach.equity)} fell below ${formatMoney(breach.level)} on ${formatDateTime(breach.time, details.challenge.tradingDay.timeZone)}.`,
    };
  }

  const floor = liveFloor(details, floorId);
  if (floor) {
    const state = floorState(floor);
    const reset =
      floorId === "daily" && details.results.nextDayStartsAt
        ? ` It starts again ${formatDateTime(details.results.nextDayStartsAt, details.challenge.tradingDay.timeZone)}.`
        : "";
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

function deadline(key: string, title: string, verb: string, due: Deadline, detail: string): Objective {
  const close = due.daysLeft != null && due.daysLeft <= deadlineWarningDays;
  return {
    key,
    title,
    state: close ? "warning" : "info",
    stateText: deadlineText(verb, due),
    detail,
  };
}

/** The share of a stage's profit split, for example for the funded stage in the stepper. */
export function profitSplitText(details: AccountDetails): string | null {
  const split = details.challenge.funded.profitSplitPercent;
  return split == null ? null : `${split}% of the profit is yours`;
}
