import type { EngineEvent } from "./api/types";
import { formatMoney, formatPercent, formatPrice, formatVolume } from "./format";

/** Price decimals for a symbol. Falls back to 5 for symbols the terminal does not know. */
export type DigitsOf = (symbol: string) => number;

/** One line of text describing an engine event for the trader. */
export function describeEvent(event: EngineEvent, digitsOf: DigitsOf): string {
  switch (event.kind) {
    case "AccountCreated":
      return `Account created with ${formatMoney(event.balance)} ${event.currency}`;
    case "OrderPlaced":
      return `Placed ${event.side} ${event.type} ${formatVolume(event.volume)} ${event.symbol} at ${formatPrice(event.price, digitsOf(event.symbol))}`;
    case "OrderCancelled":
      return `Cancelled order ${event.orderId} (${event.reason})`;
    case "PositionOpened":
      return `Opened ${event.side} ${formatVolume(event.volume)} ${event.symbol} at ${formatPrice(event.openPrice, digitsOf(event.symbol))}`;
    case "PositionModified":
      return `Changed stops on ${event.positionId}: SL ${event.stopLoss ?? "-"}, TP ${event.takeProfit ?? "-"}`;
    case "PositionClosed":
      return `Closed ${event.side} ${formatVolume(event.volume)} ${event.symbol} at ${formatPrice(event.closePrice, digitsOf(event.symbol))} (${event.reason}), profit ${formatMoney(event.profit)}`;
    case "EquityFloorSet":
      return `Floor ${event.floorId} set at ${formatMoney(event.level)}`;
    case "EquityFloorRemoved":
      return `Floor ${event.floorId} removed`;
    case "EquityFloorBreached":
      return `Floor ${event.floorId} breached: equity ${formatMoney(event.equity)} fell below ${formatMoney(event.level)}`;
    case "StopOutTriggered":
      return `Stop out at margin level ${formatPercent(event.marginLevelPercent)}`;
    case "AccountDisabled":
      return `Account disabled (${event.reason})`;
    case "AccountSuspended":
      return "Trading paused: no new orders until it is resumed";
    case "AccountResumed":
      return "Trading resumed";
    case "BalanceAdjusted":
      return `${balanceOperationName(event.amount)} of ${formatMoney(Math.abs(event.amount))}, balance ${formatMoney(event.balanceAfter)}`;
    case "InputRejected":
      return `Rejected ${event.input.kind ?? "input"}: ${event.reason}`;
    default:
      return "Unknown event";
  }
}

/** Money added to or taken from the account, for example a payout. Not a trading result. */
export function balanceOperationName(amount: number): "Deposit" | "Withdrawal" {
  return amount >= 0 ? "Deposit" : "Withdrawal";
}

/** Events that need the trader's attention. */
export function isWarning(event: EngineEvent): boolean {
  return (
    event.kind === "InputRejected" ||
    event.kind === "EquityFloorBreached" ||
    event.kind === "StopOutTriggered" ||
    event.kind === "AccountDisabled" ||
    event.kind === "AccountSuspended"
  );
}
