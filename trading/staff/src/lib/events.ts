import type { EngineEvent, EventEnvelope } from "./api/types";
import { formatLots, formatMoney, formatPrice, formatSignedMoney } from "./format";

/** Price decimals per symbol. Unknown symbols show their price as it came. */
export type DigitsOf = (symbol: string) => number | undefined;

/** How an event reads in the staff panel: what happened, on which account, the detail, and a tone when it is bad news. */
export type EventLine = {
  sequence: number;
  time: string;
  what: string;
  accountId: string | null;
  detail: string;
  tone: "normal" | "warning" | "loss";
};

const closeReasons: Record<string, string> = {
  Manual: "",
  StopLoss: ", stop loss",
  TakeProfit: ", take profit",
  StopOut: ", stop out",
  EquityFloor: ", loss limit",
  AccountClosed: ", account closed",
  OwnLimit: ", trader's own limit",
};

const inputNames: Record<string, string> = {
  PlaceOrder: "Order",
  ModifyOrder: "Order change",
  CancelOrder: "Cancel",
  ClosePosition: "Close",
  CloseAllPositions: "Close all",
  ModifyPosition: "Stop change",
  SetOwnLimits: "Own limits",
  LockTrading: "Lock",
  AdjustBalance: "Balance change",
  CreateAccount: "New account",
  ReopenAccount: "Reopen",
};

const rejectReasons: Record<string, string> = {
  StalePrice: "no fresh price",
  NoPrice: "no price yet",
  MarketClosed: "market closed",
  InsufficientMargin: "not enough margin",
  InsufficientFunds: "balance too low",
  AccountDisabled: "account closed",
  AccountSuspended: "account paused",
  AccountLocked: "locked for the day",
  TradeLimitReached: "trade limit reached",
  NoConversionRate: "no conversion rate",
};

/** One engine event as a line in a server's event list. */
export function describeEvent(envelope: EventEnvelope, digitsOf: DigitsOf): EventLine {
  const event = envelope.event;
  const price = (value: number, symbol: string) => {
    const digits = digitsOf(symbol);
    return digits === undefined ? String(value) : formatPrice(value, digits);
  };
  const line = (what: string, detail: string, tone: EventLine["tone"] = "normal"): EventLine => ({
    sequence: envelope.sequence,
    time: event.timestamp,
    what,
    accountId: accountOf(event),
    detail,
    tone,
  });

  switch (event.kind) {
    case "AccountCreated":
      return line("Account opened", `${formatMoney(event.balance)} ${event.currency}`);
    case "OrderPlaced":
      return line("Order placed", `${event.side} ${event.type.toLowerCase()} ${event.symbol}, ${formatLots(event.volume)} lot at ${price(event.price, event.symbol)}`);
    case "OrderModified":
      return line("Order moved", `${event.symbol} to ${price(event.price, event.symbol)}`);
    case "OrderCancelled":
      return line("Order cancelled", event.reason === "Manual" ? "By the trader" : event.reason);
    case "PositionOpened":
      return line("Position opened", `${event.symbol}, ${event.side.toLowerCase()} ${formatLots(event.volume)} lot at ${price(event.openPrice, event.symbol)}`);
    case "PositionModified":
      return line("Stops changed", `Stop loss ${event.stopLoss ?? "none"}, take profit ${event.takeProfit ?? "none"}`);
    case "PositionPartiallyClosed":
      return line(
        "Part closed",
        `${event.symbol}, ${formatLots(event.volume)} lot at ${price(event.closePrice, event.symbol)}, ${formatSignedMoney(event.profit)}${closeReasons[event.reason] ?? ""}`,
      );
    case "PositionClosed":
      return line(
        "Position closed",
        `${event.symbol}, ${formatLots(event.volume)} lot at ${price(event.closePrice, event.symbol)}, ${formatSignedMoney(event.profit)}${closeReasons[event.reason] ?? ""}`,
      );
    case "EquityFloorSet":
      return line("Loss limit set", `${event.floorId} at ${formatMoney(event.level)}`);
    case "EquityFloorRemoved":
      return line("Loss limit removed", event.floorId);
    case "EquityFloorBreached":
      return line("Loss limit broken", `${event.floorId} at ${formatMoney(event.level)}, equity ${formatMoney(event.equity)}`, "loss");
    case "StopOutTriggered":
      return line("Stop out", `Equity ${formatMoney(event.equity)}, margin level ${event.marginLevelPercent.toFixed(1)}%`, "loss");
    case "BalanceAdjusted":
      return line("Balance changed", `${formatSignedMoney(event.amount)}, now ${formatMoney(event.balanceAfter)}`);
    case "AccountSuspended":
      return line("Account paused", "");
    case "AccountResumed":
      return line("Account resumed", "");
    case "AccountDisabled":
      return line("Account closed", event.reason === "EquityFloor" ? "A loss limit was broken" : "By the firm", event.reason === "EquityFloor" ? "loss" : "normal");
    case "AccountReopened":
      return line("Account reopened", formatMoney(event.balance));
    case "InputRejected":
      return line("Refused", `${inputNames[event.input.kind ?? ""] ?? event.input.kind ?? "Request"}: ${rejectReasons[event.reason] ?? event.reason}`, "warning");
    case "OwnLimitReached":
      return line("Own limit reached", `${event.limit} at ${formatMoney(event.level)}`);
    case "TradingLocked":
      return line("Locked for the day", event.positionsClosed > 0 ? `${event.positionsClosed} positions closed` : "");
    case "TradingUnlocked":
      return line("Unlocked", "");
    default:
      return line(event.kind ?? "Event", "");
  }
}

/** The account an event belongs to, also the one a refused request was for. */
export function accountOf(event: EngineEvent): string | null {
  if ("accountId" in event && typeof event.accountId === "string") {
    return event.accountId;
  }

  if (event.kind === "InputRejected" && "accountId" in event.input && typeof event.input.accountId === "string") {
    return event.input.accountId;
  }

  return null;
}
