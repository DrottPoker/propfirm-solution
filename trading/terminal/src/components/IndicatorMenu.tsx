"use client";

import { useEffect, useRef } from "react";

import { clampPeriod, indicatorKinds, indicatorLabel, indicatorOrder, maxIndicators, periodLimits, useChartStudies } from "@/lib/chartStudies";

import { CloseIcon } from "./icons";

/**
 * The indicators on the chart: each with its color and period, which can be changed or removed, and the kinds that
 * can be added. Saved on the trader's login, for every symbol. Closes on Esc or a click outside.
 */
export function IndicatorMenu({ colors, onClose }: { colors: readonly string[]; onClose: () => void }) {
  const ref = useRef<HTMLDivElement>(null);
  const indicators = useChartStudies((s) => s.indicators);
  const add = useChartStudies((s) => s.add);
  const remove = useChartStudies((s) => s.remove);
  const setPeriod = useChartStudies((s) => s.setPeriod);
  const full = indicators.length >= maxIndicators;

  useEffect(() => {
    const onPointerDown = (e: PointerEvent) => {
      if (!ref.current?.contains(e.target as Node)) {
        onClose();
      }
    };
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onClose();
      }
    };
    // After the click that opened the menu.
    const timer = setTimeout(() => document.addEventListener("pointerdown", onPointerDown, true), 0);
    window.addEventListener("keydown", onKeyDown);
    return () => {
      clearTimeout(timer);
      document.removeEventListener("pointerdown", onPointerDown, true);
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [onClose]);

  return (
    <div
      ref={ref}
      role="dialog"
      aria-label="Indicators"
      className="absolute top-2 left-2 z-20 flex w-72 max-w-[calc(100%-1rem)] origin-top-left animate-pop flex-col gap-3 rounded-xl border border-border bg-panel p-3 text-xs shadow-float"
    >
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold">Indicators</h3>
        <button type="button" aria-label="Close" onClick={onClose} className="rounded-md p-1 text-muted transition-colors duration-150 hover:bg-raised hover:text-foreground">
          <CloseIcon className="size-3.5" />
        </button>
      </div>

      {indicators.length > 0 ? (
        <ul className="flex flex-col gap-1.5">
          {indicators.map((indicator, index) => (
            <li key={indicator.id} className="flex items-center gap-2">
              <span aria-hidden="true" className="h-0.5 w-3 shrink-0 rounded-full" style={{ backgroundColor: colors[index % colors.length] }} />
              <span className="min-w-0 flex-1 truncate" title={indicatorKinds[indicator.kind].name}>
                {indicatorLabel(indicator)}
              </span>
              {indicatorKinds[indicator.kind].period !== null && (
                <input
                  type="number"
                  min={periodLimits.min}
                  max={periodLimits.max}
                  defaultValue={indicator.period}
                  aria-label={`${indicatorKinds[indicator.kind].name} period`}
                  onBlur={(e) => {
                    const period = clampPeriod(e.target.value);
                    if (period !== null && period !== indicator.period) {
                      setPeriod(indicator.id, period);
                    }
                    e.target.value = String(period ?? indicator.period);
                  }}
                  onKeyDown={(e) => e.key === "Enter" && e.currentTarget.blur()}
                  className="w-14 rounded-md border border-border bg-raised px-1.5 py-0.5 text-right tabular-nums outline-none focus:border-accent"
                />
              )}
              <button
                type="button"
                aria-label={`Remove ${indicatorLabel(indicator)}`}
                onClick={() => remove(indicator.id)}
                className="rounded-md p-1 text-muted transition-colors duration-150 hover:bg-raised hover:text-loss"
              >
                <CloseIcon className="size-3" />
              </button>
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-muted">No indicators on the chart.</p>
      )}

      <div className="flex flex-col gap-1 border-t border-border pt-2.5">
        <span className="text-muted">{full ? `At most ${maxIndicators} indicators at a time.` : "Add"}</span>
        <div className="flex flex-wrap gap-1">
          {indicatorOrder.map((kind) => (
            <button
              key={kind}
              type="button"
              disabled={full}
              onClick={() => add(kind)}
              className="rounded-md border border-border px-2 py-1 transition duration-150 hover:border-muted hover:bg-raised active:translate-y-px disabled:opacity-40"
            >
              {indicatorKinds[kind].name}
            </button>
          ))}
        </div>
        <span className="text-muted">Worked out from the bid candles. Saved on your login, for every symbol.</span>
      </div>
    </div>
  );
}
