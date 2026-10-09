"use client";

import { useRef } from "react";

/**
 * The edge between two parts of the terminal, dragged to make one larger. A thin line with a wider grip around it,
 * brass while held. The arrow keys move it in steps, and a double click or Enter puts it back where it started out.
 * <paramref name="direction"/> says which way a larger value grows from the edge: 1 when the part is before the edge
 * (left or above it), -1 when it is after.
 */
export function Splitter({
  orientation,
  label,
  value,
  min,
  max,
  direction,
  defaultValue,
  onResize,
  onDone,
}: {
  /** "vertical" for an edge between parts side by side, "horizontal" for one between parts above each other. */
  orientation: "vertical" | "horizontal";
  label: string;
  value: number;
  min: number;
  max: number;
  direction: 1 | -1;
  defaultValue: number;
  onResize: (value: number) => void;
  onDone: () => void;
}) {
  const start = useRef<{ pointer: number; value: number; id: number } | null>(null);
  const vertical = orientation === "vertical";
  const clamp = (next: number) => Math.min(max, Math.max(min, next));

  return (
    <div
      role="separator"
      aria-orientation={orientation}
      aria-label={label}
      aria-valuenow={Math.round(value)}
      aria-valuemin={min}
      aria-valuemax={max}
      tabIndex={0}
      title={`${label}. Double-click to reset.`}
      onPointerDown={(e) => {
        if (e.button !== 0) {
          return;
        }

        e.preventDefault();
        e.currentTarget.setPointerCapture(e.pointerId);
        start.current = { pointer: vertical ? e.clientX : e.clientY, value, id: e.pointerId };
      }}
      onPointerMove={(e) => {
        const drag = start.current;
        if (drag && drag.id === e.pointerId) {
          onResize(clamp(drag.value + direction * ((vertical ? e.clientX : e.clientY) - drag.pointer)));
        }
      }}
      onPointerUp={(e) => {
        if (start.current?.id === e.pointerId) {
          start.current = null;
          onDone();
        }
      }}
      onPointerCancel={() => {
        start.current = null;
        onDone();
      }}
      onDoubleClick={() => {
        onResize(clamp(defaultValue));
        onDone();
      }}
      onKeyDown={(e) => {
        const step = e.shiftKey ? 64 : 16;
        const keys: Record<string, number> = vertical ? { ArrowLeft: -direction, ArrowRight: direction } : { ArrowUp: -direction, ArrowDown: direction };
        if (e.key in keys) {
          e.preventDefault();
          onResize(clamp(value + keys[e.key] * step));
          onDone();
        } else if (e.key === "Enter") {
          e.preventDefault();
          onResize(clamp(defaultValue));
          onDone();
        }
      }}
      className={`group relative z-10 shrink-0 bg-border outline-none ${vertical ? "w-px cursor-col-resize" : "h-px cursor-row-resize"}`}
    >
      {/* A grip wider than the line, so it is easy to catch, and brass while held, hovered or focused. */}
      <span
        aria-hidden="true"
        className={`absolute transition-colors duration-150 group-hover:bg-accent/40 group-focus-visible:bg-accent group-active:bg-accent ${vertical ? "inset-y-0 -left-[3px] w-[7px]" : "inset-x-0 -top-[3px] h-[7px]"}`}
      />
    </div>
  );
}
