"use client";

import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";

// The box keeps this far from the window's edges.
const edge = 8;

/**
 * A small box by an element, such as the stops of a position by its row, that floats over the page so a table that
 * scrolls never cuts it off. It opens above the element when there is room, since the tables sit at the bottom of the
 * screen, and closes on Esc or a press outside it.
 */
export function Popover({
  anchor,
  label,
  onClose,
  children,
  className = "w-80",
}: {
  anchor: HTMLElement | null;
  label: string;
  onClose: () => void;
  children: React.ReactNode;
  className?: string;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const [place, setPlace] = useState<{ left: number; top: number } | null>(null);

  useLayoutEffect(() => {
    const box = ref.current;
    if (!anchor || !box) {
      return;
    }

    const at = anchor.getBoundingClientRect();
    const size = box.getBoundingClientRect();
    const above = at.top - 6 - size.height >= edge;
    setPlace({
      left: Math.min(Math.max(edge, at.right - size.width), window.innerWidth - size.width - edge),
      top: above ? at.top - 6 - size.height : Math.min(at.bottom + 6, window.innerHeight - size.height - edge),
    });
  }, [anchor]);

  // The first field takes the focus once the box is placed.
  useEffect(() => {
    if (place) {
      ref.current?.querySelector<HTMLElement>("input, select, button")?.focus();
    }
  }, [place]);

  useEffect(() => {
    const onPointerDown = (e: PointerEvent) => {
      const target = e.target as Node;
      // A menu opened from the box floats outside it, and a press in it belongs to the box.
      if (!ref.current?.contains(target) && !anchor?.contains(target) && !(target instanceof Element && target.closest('[role="menu"]'))) {
        onClose();
      }
    };
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onClose();
      }
    };
    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    window.addEventListener("resize", onClose);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("resize", onClose);
    };
  }, [anchor, onClose]);

  return createPortal(
    <div
      ref={ref}
      role="dialog"
      aria-label={label}
      style={place ?? { left: 0, top: 0, visibility: "hidden" }}
      className={`fixed z-50 flex flex-col gap-3 rounded-xl border border-border bg-panel p-3.5 text-sm shadow-float ${place ? "animate-pop" : ""} ${className}`}
    >
      {children}
    </div>,
    document.body,
  );
}
