"use client";

import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";

export interface MenuItem<T extends string> {
  value: T;
  label: string;
  /** A second line or a shortcut, shown dimmer. */
  hint?: string;
  icon?: React.ReactNode;
  disabled?: boolean;
  /** An action among choices, such as "Hide the watchlist", which is never the chosen one. */
  action?: boolean;
}

// The list keeps this far from the window's edges.
const edge = 8;

/**
 * A button that opens a short list to choose from, such as the chart type or a ready-made layout. The chosen item is
 * marked, the arrow keys move between items, and Esc, a press outside, scrolling or resizing closes the list. The list
 * floats over the whole page, so a toolbar or a status bar that scrolls sideways never cuts it off.
 */
export function Menu<T extends string>({
  label,
  button,
  items,
  value,
  onChoose,
  align = "start",
  side = "below",
  className = "",
  title,
  buttonLabel,
}: {
  /** The list's name for screen readers, such as "Chart type". */
  label: string;
  /** What the button shows. */
  button: React.ReactNode;
  items: readonly MenuItem<T>[];
  value?: T | null;
  onChoose: (value: T) => void;
  align?: "start" | "end";
  side?: "below" | "above";
  className?: string;
  title?: string;
  /** The button's name for screen readers when what it shows is not enough, such as an icon. */
  buttonLabel?: string;
}) {
  const [open, setOpen] = useState(false);
  const [place, setPlace] = useState<{ left: number; top: number } | null>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const id = useId();
  const close = useCallback(() => {
    setOpen(false);
    setPlace(null);
  }, []);

  // Placed by the button once the list's size is known, flipped to stay inside the window.
  useLayoutEffect(() => {
    const button = buttonRef.current;
    const list = listRef.current;
    if (!open || !button || !list) {
      return;
    }

    const anchor = button.getBoundingClientRect();
    const size = list.getBoundingClientRect();
    const fitsBelow = anchor.bottom + 4 + size.height <= window.innerHeight - edge;
    const fitsAbove = anchor.top - 4 - size.height >= edge;
    const below = side === "below" ? fitsBelow || !fitsAbove : !fitsAbove && fitsBelow;
    const left = align === "end" ? anchor.right - size.width : anchor.left;
    setPlace({
      left: Math.min(Math.max(edge, left), window.innerWidth - size.width - edge),
      top: below ? anchor.bottom + 4 : anchor.top - 4 - size.height,
    });
  }, [open, side, align]);

  // The chosen item, or the first, takes the focus once the list is placed.
  useEffect(() => {
    if (!place) {
      return;
    }

    const list = listRef.current;
    const chosen = list?.querySelector<HTMLButtonElement>('[aria-checked="true"]') ?? list?.querySelector<HTMLButtonElement>("button:not(:disabled)");
    chosen?.focus();
  }, [place]);

  useEffect(() => {
    if (!open) {
      return;
    }

    const onPointerDown = (e: PointerEvent) => {
      const target = e.target as Node;
      if (!listRef.current?.contains(target) && !buttonRef.current?.contains(target)) {
        close();
      }
    };
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        close();
        buttonRef.current?.focus();
      }
    };
    // The list would stay where the button was, so a scroll that moves the button closes it. Other scrolls, such as
    // a live list beside it or the page bringing the button into view as it was pressed, leave it open.
    const opened = buttonRef.current?.getBoundingClientRect();
    const onScroll = (e: Event) => {
      const now = buttonRef.current?.getBoundingClientRect();
      const moved = !opened || !now || Math.abs(now.top - opened.top) > 1 || Math.abs(now.left - opened.left) > 1;
      if (moved && !listRef.current?.contains(e.target as Node)) {
        close();
      }
    };
    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    window.addEventListener("resize", close);
    window.addEventListener("scroll", onScroll, true);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("resize", close);
      window.removeEventListener("scroll", onScroll, true);
    };
  }, [open, close]);

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key !== "ArrowDown" && e.key !== "ArrowUp") {
      return;
    }

    e.preventDefault();
    const buttons = [...(listRef.current?.querySelectorAll<HTMLButtonElement>("button:not(:disabled)") ?? [])];
    const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
    buttons[(index + (e.key === "ArrowDown" ? 1 : -1) + buttons.length) % buttons.length]?.focus();
  };

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        aria-haspopup="menu"
        aria-label={buttonLabel}
        aria-expanded={open}
        aria-controls={open ? id : undefined}
        title={title}
        onClick={() => (open ? close() : setOpen(true))}
        className={className}
      >
        {button}
      </button>
      {open &&
        createPortal(
          <div
            ref={listRef}
            id={id}
            role="menu"
            aria-label={label}
            onKeyDown={onKeyDown}
            style={place ?? { left: 0, top: 0, visibility: "hidden" }}
            className={`fixed z-50 flex max-h-[min(28rem,80vh)] min-w-44 flex-col overflow-y-auto rounded-lg border border-border bg-panel p-1 text-xs shadow-float ${place ? "animate-pop" : ""}`}
          >
            {items.map((item) => (
              <button
                key={item.value}
                type="button"
                role={value === undefined || item.action ? "menuitem" : "menuitemradio"}
                aria-checked={value === undefined || item.action ? undefined : item.value === value}
                disabled={item.disabled}
                onClick={() => {
                  close();
                  onChoose(item.value);
                }}
                className={`flex items-center gap-2.5 rounded-md px-2.5 py-1.5 text-left whitespace-nowrap transition-colors duration-150 outline-none hover:bg-raised focus-visible:bg-raised disabled:opacity-40 ${item.value === value && !item.action ? "text-accent" : "text-foreground"}`}
              >
                {item.icon && <span className="flex size-4 shrink-0 items-center justify-center text-muted">{item.icon}</span>}
                <span className="flex max-w-72 min-w-0 flex-1 flex-col">
                  <span className="font-medium">{item.label}</span>
                  {item.hint && <span className="text-[11px] whitespace-normal text-muted">{item.hint}</span>}
                </span>
              </button>
            ))}
          </div>,
          document.body,
        )}
    </>
  );
}
