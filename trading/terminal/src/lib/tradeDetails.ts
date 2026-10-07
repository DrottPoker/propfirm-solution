import type { ReceiptClose, ReceiptFill, Side, TradeReceipt } from "./api/types";
import { closeReasons } from "./events";
import { formatDateTime, formatMoney, formatPrice, formatSignedMoney, formatVolume } from "./format";

// A trade's details (ADR 0053): what the account got at each fill, the feed's price behind it and the firm's markup.

/** Whether a fill was at the ask: a buy opens and a sell closes there, the other two at the bid. */
export function filledAtAsk(side: Side, opening: boolean): boolean {
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
export function stopText(close: ReceiptClose, side: Side, digits: number): string | null {
  const { level, reason, fill } = close;
  if (level === null || (reason !== "StopLoss" && reason !== "TakeProfit")) {
    return null;
  }

  const name = reason === "StopLoss" ? "stop loss" : "take profit";
  const points = Math.round(Math.abs(fill.price - level) * 10 ** digits);
  if (points === 0) {
    return `The ${name} closed at its level, ${formatPrice(level, digits)}.`;
  }

  // A buy closes at the bid and a sell at the ask. A stop loss is reached from above on a buy, a take profit from below.
  const price = side === "Buy" ? "bid" : "ask";
  const direction = (reason === "StopLoss") === (side === "Buy") ? "at or below" : "at or above";
  return `The ${name} was at ${formatPrice(level, digits)}. The first ${price} ${direction} it was ${formatPrice(fill.price, digits)}, and a ${name} closes at the price that reaches it, here ${points} ${points === 1 ? "point" : "points"} past the level.`;
}

/** The fills in order: the opening, then each part closed and the close, with what closed it. */
export function fillsOf(receipt: TradeReceipt): { fill: ReceiptFill; close: ReceiptClose | null }[] {
  return [{ fill: receipt.opened, close: null }, ...receipt.closes.map((close) => ({ fill: close.fill, close }))];
}

/** The trade's details in plain text, to paste into a message to the firm. */
export function detailsText(receipt: TradeReceipt, timeZone: string): string {
  const { digits, currency } = receipt;
  const lines = [
    `Trade details: ${receipt.side} ${formatVolume(receipt.opened.volume)} ${receipt.symbol}, position ${receipt.positionId}, account ${receipt.accountId}`,
  ];
  if (receipt.order) {
    lines.push(`${receipt.order.type} order placed ${formatDateTime(receipt.order.placedAt, timeZone)} at ${formatPrice(receipt.order.price, digits)}`);
  }

  for (const { fill, close } of fillsOf(receipt)) {
    const opening = close === null;
    const what = close === null ? "Opened" : `Closed (${closeReasons[close.reason].toLowerCase()})`;
    lines.push(`${what} ${formatDateTime(fill.at, timeZone)}: ${formatVolume(fill.volume)} at ${formatPrice(fill.price, digits)}, commission ${formatMoney(fill.commission)} ${currency}`);
    if (fill.feed) {
      const markup = markupText(fill, filledAtAsk(receipt.side, opening));
      lines.push(`  Feed price bid ${formatPrice(fill.feed.bid, digits)} ask ${formatPrice(fill.feed.ask, digits)}, journal entry ${fill.feed.inputSequence}${markup ? `, markup ${markup}` : ""}`);
    }
  }

  if (receipt.result !== null) {
    lines.push(`Result ${formatSignedMoney(receipt.result)} ${currency} after ${formatMoney(receipt.commission)} commission`);
  }

  return lines.join("\n");
}
