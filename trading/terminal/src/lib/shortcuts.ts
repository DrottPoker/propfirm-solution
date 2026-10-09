import { timeframes } from "./candles";

// The terminal's keyboard shortcuts (ADR 0058). None of them places an order, so a key pressed by mistake costs nothing.

export interface Shortcut {
  keys: string[];
  what: string;
}

export const shortcuts: readonly Shortcut[] = [
  { keys: ["/"], what: "Search the watchlist" },
  { keys: ["1", "9"], what: `Timeframe ${timeframes[0]} to ${timeframes[timeframes.length - 1]}, in order` },
  { keys: ["Alt", "R"], what: "Reset the chart" },
  { keys: ["Delete"], what: "Remove the selected drawing" },
  { keys: ["Esc"], what: "Cancel, close or stop drawing" },
  { keys: ["?"], what: "Show these shortcuts" },
];

/** Whether a key press belongs to a field or a button being typed in, which a shortcut must leave alone. */
export function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }

  return target.isContentEditable || target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.tagName === "SELECT";
}

/** The timeframe a digit key chooses, or null: 1 is the first timeframe. */
export function timeframeOfKey(key: string): (typeof timeframes)[number] | null {
  const n = /^[1-9]$/.test(key) ? Number(key) : 0;
  return n >= 1 && n <= timeframes.length ? timeframes[n - 1] : null;
}
