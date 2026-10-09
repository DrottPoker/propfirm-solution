"use client";

import { useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";

// How long the pointer rests before the tip shows, so it does not flicker as the mouse passes.
const showDelayMs = 350;

// The tip keeps this far from the window's edges.
const edge = 8;

/**
 * An explanation that shows on hover, on keyboard focus and on a tap, unlike the browser's own title, which shows late,
 * looks foreign and never on a touch screen (ADR 0058). The child is what is explained; the tip names it for screen
 * readers too. The tip floats over the page, so a bar that scrolls sideways never cuts it off.
 */
export function Tooltip({
  content,
  children,
  side = "below",
  align = "start",
  focusable = false,
  className = "",
}: {
  content: React.ReactNode;
  children: React.ReactNode;
  side?: "below" | "above";
  align?: "start" | "end" | "center";
  /** Whether the tip itself takes the keyboard focus, for a child that cannot, such as a label with an icon. */
  focusable?: boolean;
  className?: string;
}) {
  const [open, setOpen] = useState(false);
  const [place, setPlace] = useState<{ left: number; top: number } | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const anchorRef = useRef<HTMLSpanElement>(null);
  const tipRef = useRef<HTMLSpanElement>(null);
  const id = useId();
  useEffect(() => () => clearTimeout(timer.current), []);

  // Placed by the child once the tip's size is known, flipped and kept inside the window.
  useLayoutEffect(() => {
    const anchor = anchorRef.current;
    const tip = tipRef.current;
    if (!open || !anchor || !tip) {
      return;
    }

    const at = anchor.getBoundingClientRect();
    const size = tip.getBoundingClientRect();
    const fitsBelow = at.bottom + 6 + size.height <= window.innerHeight - edge;
    const fitsAbove = at.top - 6 - size.height >= edge;
    const below = side === "below" ? fitsBelow || !fitsAbove : !fitsAbove;
    const left = align === "start" ? at.left : align === "end" ? at.right - size.width : at.left + at.width / 2 - size.width / 2;
    setPlace({
      left: Math.min(Math.max(edge, left), window.innerWidth - size.width - edge),
      top: below ? at.bottom + 6 : at.top - 6 - size.height,
    });
  }, [open, side, align]);

  const show = (delay: number) => {
    clearTimeout(timer.current);
    timer.current = setTimeout(() => setOpen(true), delay);
  };
  const hide = () => {
    clearTimeout(timer.current);
    setOpen(false);
    setPlace(null);
  };

  return (
    <span
      ref={anchorRef}
      className={`inline-flex ${className}`}
      aria-describedby={open ? id : undefined}
      tabIndex={focusable ? 0 : undefined}
      onPointerEnter={(e) => e.pointerType === "mouse" && show(showDelayMs)}
      onPointerLeave={(e) => e.pointerType === "mouse" && hide()}
      onPointerDown={(e) => e.pointerType !== "mouse" && (open ? hide() : show(0))}
      onFocus={() => show(0)}
      onBlur={hide}
      onKeyDown={(e) => e.key === "Escape" && hide()}
    >
      {children}
      {open &&
        createPortal(
          <span
            ref={tipRef}
            role="tooltip"
            id={id}
            style={place ?? { left: 0, top: 0, visibility: "hidden" }}
            className={`pointer-events-none fixed z-50 w-max max-w-72 rounded-md border border-border bg-raised px-2.5 py-1.5 text-left text-xs leading-relaxed font-normal whitespace-normal text-foreground normal-case shadow-float ${place ? "animate-fade" : ""}`}
          >
            {content}
          </span>,
          document.body,
        )}
    </span>
  );
}
