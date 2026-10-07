import type { BreachReport, BreachStep, EquityPoint, PriceGap, ReceiptClose, ReceiptFill, TradeReceipt } from "./api/types";
import { formatLots, formatMoney, formatPrice, formatSignedMoney } from "./format";

// The trading platform's proof (ADR 0053): the details of every trade, and a report for a broken loss limit, with the
// feed's prices behind them. The same as the terminal shows.

/** Close reasons from the trading platform, in words. An unknown one is shown as it is. */
export const closeReasonLabels: Record<string, string> = {
  Manual: "Manual",
  StopLoss: "Stop loss",
  TakeProfit: "Take profit",
  StopOut: "Stop out",
  EquityFloor: "Loss limit",
  AccountClosed: "Account closed",
  OwnLimit: "Trader's own limit",
};

/** The loss limits by their id on the trading platform. */
export function floorLabel(floorId: string): string {
  return floorId === "daily" ? "Daily loss limit" : floorId === "max-loss" ? "Max loss limit" : "Loss limit";
}

/** Whether a fill was at the ask: a buy opens and a sell closes there, the other two at the bid. */
export function filledAtAsk(side: string, opening: boolean): boolean {
  return (side === "Buy") === opening;
}

/** What the firm's markup was on the filled side, for example "2 points on the ask", or null when it is not known. */
export function markupText(fill: ReceiptFill, atAsk: boolean): string | null {
  if (fill.markupPoints === null) {
    return null;
  }

  return `${fill.markupPoints} ${fill.markupPoints === 1 ? "point" : "points"} on the ${atAsk ? "ask" : "bid"}`;
}

/**
 * Where a stop loss or take profit closed compared with its level, when the level is known. A stop closes at the first
 * price that reaches it, which after a jump in the price is past the level.
 */
export function stopText(close: ReceiptClose, side: string, digits: number): string | null {
  const { level, reason, fill } = close;
  if (level === null || (reason !== "StopLoss" && reason !== "TakeProfit")) {
    return null;
  }

  const name = reason === "StopLoss" ? "stop loss" : "take profit";
  const points = Math.round(Math.abs(fill.price - level) * 10 ** digits);
  if (points === 0) {
    return `The ${name} closed at its level, ${formatPrice(level)}.`;
  }

  // A buy closes at the bid and a sell at the ask. A stop loss is reached from above on a buy, a take profit from below.
  const price = side === "Buy" ? "bid" : "ask";
  const direction = (reason === "StopLoss") === (side === "Buy") ? "at or below" : "at or above";
  return `The ${name} was at ${formatPrice(level)}. The first ${price} ${direction} it was ${formatPrice(fill.price)}, and a ${name} closes at the price that reaches it, here ${points} ${points === 1 ? "point" : "points"} past the level.`;
}

/** The fills in order: the opening, then each part closed and the close, with what closed it. */
export function fillsOf(receipt: TradeReceipt): { fill: ReceiptFill; close: ReceiptClose | null }[] {
  return [{ fill: receipt.opened, close: null }, ...receipt.closes.map((close) => ({ fill: close.fill, close }))];
}

// The trading platform's requests and reasons for refusing them, in words.
const requestLabels: Record<string, string> = {
  PlaceOrder: "an order",
  ModifyOrder: "an order change",
  CancelOrder: "cancelling an order",
  ClosePosition: "a close",
  CloseAllPositions: "closing every position",
  ModifyPosition: "a stop change",
};

const refusalLabels: Record<string, string> = {
  StalePrice: "no fresh price, so nothing was filled at an old one",
  NoPrice: "no price yet",
  MarketClosed: "the market was closed",
  InsufficientMargin: "not enough margin",
  AccountDisabled: "trading on the account had ended",
  AccountSuspended: "the account was paused",
};

/** What happened in a step before and at a breach, in words. */
export function stepText(step: BreachStep, currency: string): string {
  const what = `${step.side ?? ""} ${step.volume === null ? "" : formatLots(step.volume)} ${step.symbol ?? ""}`.replace(/\s+/g, " ").trim();
  switch (step.kind) {
    case "PositionOpened":
      return `${step.side === "Sell" ? "Sold" : "Bought"} ${step.volume === null ? "" : `${formatLots(step.volume)} `}${step.symbol ?? ""} at ${formatPrice(step.price ?? 0)}.`;
    case "PositionClosed":
    case "PositionPartiallyClosed": {
      const part = step.kind === "PositionPartiallyClosed" ? "Part of " : "";
      const reason = step.reason && step.reason !== "Manual" ? ` (${(closeReasonLabels[step.reason] ?? step.reason).toLowerCase()})` : "";
      return `${part}${what} closed at ${formatPrice(step.price ?? 0)}${reason}, ${formatSignedMoney(step.amount)} ${currency}.`;
    }
    case "OrderPlaced":
      return `${step.reason ?? "An"} order to ${step.side === "Sell" ? "sell" : "buy"} ${step.volume === null ? "" : `${formatLots(step.volume)} `}${step.symbol ?? ""} at ${formatPrice(step.price ?? 0)}.`;
    case "InputRejected": {
      const request = requestLabels[step.request ?? ""] ?? "a request";
      const reason = refusalLabels[step.reason ?? ""] ?? step.reason ?? "refused";
      return `${capitalize(request)}${step.symbol ? ` on ${step.symbol}` : ""} was refused: ${reason}.`;
    }
    case "BalanceAdjusted":
      return `${formatSignedMoney(step.amount)} ${currency} was ${(step.amount ?? 0) >= 0 ? "put on" : "taken off"} the account.`;
    case "EquityFloorSet":
      return `The ${floorLabel(step.floorId ?? "").toLowerCase()} was set at ${formatMoney(step.level)}.`;
    case "EquityFloorBreached":
      return `Equity ${formatMoney(step.amount)} fell below the ${floorLabel(step.floorId ?? "").toLowerCase()} at ${formatMoney(step.level)}.`;
    case "StopOutTriggered":
      return `Margin ran out at equity ${formatMoney(step.amount)}, so the biggest loss was closed.`;
    case "AccountDisabled":
      return "Trading on the account ended.";
    default:
      return step.kind;
  }
}

/** How a step is marked: the breach in red, a refusal in amber, the rest quietly. */
export function stepTone(step: BreachStep): "loss" | "warning" | "muted" {
  return step.kind === "EquityFloorBreached" || step.kind === "AccountDisabled" ? "loss" : step.kind === "InputRejected" ? "warning" : "muted";
}

/** Below the limit by: how far equity was under it. */
export function belowBy(report: BreachReport): number {
  return Math.max(0, report.level - report.equity);
}

export interface Box {
  width: number;
  height: number;
  /** Room kept free above and below the values. */
  padding: number;
}

// Maps times and values onto the box. Equal values get the middle.
function scale(times: readonly number[], values: readonly number[], box: Box) {
  const [t0, t1] = [Math.min(...times), Math.max(...times)];
  const [v0, v1] = [Math.min(...values), Math.max(...values)];
  return {
    x: (t: number) => (t1 === t0 ? box.width / 2 : ((t - t0) / (t1 - t0)) * box.width),
    y: (v: number) => (v1 === v0 ? box.height / 2 : box.padding + (1 - (v - v0) / (v1 - v0)) * (box.height - 2 * box.padding)),
  };
}

const point = (x: number, y: number) => `${x.toFixed(1)},${y.toFixed(1)}`;

/** Equity over time, the loss limit as a line, the periods without prices and the breach at the end. */
export function equityChart(points: readonly EquityPoint[], level: number, gaps: readonly PriceGap[], box: Box) {
  if (points.length === 0) {
    return { line: "", levelY: box.height / 2, gaps: [], breach: null };
  }

  const times = points.map((p) => Date.parse(p.time));
  const { x, y } = scale(times, [...points.map((p) => p.equity), level], box);
  const last = points[points.length - 1];
  return {
    line: points.map((p) => point(x(Date.parse(p.time)), y(p.equity))).join(" "),
    levelY: y(level),
    gaps: gaps.map((g) => {
      const from = x(Math.max(Date.parse(g.from), times[0]));
      return { x: from, width: Math.max(1, x(Date.parse(g.to)) - from) };
    }),
    breach: { x: x(Date.parse(last.time)), y: y(last.equity) },
  };
}

function capitalize(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** The most decimals among the prices, so a row of prices shows them alike, for example 0.65990 beside 0.65994. */
export function priceDigits(prices: readonly (number | null | undefined)[]): number {
  return Math.max(0, ...prices.map((p) => (p == null ? 0 : (String(p).split(".")[1] ?? "").length)));
}

/** A price with the instrument's decimals, for example "0.65990" or "2,641.30". */
export function priceText(price: number, digits: number): string {
  return price.toLocaleString("en-US", { minimumFractionDigits: digits, maximumFractionDigits: digits });
}
