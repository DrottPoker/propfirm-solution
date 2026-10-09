"use client";

import { useEffect, useLayoutEffect, useState } from "react";
import { create } from "zustand";

import { hasRulesPanel } from "@/lib/profile";
import { useProfile } from "@/lib/profileContext";
import { onSettingsLoaded, readSetting, writeSetting } from "@/lib/syncedSettings";

const seenKey = "trading.tourSeen";

interface TourState {
  open: boolean;
  start: () => void;
  finish: () => void;
}

/** Whether the tour is showing. It shows once on a login, and again when the trader asks for it in the user menu. */
export const useTour = create<TourState>()((set) => ({
  open: false,
  start: () => set({ open: true }),
  finish: () => {
    writeSetting(seenKey, "on");
    set({ open: false });
  },
}));

// The settings are loaded from the login before the terminal shows, so the tour knows whether it was seen.
onSettingsLoaded(() => {
  if (readSetting(seenKey) !== "on") {
    useTour.setState({ open: true });
  }
});

interface Step {
  target: string;
  title: string;
  text: string;
}

/**
 * A short tour the first time a trader opens the terminal (ADR 0058): the order ticket, the chart, the account's figures
 * and the rulebook, one card at a time beside each, which can be skipped. It never covers the page, so the trader can
 * try what it points at.
 */
export function Tour() {
  const open = useTour((s) => s.open);
  const finish = useTour((s) => s.finish);
  const profile = useProfile();
  const [index, setIndex] = useState(0);
  const [place, setPlace] = useState<{ ring: DOMRect; left: number; top: number } | null>(null);

  const steps: Step[] = [
    {
      target: "order-panel",
      title: "The order ticket",
      text: "Market opens now, Limit waits for a better price and Stop for a move through a level. Sell and Buy say what they send, and what the order risks shows under the buttons.",
    },
    {
      target: "chart",
      title: "The chart",
      text: "What you type shows faintly on the chart. Drag a position's stop loss or take profit to move it, and right-click for orders, alerts and lines.",
    },
    { target: "account", title: "The account", text: "Equity, what you made today and what is left before the nearest loss limit. More has every figure." },
    ...(hasRulesPanel(profile.modules)
      ? [{ target: "rules", title: "The rules", text: "Every rule and how it stands, with your own daily limits under them." }]
      : []),
  ];
  const targets = steps.map((s) => s.target).join(" ");
  // Only the parts on screen, so a phone, which shows one part at a time, skips the others.
  const [shown, setShown] = useState<string[]>([]);
  const visible = steps.filter((s) => shown.includes(s.target));
  const step = visible[Math.min(index, visible.length - 1)];
  const stepTarget = step?.target ?? null;

  useLayoutEffect(() => {
    if (!open) {
      return;
    }

    // Parts that show once the account has loaded join the tour then.
    const update = () =>
      setShown((current) => {
        const next = targets.split(" ").filter(isShown);
        return next.join(" ") === current.join(" ") ? current : next;
      });
    update();
    const timer = setInterval(update, 1_000);
    window.addEventListener("resize", update);
    return () => {
      clearInterval(timer);
      window.removeEventListener("resize", update);
    };
  }, [open, targets]);

  useLayoutEffect(() => {
    if (!open || !stepTarget) {
      return;
    }

    const measure = () => {
      const target = document.querySelector(`[data-tour="${stepTarget}"]`);
      if (!target) {
        return;
      }

      const ring = target.getBoundingClientRect();
      const width = Math.min(320, window.innerWidth - 16);
      // Beside the part when there is room on its left, otherwise inside it at the top.
      const left = ring.left - width - 12 >= 8 ? ring.left - width - 12 : Math.min(Math.max(8, ring.left + 12), window.innerWidth - width - 8);
      const top = Math.min(Math.max(8, ring.top + 12), window.innerHeight - 200);
      setPlace({ ring, left, top });
    };
    measure();
    window.addEventListener("resize", measure);
    return () => window.removeEventListener("resize", measure);
  }, [open, stepTarget]);

  useEffect(() => {
    if (!open) {
      return;
    }

    const onKeyDown = (e: KeyboardEvent) => e.key === "Escape" && finish();
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [open, finish]);

  if (!open || !step || !place) {
    return null;
  }

  const at = visible.indexOf(step);
  const last = at === visible.length - 1;
  const done = () => {
    setIndex(0);
    finish();
  };

  return (
    <>
      <div
        aria-hidden="true"
        className="pointer-events-none fixed z-40 rounded-lg ring-2 ring-accent/70 transition-all duration-300 ease-out-soft"
        style={{ left: place.ring.left + 2, top: place.ring.top + 2, width: place.ring.width - 4, height: place.ring.height - 4 }}
      />
      <div
        role="dialog"
        aria-label={`Tour, ${step.title}`}
        className="fixed z-50 flex w-80 max-w-[calc(100vw-1rem)] animate-pop flex-col gap-2 rounded-xl border border-border bg-panel p-4 text-sm shadow-float"
        style={{ left: place.left, top: place.top }}
      >
        <div className="flex items-baseline justify-between gap-3">
          <h2 className="font-semibold">{step.title}</h2>
          <span className="text-xs text-muted tabular-nums">
            {at + 1} of {visible.length}
          </span>
        </div>
        <p className="leading-relaxed text-muted">{step.text}</p>
        <div className="mt-1 flex items-center justify-between gap-2">
          <button type="button" onClick={done} className="text-xs text-muted transition-colors duration-150 hover:text-foreground">
            Skip the tour
          </button>
          <span className="flex gap-2">
            {at > 0 && (
              <button
                type="button"
                onClick={() => setIndex(at - 1)}
                className="h-8 rounded-lg border border-border bg-raised px-3 text-xs font-medium transition duration-150 hover:border-muted"
              >
                Back
              </button>
            )}
            <button
              type="button"
              onClick={() => (last ? done() : setIndex(at + 1))}
              className="h-8 rounded-lg bg-accent px-3 text-xs font-semibold text-accent-foreground transition duration-150 hover:brightness-110"
            >
              {last ? "Done" : "Next"}
            </button>
          </span>
        </div>
      </div>
    </>
  );
}

function isShown(target: string): boolean {
  const element = document.querySelector(`[data-tour="${target}"]`);
  if (!element) {
    return false;
  }

  const rect = element.getBoundingClientRect();
  return rect.width > 0 && rect.height > 0;
}
