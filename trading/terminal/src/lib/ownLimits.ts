import type { AccountSnapshot, EngineEvent, FloorSnapshot, LockReason, OwnLimits, OwnLimitsSnapshot, OwnLock } from "./api/types";
import { formatClock, formatMoney, formatTime, timeZoneName } from "./format";

// The limits the trader sets for themselves (ADR 0054): a daily loss limit and a daily profit target, which close every
// position and lock new orders until the next trading day, and the most trades a day. The engine keeps them; this is
// how the terminal shows and asks for them. Their times are on the clock of the trading day they count in, so "until
// 00:00 Stockholm time" is when the day really starts.

/** No limits. */
export const noLimits: OwnLimits = { dailyLoss: null, dailyTarget: null, maxTrades: null };

/** The most trades a day the engine takes as a limit. */
export const mostTrades = 1_000;

/** Whether any limit is on. */
export function hasLimits(limits: OwnLimits): boolean {
  return limits.dailyLoss !== null || limits.dailyTarget !== null || limits.maxTrades !== null;
}

/**
 * The limits that hold now when the trader asks for the wanted ones, as the engine decides: each that is stricter
 * applies at once, and each that is looser or turned off from the next trading day. A lower amount or count is stricter,
 * and so is any limit where there was none.
 */
export function stricterOf(now: OwnLimits, wanted: OwnLimits): OwnLimits {
  const stricter = (current: number | null, value: number | null) => (value !== null && (current === null || value < current) ? value : current);
  return {
    dailyLoss: stricter(now.dailyLoss, wanted.dailyLoss),
    dailyTarget: stricter(now.dailyTarget, wanted.dailyTarget),
    maxTrades: stricter(now.maxTrades, wanted.maxTrades),
  };
}

/** A moment as the trading day's clock shows it, named, for example "00:00 Stockholm time". */
export function clockText(iso: string, timeZone: string): string {
  return `${formatClock(iso, timeZone)} ${timeZoneName(timeZone)}`;
}

/** The time until a moment, for example "7 hours 28 minutes" or "12 minutes". */
export function timeUntilText(iso: string, now: number): string {
  const minutes = Math.max(1, Math.ceil((Date.parse(iso) - now) / 60_000));
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  const unit = (n: number, word: string) => `${n} ${word}${n === 1 ? "" : "s"}`;
  return hours === 0 ? unit(rest, "minute") : rest === 0 ? unit(hours, "hour") : `${unit(hours, "hour")} ${unit(rest, "minute")}`;
}

export type LimitTone = "accent" | "profit" | "warning" | "loss";

export interface OwnLimitRow {
  id: "loss" | "target" | "trades";
  label: string;
  value: string;
  /** How much of the limit is used, from 0 to 1. */
  used: number;
  tone: LimitTone;
  note: string;
}

/** How close a limit with room left of the whole is, as the account bar colors the firm's loss limits. */
export function closeness(left: number, whole: number): "ok" | "warning" | "danger" {
  if (left <= 0) {
    return "danger";
  }

  const share = left / whole;
  return share < 0.1 ? "danger" : share < 0.25 ? "warning" : "ok";
}

const closenessTones = { ok: "accent", warning: "warning", danger: "loss" } as const;

const clamp = (value: number) => Math.min(1, Math.max(0, value));

/** Each limit that is on and where it stands today, for the rulebook. */
export function ownLimitRows(own: OwnLimitsSnapshot, equity: number): OwnLimitRow[] {
  const rows: OwnLimitRow[] = [];
  const { dailyLoss, dailyTarget, maxTrades } = own.limits;
  if (dailyLoss !== null && own.lossLevel !== null) {
    const left = Math.max(0, equity - own.lossLevel);
    rows.push({
      id: "loss",
      label: `Daily loss limit, ${formatMoney(dailyLoss)}`,
      value: left > 0 ? `${formatMoney(left)} left` : "Reached",
      used: clamp(1 - left / dailyLoss),
      tone: closenessTones[closeness(left, dailyLoss)],
      note: `Positions close and the day locks when equity reaches ${formatMoney(own.lossLevel)}.`,
    });
  }

  if (dailyTarget !== null && own.targetLevel !== null) {
    const toGo = Math.max(0, own.targetLevel - equity);
    rows.push({
      id: "target",
      label: `Daily profit target, ${formatMoney(dailyTarget)}`,
      value: toGo > 0 ? `${formatMoney(toGo)} to go` : "Reached",
      used: clamp(1 - toGo / dailyTarget),
      tone: "profit",
      note: `Positions close and the day locks when equity reaches ${formatMoney(own.targetLevel)}.`,
    });
  }

  if (maxTrades !== null) {
    const reached = own.tradesToday >= maxTrades;
    rows.push({
      id: "trades",
      label: "Trades a day",
      value: `${own.tradesToday} of ${maxTrades}`,
      used: clamp(own.tradesToday / maxTrades),
      tone: reached ? "warning" : "accent",
      note: reached ? "No new positions until the next trading day." : `New orders are refused after trade number ${maxTrades} today.`,
    });
  }

  return rows;
}

// Each limit in a sentence, for example "daily loss limit 2,000.00" or "trades a day off".
const parts: { key: keyof OwnLimits; text: (v: number | null) => string }[] = [
  { key: "dailyLoss", text: (v) => `daily loss limit ${v === null ? "off" : formatMoney(v)}` },
  { key: "dailyTarget", text: (v) => `daily profit target ${v === null ? "off" : formatMoney(v)}` },
  { key: "maxTrades", text: (v) => (v === null ? "trades a day off" : `${v} trades a day`) },
];

/** What changes when the next trading day starts, for example "From 00:00: daily loss limit 2,000.00", or null. */
export function pendingText(limits: OwnLimits, pending: OwnLimits | null, nextDayStart: string, timeZone: string): string | null {
  if (pending === null) {
    return null;
  }

  const changes = parts.filter((p) => pending[p.key] !== limits[p.key]).map((p) => p.text(pending[p.key]));
  return changes.length === 0 ? null : `From ${formatClock(nextDayStart, timeZone)}: ${changes.join(", ")}`;
}

type Locked = Extract<EngineEvent, { kind?: "TradingLocked" }>;

/** The event that locked the account, from its latest events, for when and how many positions it closed. */
export function lockEvent(events: readonly EngineEvent[], lock: OwnLock): Locked | undefined {
  return events.findLast((e): e is Locked => e.kind === "TradingLocked" && e.until === lock.until);
}

const reachedNames: Record<Exclude<LockReason, "Trader">, string> = { DailyLoss: "daily loss limit", DailyTarget: "daily profit target" };

/** The heading of the note while new orders are locked, for example "Your own daily loss limit was reached at 16:32:08 Stockholm time". */
export function lockTitle(lock: OwnLock, lockedAt: string | null, timeZone: string): string {
  const at = lockedAt ? ` at ${formatTime(lockedAt, timeZone)} ${timeZoneName(timeZone)}` : "";
  return lock.reason === "Trader" ? `You locked the rest of the day${at}` : `Your own ${reachedNames[lock.reason]} was reached${at}`;
}

/**
 * What the lock means for the trader, in a sentence or two. <paramref name="carriesOn"/> says what goes on, in the words
 * of the firm's kind of business (ADR 0058), for example "The challenge goes on."
 */
export function lockDescription(lock: OwnLock, positionsClosed: number | null, timeZone: string, carriesOn = "The challenge goes on."): string {
  const until = `New orders are locked until the next trading day starts at ${clockText(lock.until, timeZone)}.`;
  if (lock.reason === "Trader") {
    return `${until} You can still close positions and change their stops.`;
  }

  const closed = positionsClosed === null || positionsClosed > 0 ? "Every position was closed at that price. " : "";
  return `${closed}${until} This was your limit, not the firm's. ${carriesOn}`;
}

/** Why the order ticket takes no orders while the day is locked. */
export function orderLockText(lock: OwnLock, timeZone: string): string {
  const until = clockText(lock.until, timeZone);
  return lock.reason === "Trader"
    ? `You locked the rest of the day, until ${until}. You can still close positions and change their stops.`
    : `Locked by your own ${reachedNames[lock.reason]} until ${until}. Charts, history and your account stay open.`;
}

/** Why the order ticket takes no orders once the day's trades are used. */
export function tradesUsedText(own: OwnLimitsSnapshot, timeZone: string): string {
  return `You have opened your ${own.limits.maxTrades} trades for today. New orders are taken again from ${clockText(own.nextDayStart, timeZone)}.`;
}

/** Whether the trader has opened as many positions today as their limit allows. */
export function tradesUsed(own: OwnLimitsSnapshot): boolean {
  return own.limits.maxTrades !== null && own.tradesToday >= own.limits.maxTrades;
}

/** What the trader types in the form of their limits. The daily loss limit is in the account currency or in percent of the day's start. */
export interface LimitsForm {
  dailyLoss: string;
  lossUnit: "amount" | "percent";
  dailyTarget: string;
  maxTrades: string;
}

/** The form filled in with the limits as they are, from the next trading day when some are loosened. */
export function limitsForm(limits: OwnLimits): LimitsForm {
  const amount = (v: number | null) => (v === null ? "" : v.toFixed(2));
  return { dailyLoss: amount(limits.dailyLoss), lossUnit: "amount", dailyTarget: amount(limits.dailyTarget), maxTrades: limits.maxTrades === null ? "" : String(limits.maxTrades) };
}

export type LimitsParse = { ok: true; limits: OwnLimits } | { ok: false; field: "dailyLoss" | "dailyTarget" | "maxTrades"; message: string };

/**
 * The limits the form asks for, or what is wrong with it. An empty field is off. Amounts take at most 2 decimals, and a
 * percent of the day's start becomes the amount. The daily loss limit must be smaller than the firm's, when known, or it
 * would never be reached first.
 */
export function parseLimitsForm(form: LimitsForm, dayStartBalance: number, firmDailyLoss: number | null): LimitsParse {
  const decimal = (text: string) => {
    const t = text.trim().replace(/,/g, "");
    return /^\d+(\.\d{1,2})?$/.test(t) && Number(t) > 0 ? Number(t) : null;
  };

  let dailyLoss: number | null = null;
  if (form.dailyLoss.trim() !== "") {
    const value = decimal(form.dailyLoss);
    if (value === null || (form.lossUnit === "percent" && value >= 100)) {
      return { ok: false, field: "dailyLoss", message: form.lossUnit === "percent" ? "Type a percent above 0 and below 100." : "Type an amount above 0." };
    }

    dailyLoss = form.lossUnit === "percent" ? Math.round(dayStartBalance * value) / 100 : value;
    if (dailyLoss <= 0) {
      return { ok: false, field: "dailyLoss", message: "The percent is too small for the balance." };
    }

    if (firmDailyLoss !== null && dailyLoss >= firmDailyLoss) {
      return { ok: false, field: "dailyLoss", message: `Must be smaller than the firm's daily loss limit of ${formatMoney(firmDailyLoss)}.` };
    }
  }

  let dailyTarget: number | null = null;
  if (form.dailyTarget.trim() !== "") {
    dailyTarget = decimal(form.dailyTarget);
    if (dailyTarget === null) {
      return { ok: false, field: "dailyTarget", message: "Type an amount above 0." };
    }
  }

  let maxTrades: number | null = null;
  if (form.maxTrades.trim() !== "") {
    const text = form.maxTrades.trim();
    maxTrades = /^\d+$/.test(text) ? Number(text) : NaN;
    if (!(maxTrades >= 1 && maxTrades <= mostTrades)) {
      return { ok: false, field: "maxTrades", message: `Type a whole number from 1 to ${mostTrades.toLocaleString("en-US")}.` };
    }
  }

  return { ok: true, limits: { dailyLoss, dailyTarget, maxTrades } };
}

/** The whole loss the firm's daily loss limit allows, when it is counted from the day's start. */
export function firmDailyLoss(floors: readonly FloorSnapshot[]): number | null {
  const daily = floors.find((f) => f.floorId === "daily");
  return daily && "distance" in daily.rule && daily.rule.distance > 0 ? daily.rule.distance : null;
}

/** Whether the trader can still change limits or lock the day: not once trading on the account has ended. */
export function canSetLimits(account: Pick<AccountSnapshot, "status">): boolean {
  return account.status !== "Disabled";
}
