"use client";

import { DotsThreeIcon } from "@phosphor-icons/react/ssr";
import { useEffect, useId, useRef, useState } from "react";

import { secondaryButtonClass } from "./ui";

/**
 * Actions that are seldom needed or cannot be undone, behind one button, so they do not stand beside the page's main
 * ones. The menu closes on a click outside it, on Escape and when an action is chosen.
 */
export function ActionsMenu({ label = "More actions", items }: { label?: string; items: { label: string; onSelect: () => void; danger?: boolean }[] }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const menuId = useId();

  useEffect(() => {
    if (!open) {
      return;
    }

    const onPointerDown = (event: PointerEvent) => {
      if (!ref.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setOpen(false);
      }
    };
    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        aria-label={label}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={menuId}
        onClick={() => setOpen(!open)}
        className={`${secondaryButtonClass} grid size-10 place-items-center px-0`}
      >
        <DotsThreeIcon weight="bold" className="size-5" />
      </button>
      {open && (
        <div id={menuId} role="menu" className="absolute right-0 z-20 mt-2 flex w-56 origin-top-right animate-pop flex-col rounded-xl border border-border bg-panel p-1.5 text-sm shadow-float">
          {items.map((item) => (
            <button
              key={item.label}
              type="button"
              role="menuitem"
              onClick={() => {
                setOpen(false);
                item.onSelect();
              }}
              className={`rounded-lg px-3 py-2 text-left transition-colors hover:bg-raised ${item.danger ? "text-loss" : ""}`}
            >
              {item.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
