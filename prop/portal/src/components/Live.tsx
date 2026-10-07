"use client";

import NumberFlow from "@number-flow/react";

import type { FloorFigure } from "@/lib/api/types";
import { floorState, roomShare, type FloorState } from "@/lib/dashboard";
import { formatMoney } from "@/lib/format";

// Figures that move: amounts that roll to their new value when they change, and how much room is left to a loss limit.
// Rolling stands still for those who asked their system for less motion.

const money = { minimumFractionDigits: 2, maximumFractionDigits: 2 } as const;

/** An amount, such as equity, that rolls to its new value when it changes. "-" when it is not known. */
export function AnimatedMoney({ value, signed = false, className = "" }: { value: number | null | undefined; signed?: boolean; className?: string }) {
  if (value == null) {
    return <span className={className}>-</span>;
  }

  return (
    <NumberFlow
      value={value}
      locales="en-US"
      format={{ ...money, signDisplay: signed ? "exceptZero" : "auto" }}
      respectMotionPreference
      className={className}
    />
  );
}

const roomBars: Record<FloorState, string> = { ok: "bg-profit", warning: "bg-warning", danger: "bg-loss" };
const roomTexts: Record<FloorState, string> = { ok: "text-foreground", warning: "text-warning", danger: "text-loss" };

/**
 * A bar for the room left before a loss limit, that shrinks as equity falls toward it: green while there is plenty,
 * amber under a quarter and red under a tenth, as in the terminal.
 */
export function RoomBar({ share, state, label }: { share: number; state: FloorState; label: string }) {
  return (
    <div
      role="meter"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(share)}
      className="h-1.5 overflow-hidden rounded-full bg-border"
    >
      <div
        className={`h-full origin-left animate-grow-x rounded-full transition-[width,background-color] duration-700 ease-out-soft ${roomBars[state]}`}
        style={{ width: `${share}%` }}
      />
    </div>
  );
}

/** How much room is left before a loss limit, as an amount that rolls and a bar under it. */
export function RoomMeter({ label, floor }: { label: string; floor: FloorFigure | null }) {
  if (!floor) {
    return (
      <div className="flex flex-col gap-1.5">
        <span className="text-xs text-muted">{label}</span>
        <span className="text-sm text-muted">-</span>
      </div>
    );
  }

  const state = floorState(floor);
  const left = Math.max(floor.headroom, 0);
  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-baseline justify-between gap-2 whitespace-nowrap">
        <span className="text-xs text-muted">{label}</span>
        <span className={`text-sm font-medium ${roomTexts[state]}`}>
          <AnimatedMoney value={left} /> <span className="text-xs font-normal text-muted">left</span>
        </span>
      </div>
      <RoomBar share={roomShare(floor)} state={state} label={`${label}: ${formatMoney(left)} left`} />
    </div>
  );
}
