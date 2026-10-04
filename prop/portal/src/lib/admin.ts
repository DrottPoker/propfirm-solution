import type { Account, AccountGroup, Activity, MoneyTotal, PassRate, PayoutSummary, Slots, Week } from "./api/types";
import { niceTicks } from "./chart";
import { failureLabels } from "./challenge";
import { formatMoney, formatSignedMoney } from "./format";

// What the admin panel shows of the firm (ADR 0023). Prop.Api counts and sums; this only words and arranges it.

/** The tones of the admin panel's badges and marks, the same as the badges in ui.tsx. */
export type StatusTone = "accent" | "profit" | "loss" | "warning" | "muted";

export const accountGroups: AccountGroup[] = ["All", "Evaluation", "AwaitingFunding", "Funded", "Ended"];

export const groupLabels: Record<AccountGroup, string> = {
  All: "All",
  Evaluation: "Evaluation",
  AwaitingFunding: "Waiting for you",
  Funded: "Funded",
  Ended: "Ended",
};

/** Where an account is, in a few words for the list of accounts, with its tone. */
export function accountStatus(account: Account): { label: string; tone: StatusTone } {
  switch (account.status) {
    case "Failed":
      return { label: "Failed", tone: "loss" };
    case "Cancelled":
      return { label: "Cancelled", tone: "muted" };
    case "AwaitingFunding":
      return { label: "Waiting for your approval", tone: "warning" };
    case "OpeningAccount":
      return { label: "Opening its account", tone: "muted" };
    default:
      return account.paused ? { label: "Paused", tone: "warning" } : { label: "Trading", tone: "accent" };
  }
}

/** The stage an account is on, for example "Phase 1" or "Funded". */
export function stageLabel(account: Account): string {
  return account.status === "AwaitingFunding" ? "Every stage passed" : account.stageName;
}

/** Amounts per currency in one line, for example "19,498.00 USD" or "120.00 USD + 50.00 EUR". None is zero in the fallback currency. */
export function formatTotals(totals: MoneyTotal[], fallbackCurrency: string): string {
  return totals.length === 0 ? `${formatMoney(0)} ${fallbackCurrency}` : totals.map((t) => `${formatMoney(t.amount)} ${t.currency}`).join(" + ");
}

/** The share of ended evaluations that passed every stage, for example "12%". A dash before any has ended. */
export function passRateText(rate: PassRate): string {
  return rate.ended === 0 ? "-" : `${Math.round((rate.passed / rate.ended) * 100)}%`;
}

/** How long ago a time was, for example "12 min ago", "4 hours ago" or "2 days ago". */
export function ageText(iso: string, now: number): string {
  const minutes = Math.max(0, Math.floor((now - Date.parse(iso)) / 60_000));
  if (minutes < 1) {
    return "just now";
  }

  if (minutes < 60) {
    return `${minutes} min ago`;
  }

  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return hours === 1 ? "1 hour ago" : `${hours} hours ago`;
  }

  const days = Math.floor(hours / 24);
  return days === 1 ? "1 day ago" : `${days} days ago`;
}

/** When something happened, short for a list: the time today, "Yesterday", or the date, for example "3 Oct". */
export function whenText(iso: string, now: number): string {
  const time = new Date(iso);
  const today = new Date(now);
  const yesterday = new Date(now - 86_400_000);
  const sameDay = (a: Date, b: Date) => a.toDateString() === b.toDateString();
  if (sameDay(time, today)) {
    return time.toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" });
  }

  return sameDay(time, yesterday) ? "Yesterday" : time.toLocaleDateString("en-GB", { day: "numeric", month: "short" });
}

export type NeedsYouIcon = "payout" | "approve" | "slots";

/** Something the firm has to do, with where to do it. */
export type NeedsYouItem = {
  key: string;
  icon: NeedsYouIcon;
  tone: "accent" | "warning";
  title: string;
  detail: string;
  href: string;
  action: string;
};

/** How many accounts waiting for a funded account are named one by one before the rest are counted. */
export const namedWaitingAccounts = 3;

/**
 * What the firm has to do, the most urgent first: payouts to approve and to pay, accounts that passed every stage and
 * wait for the firm, and slots that run out. Billing problems are said on every page instead.
 */
export function needsYouItems(input: {
  payouts: PayoutSummary;
  waitingAccounts: Account[];
  waitingCount: number;
  challengeName: (challengeId: string) => string;
  slots: Slots | null;
  currency: string;
  now: number;
}): NeedsYouItem[] {
  const { payouts, waitingAccounts, waitingCount, slots, currency, now } = input;
  const items: NeedsYouItem[] = [];
  const toApprove = payouts.toApprove;
  if (toApprove.count > 0) {
    items.push({
      key: "approve-payouts",
      icon: "payout",
      tone: "accent",
      title: toApprove.count === 1 ? "A payout waits for your approval" : `${toApprove.count} payouts wait for your approval`,
      detail: `${formatTotals(toApprove.totals, currency)}${toApprove.count === 1 ? "" : " in total"}.${toApprove.oldest ? ` ${toApprove.count === 1 ? "It" : "The oldest"} was asked for ${ageText(toApprove.oldest, now)}.` : ""}`,
      href: "/admin/payouts",
      action: "Review payouts",
    });
  }

  const toPay = payouts.toPay;
  if (toPay.count > 0) {
    items.push({
      key: "pay-payouts",
      icon: "payout",
      tone: "accent",
      title: toPay.count === 1 ? "An approved payout to pay" : `${toPay.count} approved payouts to pay`,
      detail: `${formatTotals(toPay.totals, currency)}. Send the money, then mark ${toPay.count === 1 ? "it" : "them"} as paid.`,
      href: "/admin/payouts?view=to-pay",
      action: "Pay",
    });
  }

  for (const account of waitingAccounts.slice(0, namedWaitingAccounts)) {
    items.push({
      key: `approve-${account.id}`,
      icon: "approve",
      tone: "accent",
      title: `${account.email} passed ${input.challengeName(account.challengeId)}`,
      detail: `Account #${account.number} waits for a funded account. Approve it when your checks, such as KYC, are done.`,
      href: `/admin/accounts/${account.id}`,
      action: "Review account",
    });
  }

  if (waitingCount > namedWaitingAccounts) {
    const more = waitingCount - namedWaitingAccounts;
    items.push({
      key: "approve-more",
      icon: "approve",
      tone: "accent",
      title: `${more} more ${more === 1 ? "account waits" : "accounts wait"} for a funded account`,
      detail: "Each passed every evaluation stage.",
      href: "/admin/accounts?group=AwaitingFunding",
      action: "Show them",
    });
  }

  if (slots && slots.slots !== null && (slots.warning || slots.free === 0)) {
    items.push({
      key: "slots",
      icon: "slots",
      tone: "warning",
      title: `${slots.used + slots.reserved} of ${slots.slots} slots are taken`,
      detail: "No new challenge can start when every slot is taken. Add slots, or let them be added when the last one is taken.",
      href: "/admin/billing",
      action: "Add slots",
    });
  }

  return items;
}

export type ActivityIcon = "start" | "bag" | "check" | "shield" | "cross" | "clock" | "payout" | "paid";

/** One thing that happened, worded for the overview. The trader and the account are shown beside it. */
export type ActivityView = { title: string; note: string | null; tone: StatusTone; icon: ActivityIcon };

export function activityView(activity: Activity): ActivityView {
  const amount = activity.amount != null && activity.currency ? `${formatMoney(activity.amount)} ${activity.currency}` : null;
  switch (activity.kind) {
    case "ChallengeStarted":
      return { title: "Challenge started", note: null, tone: "accent", icon: "start" };
    case "ChallengeBought":
      return { title: "Challenge bought", note: amount, tone: "profit", icon: "bag" };
    case "StagePassed":
      return {
        title: `Passed ${activity.stageName ?? "a stage"}`,
        note: activity.amount == null ? null : `${formatSignedMoney(activity.amount)}${activity.tradingDays == null ? "" : ` in ${days(activity.tradingDays)}`}`,
        tone: "profit",
        icon: "check",
      };
    case "EvaluationPassed":
      return { title: "Passed every stage", note: "waits for your approval", tone: "accent", icon: "shield" };
    case "FundedStarted":
      return { title: "Funded account started", note: null, tone: "profit", icon: "check" };
    case "ChallengeFailed":
      return {
        title: `Failed ${activity.stageName ?? ""}`.trim(),
        note: activity.reason && activity.reason in failureLabels ? failureLabels[activity.reason as keyof typeof failureLabels] : null,
        tone: "loss",
        icon: "cross",
      };
    case "ChallengeExpired":
      return {
        title: `Ran out of time in ${activity.stageName ?? "a stage"}`,
        note: activity.reason === "Inactivity" ? "no new trade for too long" : activity.reason === "TimeLimit" ? "not passed within its time limit" : null,
        tone: "muted",
        icon: "clock",
      };
    case "ChallengeCancelled":
      return { title: "Cancelled", note: activity.reason, tone: "muted", icon: "cross" };
    case "PayoutRequested":
      return { title: "Payout asked for", note: amount, tone: "accent", icon: "payout" };
    case "PayoutPaid":
      return { title: "Payout marked as paid", note: [amount, activity.reference && `reference ${activity.reference}`].filter(Boolean).join(" · ") || null, tone: "muted", icon: "paid" };
    case "PayoutRejected":
      return { title: "Payout rejected", note: amount, tone: "loss", icon: "cross" };
  }
}

/** A week's two amounts per currency, such as sales and payouts, from Monday <code>start</code>. */
export type WeekAmounts = { start: string; first: MoneyTotal[]; second: MoneyTotal[] };

/** A week's bar pair in one currency. */
export type WeekBar = { start: string; first: number; second: number };

/** A firm's sales and payouts per week, for the chart. */
export function salesAndPayouts(weeks: Week[]): WeekAmounts[] {
  return weeks.map((week) => ({ start: week.start, first: week.sales, second: week.payouts }));
}

/**
 * Two amounts per week in one currency, with the axis they are drawn on. Amounts in other currencies are left out, and
 * <code>otherCurrencies</code> says if there were any.
 */
export function weekBars(weeks: WeekAmounts[], currency: string): { bars: WeekBar[]; ticks: number[]; otherCurrencies: boolean } {
  const amountIn = (totals: MoneyTotal[]) => totals.find((t) => t.currency === currency)?.amount ?? 0;
  const bars = weeks.map((week) => ({ start: week.start, first: amountIn(week.first), second: amountIn(week.second) }));
  const otherCurrencies = weeks.some((week) => [...week.first, ...week.second].some((t) => t.currency !== currency));
  const max = Math.max(0, ...bars.flatMap((b) => [b.first, b.second]));
  return { bars, ticks: axisTicks(max), otherCurrencies };
}

/** Clean ticks from zero to at least the highest value, such as 0 / 2,000 / 4,000 / 6,000. */
export function axisTicks(max: number): number[] {
  if (max <= 0) {
    return [0, 1];
  }

  const ticks = niceTicks(0, max, 4);
  const step = ticks.length > 1 ? ticks[1] - ticks[0] : max;
  while (ticks[ticks.length - 1] < max) {
    ticks.push(Number((ticks[ticks.length - 1] + step).toFixed(10)));
  }

  return ticks;
}

/** A short label for an amount on an axis, for example "6k" or "1.5M". */
export function shortAmount(value: number): string {
  if (value >= 1_000_000) {
    return `${Number((value / 1_000_000).toFixed(1))}M`;
  }

  return value >= 1_000 ? `${Number((value / 1_000).toFixed(1))}k` : `${value}`;
}

function days(count: number): string {
  return count === 1 ? "1 trading day" : `${count} trading days`;
}
