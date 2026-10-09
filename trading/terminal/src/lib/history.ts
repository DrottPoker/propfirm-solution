import type { EngineEvent } from "./api/types";
import { closeReasons, netResult, partResult, positionCommission, type DigitsOf } from "./events";
import { dayKey, formatDateTime } from "./format";

// The history of closed trades and money in and out (ADR 0058): which rows a range shows, what they add up to, and the
// same rows as a CSV file for the trader's own records.

export type ClosedPosition = Extract<EngineEvent, { kind?: "PositionClosed" }>;
export type ClosedPart = Extract<EngineEvent, { kind?: "PositionPartiallyClosed" }>;
export type BalanceOperation = Extract<EngineEvent, { kind?: "BalanceAdjusted" }>;
export type HistoryRow = ClosedPosition | ClosedPart | BalanceOperation;

// The generated kind is optional, so a plain comparison does not narrow the other branch.
export const isBalanceOperation = (e: HistoryRow): e is BalanceOperation => e.kind === "BalanceAdjusted";
export const isPart = (e: HistoryRow): e is ClosedPart => e.kind === "PositionPartiallyClosed";

/** How far back the history shows: today, this week from Monday, or everything the terminal has. */
export type HistoryRange = "today" | "week" | "all";

export const historyRanges: { value: HistoryRange; label: string }[] = [
  { value: "today", label: "Today" },
  { value: "week", label: "This week" },
  { value: "all", label: "All" },
];

/** The closes and money in and out among the events, newest first. */
export function historyRows(events: readonly EngineEvent[]): HistoryRow[] {
  return events.filter((e): e is HistoryRow => e.kind === "PositionClosed" || e.kind === "PositionPartiallyClosed" || e.kind === "BalanceAdjusted").reverse();
}

/** The first day of the range in the time zone, as 2026-10-05, or null for everything. A week starts on Monday. */
export function rangeStart(range: HistoryRange, timeZone: string, now: Date): string | null {
  if (range === "all") {
    return null;
  }

  const today = dayKey(now, timeZone);
  if (range === "today") {
    return today;
  }

  const date = new Date(`${today}T00:00:00Z`);
  const sinceMonday = (date.getUTCDay() + 6) % 7;
  return new Date(date.getTime() - sinceMonday * 86_400_000).toISOString().slice(0, 10);
}

/** The rows within the range. */
export function inRange<T extends { timestamp: string }>(rows: readonly T[], range: HistoryRange, timeZone: string, now: Date): T[] {
  const start = rangeStart(range, timeZone, now);
  return start === null ? [...rows] : rows.filter((row) => dayKey(row.timestamp, timeZone) >= start);
}

/** What a close made after commission. */
export function rowResult(row: ClosedPosition | ClosedPart, events: readonly EngineEvent[]): number {
  return isPart(row) ? partResult(row) : netResult(row, events);
}

/** How many positions closed and what the closes made after commission. Money in and out is not a result. */
export function historySummary(rows: readonly HistoryRow[], events: readonly EngineEvent[]): { trades: number; result: number } {
  let trades = 0;
  let result = 0;
  for (const row of rows) {
    if (isBalanceOperation(row)) {
      continue;
    }

    if (!isPart(row)) {
      trades++;
    }

    result += rowResult(row, events);
  }

  return { trades, result: Math.round(result * 100) / 100 };
}

// A field with a comma, a quote or a line break is quoted, with its quotes doubled.
function csvField(value: string | number | null): string {
  const text = value === null ? "" : String(value);
  return /[",\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

/**
 * The rows as CSV, oldest first, with times in the time zone, prices at the instrument's decimals and amounts in the
 * account currency. Money in and out is told by its name, such as a payout.
 */
export function historyCsv(
  rows: readonly HistoryRow[],
  events: readonly EngineEvent[],
  digitsOf: DigitsOf,
  timeZone: string,
  names: { deposit: string; withdrawal: string },
): string {
  const header = ["Time", "Type", "Symbol", "Side", "Volume", "Open price", "Close price", "Reason", "Commission", "Result", "Balance after", "Position"];
  const lines = [...rows].reverse().map((row) => {
    const time = formatDateTime(row.timestamp, timeZone);
    if (isBalanceOperation(row)) {
      const name = row.amount >= 0 ? names.deposit : names.withdrawal;
      return [time, name, null, null, null, null, null, null, null, row.amount.toFixed(2), row.balanceAfter.toFixed(2), null];
    }

    const digits = digitsOf(row.symbol);
    const commission = isPart(row) ? row.commission : positionCommission(row, events);
    return [
      time,
      isPart(row) ? "Part closed" : "Closed",
      row.symbol,
      row.side,
      row.volume.toFixed(2),
      row.openPrice.toFixed(digits),
      row.closePrice.toFixed(digits),
      isPart(row) ? "Manual" : closeReasons[row.reason],
      commission.toFixed(2),
      rowResult(row, events).toFixed(2),
      row.balanceAfter.toFixed(2),
      row.positionId,
    ];
  });

  return [header, ...lines].map((line) => line.map(csvField).join(",")).join("\r\n") + "\r\n";
}
