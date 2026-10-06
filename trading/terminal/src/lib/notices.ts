import type { EngineEvent, EventEnvelope } from "./api/types";
import { closedBecause, netResult, partResult, type DigitsOf } from "./events";
import { formatPrice, formatVolume } from "./format";

/** A short note about a trade that comes and goes in a corner: an order filled, a pending order placed, or a close. */
export interface TradeNotice {
  /** The same trade always gets the same id. */
  id: string;
  kind: "filled" | "placed" | "closed";
  title: string;
  /** For a close, the result after commission, as in the history. */
  result?: number;
}

/**
 * The note for an event, for example "Bought 1.00 EURUSD at 1.08724", or null for events that need none. A close
 * also comes from a stop loss, a take profit or a loss limit, so the trader sees it wherever they are looking.
 */
export function tradeNotice(event: EngineEvent, digitsOf: DigitsOf, events: readonly EngineEvent[]): TradeNotice | null {
  switch (event.kind) {
    case "PositionOpened":
      return {
        id: `filled-${event.positionId}`,
        kind: "filled",
        title: `${event.side === "Buy" ? "Bought" : "Sold"} ${formatVolume(event.volume)} ${event.symbol} at ${formatPrice(event.openPrice, digitsOf(event.symbol))}`,
      };
    case "OrderPlaced":
      return {
        id: `placed-${event.orderId}`,
        kind: "placed",
        title: `${event.side} ${event.type.toLowerCase()} ${formatVolume(event.volume)} ${event.symbol} placed at ${formatPrice(event.price, digitsOf(event.symbol))}`,
      };
    case "OrderModified":
      return {
        id: `modified-${event.orderId}-${event.timestamp}`,
        kind: "placed",
        title: `${event.symbol} order moved to ${formatPrice(event.price, digitsOf(event.symbol))}`,
      };
    case "PositionPartiallyClosed":
      return {
        id: `part-${event.positionId}-${event.timestamp}`,
        kind: "closed",
        title: `${formatVolume(event.volume)} of ${event.side} ${event.symbol} closed at ${formatPrice(event.closePrice, digitsOf(event.symbol))}, ${formatVolume(event.remainingVolume)} still open`,
        result: partResult(event),
      };
    case "PositionClosed":
      return {
        id: `closed-${event.positionId}`,
        kind: "closed",
        title: `${event.side} ${formatVolume(event.volume)} ${event.symbol} closed at ${formatPrice(event.closePrice, digitsOf(event.symbol))}${closedBecause[event.reason]}`,
        result: netResult(event, events),
      };
    default:
      return null;
  }
}

type Listener = (events: readonly EventEnvelope[]) => void;

/** Passes on each new event once, however many ways it arrives. */
export interface NewEvents {
  announce(events: readonly EventEnvelope[]): void;
  /** Returns how to stop listening. */
  subscribe(listener: Listener): () => void;
  /** Forgets what was passed on, for another account whose sequence numbers start over. */
  reset(): void;
}

/**
 * Remembers the latest sequence numbers it passed on, up to the limit. Events loaded when the terminal starts or
 * catches up after a reconnect are history and never announced.
 */
export function createNewEvents(limit: number): NewEvents {
  const listeners = new Set<Listener>();
  let seen = new Set<number>();

  return {
    announce(events) {
      const fresh = events.filter((e) => !seen.has(e.sequence));
      for (const e of fresh) {
        seen.add(e.sequence);
      }

      // A Set keeps its order, so the first ones are the oldest.
      for (const sequence of seen) {
        if (seen.size <= limit) {
          break;
        }

        seen.delete(sequence);
      }

      if (fresh.length > 0) {
        listeners.forEach((listener) => listener(fresh));
      }
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    reset() {
      seen = new Set();
    },
  };
}

/**
 * The account's new events, as they happen: pushed in realtime, or returned by a command the trader sent. A command's
 * events arrive both ways, in either order, and are passed on once.
 */
export const newEvents = createNewEvents(1_000);
