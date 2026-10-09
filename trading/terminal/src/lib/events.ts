import { floorLabel } from "./account";
import type { EngineEvent } from "./api/types";
import { formatMoney, formatPercent, formatPrice, formatVolume, timeZoneName } from "./format";
import { trailingPips } from "./orderTools";

/** Price decimals for a symbol. Falls back to 5 for symbols the terminal does not know. */
export type DigitsOf = (symbol: string) => number;

type CloseReason = Extract<EngineEvent, { kind?: "PositionClosed" }>["reason"];
type CancelReason = Extract<EngineEvent, { kind?: "OrderCancelled" }>["reason"];
type DisableReason = Extract<EngineEvent, { kind?: "AccountDisabled" }>["reason"];
type RejectReason = Extract<EngineEvent, { kind?: "InputRejected" }>["reason"];

/** Why a position closed, in plain words, for the history and the events. */
export const closeReasons: Record<CloseReason, string> = {
  Manual: "Manual",
  StopLoss: "Stop loss",
  TakeProfit: "Take profit",
  StopOut: "Stop out",
  EquityFloor: "Loss limit",
  AccountClosed: "Account closed",
  OwnLimit: "Your own limit",
};

/** How a position closed, after "Closed Buy 1.00 EURUSD at 1.07500": nothing for a manual close. */
export const closedBecause: Record<CloseReason, string> = {
  Manual: "",
  StopLoss: " by its stop loss",
  TakeProfit: " by its take profit",
  StopOut: " by a stop out",
  EquityFloor: " by the loss limit",
  AccountClosed: " because the account closed",
  OwnLimit: " by your own limit",
};

const cancelledBecause: Record<CancelReason, string> = {
  Manual: "",
  InsufficientMargin: ": not enough free margin when it filled",
  EquityFloor: " by the loss limit",
  AccountClosed: " because the account closed",
  AccountSuspended: " because trading was paused",
  TradingLocked: " because new orders are locked for the day",
  TradeLimit: ": you have used your trades for the day",
};

const disabledBecause: Record<DisableReason, string> = { EquityFloor: "a loss limit was broken", Closed: "the firm closed it" };

/** Why the trading service refused something, in plain words. */
const rejections: Record<RejectReason, string> = {
  OutOfOrder: "it arrived out of order",
  InvalidId: "its id is not valid",
  DuplicateId: "it was already done",
  UnknownSymbol: "the symbol is unknown",
  InvalidQuote: "the price is not valid",
  UnknownGroup: "the group is unknown",
  InvalidGroup: "the group is not valid",
  GroupNotChangeable: "the group cannot be changed",
  SymbolInUse: "the symbol is in use",
  InvalidAmount: "the amount is not valid",
  UnknownAccount: "the account is unknown",
  AccountDisabled: "trading on the account has ended",
  AccountSuspended: "trading is paused on the account",
  AccountNotSuspended: "trading is not paused",
  AccountNotDisabled: "trading on the account has not ended",
  SymbolNotTradable: "the symbol cannot be traded on this account",
  InvalidOrder: "the order is not valid",
  InvalidVolume: "the volume is not allowed for the symbol",
  InvalidPrice: "the price is on the wrong side of the market for this order type",
  InvalidStopLoss: "the stop loss is on the wrong side of the price",
  InvalidTakeProfit: "the take profit is on the wrong side of the price",
  NoPrice: "there is no price for the symbol yet",
  StalePrice: "the price is too old, wait for the next one",
  MarketClosed: "the market is closed",
  NoConversionRate: "the result cannot be converted to the account currency right now",
  InsufficientMargin: "not enough free margin",
  UnknownOrder: "the order no longer exists",
  UnknownPosition: "the position no longer exists",
  NoStopLoss: "a trailing stop needs a stop loss to follow",
  InvalidFloor: "the loss limit is not valid",
  UnknownFloor: "the loss limit is unknown",
  InsufficientFunds: "the balance is too low",
  AccountLocked: "new orders are locked until the next trading day",
  TradeLimitReached: "you have used your trades for the day",
  InvalidLimits: "the limits are not valid",
  InvalidTradingDay: "the trading day is not valid",
};

/** Refusals from the service itself rather than the engine, such as a trader asking too often (ADR 0059). */
const serviceRejections: Record<string, string> = {
  TooManyRequests: "too many requests at once, wait a moment and try again",
};

/** Why the trading service refused something, for example "not enough free margin". */
export function rejectionReason(reason: string): string {
  return (rejections as Record<string, string | undefined>)[reason] ?? serviceRejections[reason] ?? reason;
}

/** A refusal from the trading service for the trader, for example "Refused: not enough free margin". */
export function rejectionText(reason: string): string {
  return `Refused: ${rejectionReason(reason)}`;
}

const inputNames: Record<string, string> = {
  PlaceOrder: "Order",
  ModifyOrder: "Changing the order",
  CancelOrder: "Cancelling the order",
  ClosePosition: "Closing the position",
  CloseAllPositions: "Closing all positions",
  ModifyPosition: "Changing the stops",
  SetOwnLimits: "Changing your limits",
  LockTrading: "Locking the day",
};

const lockedBy: Record<string, string> = { Trader: "by you", DailyLoss: "by your daily loss limit", DailyTarget: "by your daily profit target" };

// An own limit in a few words, for example "daily loss 1,500.00" or "6 trades a day".
function limitsText(limits: { dailyLoss: number | null; dailyTarget: number | null; maxTrades: number | null }): string {
  const parts = [
    limits.dailyLoss === null ? null : `daily loss ${formatMoney(limits.dailyLoss)}`,
    limits.dailyTarget === null ? null : `daily profit target ${formatMoney(limits.dailyTarget)}`,
    limits.maxTrades === null ? null : `${limits.maxTrades} trades a day`,
  ].filter((p) => p !== null);
  return parts.length === 0 ? "none" : parts.join(", ");
}

// ", trailing 10.0 pips" after the stops of a position or order with a trailing stop.
const trailing = (distance: number | null | undefined, digits: number) => (distance == null ? "" : `, trailing ${trailingPips(distance, digits)} pips`);

/** One line of text describing an engine event for the trader, in plain words. */
export function describeEvent(event: EngineEvent, digitsOf: DigitsOf): string {
  switch (event.kind) {
    case "AccountCreated":
      return `Account opened with ${formatMoney(event.balance)} ${event.currency}`;
    case "OrderPlaced":
      return `Placed ${event.side} ${event.type} ${formatVolume(event.volume)} ${event.symbol} at ${formatPrice(event.price, digitsOf(event.symbol))}${trailing(event.trailingDistance, digitsOf(event.symbol))}`;
    case "OrderModified":
      return `Moved order ${shortId(event.orderId)} on ${event.symbol} to ${formatPrice(event.price, digitsOf(event.symbol))}: SL ${event.stopLoss ?? "-"}, TP ${event.takeProfit ?? "-"}${trailing(event.trailingDistance, digitsOf(event.symbol))}`;
    case "OrderCancelled":
      return `Order ${shortId(event.orderId)} cancelled${cancelledBecause[event.reason]}`;
    case "PositionOpened":
      return `Opened ${event.side} ${formatVolume(event.volume)} ${event.symbol} at ${formatPrice(event.openPrice, digitsOf(event.symbol))}${trailing(event.trailingDistance, digitsOf(event.symbol))}`;
    case "PositionModified":
      return `Changed stops on ${shortId(event.positionId)}: SL ${event.stopLoss ?? "-"}, TP ${event.takeProfit ?? "-"}${event.trailingDistance == null ? "" : ", trailing"}`;
    case "PositionPartiallyClosed":
      return `Closed ${formatVolume(event.volume)} of ${event.side} ${event.symbol} at ${formatPrice(event.closePrice, digitsOf(event.symbol))}, ${formatVolume(event.remainingVolume)} left open, profit ${formatMoney(event.profit)} before commission`;
    case "PositionClosed":
      return `Closed ${event.side} ${formatVolume(event.volume)} ${event.symbol} at ${formatPrice(event.closePrice, digitsOf(event.symbol))}${closedBecause[event.reason]}, profit ${formatMoney(event.profit)} before commission`;
    case "EquityFloorSet":
      return `${floorLabel(event.floorId)} set at ${formatMoney(event.level)}`;
    case "EquityFloorRemoved":
      return `${floorLabel(event.floorId)} removed`;
    case "EquityFloorBreached":
      return `${floorLabel(event.floorId)} broken: equity ${formatMoney(event.equity)} fell below ${formatMoney(event.level)}`;
    case "StopOutTriggered":
      return `Stop out at margin level ${formatPercent(event.marginLevelPercent)}`;
    case "AccountDisabled":
      return `Trading ended: ${disabledBecause[event.reason]}`;
    case "AccountSuspended":
      return "Trading paused: no new orders until it is resumed";
    case "AccountResumed":
      return "Trading resumed";
    case "AccountReopened":
      return `Trading reopened by the firm with a balance of ${formatMoney(event.balance)}`;
    case "BalanceAdjusted":
      return `${balanceOperationName(event.amount)} of ${formatMoney(Math.abs(event.amount))}, balance ${formatMoney(event.balanceAfter)}`;
    case "TradingDaySet":
      return `Trading days start at ${event.day.start.slice(0, 5)} ${timeZoneName(event.day.timeZone)}`;
    case "TradingDayStarted":
      return `New trading day from a balance of ${formatMoney(event.dayStartBalance)}`;
    case "OwnLimitsSet":
      return `Your limits: ${limitsText(event.limits)}${event.pending ? `. From the next trading day: ${limitsText(event.pending)}` : ""}`;
    case "OwnLimitReached":
      return `Your own ${event.limit === "DailyLoss" ? "daily loss limit" : "daily profit target"} reached: equity ${formatMoney(event.equity)} at ${formatMoney(event.level)}`;
    case "TradingLocked":
      return `New orders locked ${lockedBy[event.reason]} until the next trading day`;
    case "TradingUnlocked":
      return "New orders taken again";
    case "InputRejected":
      return `${inputNames[event.input.kind ?? ""] ?? "Request"} refused: ${rejectionReason(event.reason)}`;
    default:
      return "Unknown event";
  }
}

type ClosedPosition = Extract<EngineEvent, { kind?: "PositionClosed" }>;
type OpenedPosition = Extract<EngineEvent, { kind?: "PositionOpened" }>;
type ClosedPart = Extract<EngineEvent, { kind?: "PositionPartiallyClosed" }>;

/**
 * What a closed part of a position made after the commission for closing it. The commission for opening the position
 * is counted with its last close, so the parts and the last close add up to the whole position.
 */
export function partResult(part: ClosedPart): number {
  return Math.round((part.profit - part.commission) * 100) / 100;
}

/**
 * Commission charged for a closed position, on open and on close. The opening commission comes from the position's
 * opened event. When that is older than the events at hand, it is taken to be the closing one, which is the same
 * unless the firm changed its commission while the position was open.
 */
export function positionCommission(closed: ClosedPosition, events: readonly EngineEvent[]): number {
  const opened = events.find((e): e is OpenedPosition => e.kind === "PositionOpened" && e.positionId === closed.positionId);
  return closed.commission + (opened?.commission ?? closed.commission);
}

/** What a closed position made after its commission, the result the firm's portal shows for it too. */
export function netResult(closed: ClosedPosition, events: readonly EngineEvent[]): number {
  return Math.round((closed.profit - positionCommission(closed, events)) * 100) / 100;
}

/** Money added to or taken from the account, for example a payout. Not a trading result. */
export function balanceOperationName(amount: number): "Deposit" | "Withdrawal" {
  return amount >= 0 ? "Deposit" : "Withdrawal";
}

type FloorSet = Extract<EngineEvent, { kind?: "EquityFloorSet" }>;
type DaySet = Extract<EngineEvent, { kind?: "TradingDaySet" }>;

/**
 * The events without those that only repeat what the trader already knows: a loss limit set again at the level it had,
 * as the firm's system does at the start of every trading day, and the trading day told again as it was. Oldest first.
 */
export function withoutRepeats(events: readonly EngineEvent[]): EngineEvent[] {
  const floors = new Map<string, number>();
  let day: string | null = null;
  return events.filter((event) => {
    if (event.kind === "EquityFloorSet") {
      const floor = event as FloorSet;
      const same = floors.get(floor.floorId) === floor.level;
      floors.set(floor.floorId, floor.level);
      return !same;
    }

    if (event.kind === "EquityFloorRemoved") {
      floors.delete(event.floorId);
      return true;
    }

    if (event.kind === "TradingDaySet") {
      const set = event as DaySet;
      const key = `${set.day.timeZone} ${set.day.start}`;
      const same = day === key;
      day = key;
      return !same;
    }

    return true;
  });
}

/** Events that need the trader's attention. */
export function isWarning(event: EngineEvent): boolean {
  return (
    event.kind === "InputRejected" ||
    event.kind === "EquityFloorBreached" ||
    event.kind === "StopOutTriggered" ||
    event.kind === "AccountDisabled" ||
    event.kind === "AccountSuspended" ||
    event.kind === "OwnLimitReached" ||
    event.kind === "TradingLocked"
  );
}

/** What an event is about, for the filters of the event list: trading, the account, or something that needs attention. */
export type EventTopic = "trades" | "account" | "warnings";

const tradeKinds = new Set<string>([
  "OrderPlaced",
  "OrderModified",
  "OrderCancelled",
  "PositionOpened",
  "PositionModified",
  "PositionPartiallyClosed",
  "PositionClosed",
]);

export function topicOf(event: EngineEvent): EventTopic {
  return isWarning(event) ? "warnings" : tradeKinds.has(event.kind ?? "") ? "trades" : "account";
}

/** An event, or a run of events in a row that say the same, such as the same refusal four times. */
export interface EventRun {
  event: EngineEvent;
  count: number;
  /** When the run started, the first event's time. */
  first: string;
}

/** The events with each run of events in a row that say the same told once, with how many times. Oldest first. */
export function collapseRepeats(events: readonly EngineEvent[], describe: (event: EngineEvent) => string): EventRun[] {
  const runs: EventRun[] = [];
  let lastText: string | null = null;
  for (const event of events) {
    const text = `${event.kind} ${describe(event)}`;
    const last = runs.at(-1);
    if (last && text === lastText) {
      last.count++;
      last.event = event;
    } else {
      runs.push({ event, count: 1, first: event.timestamp });
      lastText = text;
    }
  }

  return runs;
}

/** Ids are client-generated UUIDs; the start is enough to tell them apart on screen. */
export function shortId(id: string): string {
  return id.length > 8 ? id.slice(0, 8) : id;
}
