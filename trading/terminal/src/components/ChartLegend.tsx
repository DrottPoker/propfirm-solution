"use client";

import type { IChartApi, MouseEventParams, Time } from "lightweight-charts";
import { useEffect, useState } from "react";

import type { Bar } from "@/lib/candles";
import { indicatorKinds, indicatorLabel, useChartStudies } from "@/lib/chartStudies";
import { formatPrice, formatSignedPercent, formatSignedPrice } from "@/lib/format";
import { chartTypes, type ChartType } from "@/lib/settings";
import { useTradingStore } from "@/lib/store";

import { CloseIcon, EyeIcon, EyeSlashIcon } from "./icons";
import type { IndicatorSeries } from "./IndicatorSeries";

/**
 * The chart's legend in its top left corner (ADR 0058): the bar under the mouse, or the latest one, with its open, high,
 * low, close and change, and each indicator with its value there. An indicator can be hidden for a while or removed
 * from its row, and a press on its name opens the indicators to change its period.
 */
export function ChartLegend({
  chart,
  bars,
  studies,
  symbol,
  digits,
  chartType,
  colors,
  onEditIndicators,
}: {
  chart: IChartApi | null;
  bars: () => readonly Bar[];
  studies: () => IndicatorSeries | null;
  symbol: string;
  digits: number;
  chartType: ChartType;
  colors: readonly string[];
  onEditIndicators: () => void;
}) {
  // The index of the bar under the mouse, or null for the latest.
  const [hovered, setHovered] = useState<number | null>(null);
  // Changes with every live price, so the latest bar's figures follow it.
  const [, setTick] = useState(0);
  const indicators = useChartStudies((s) => s.indicators);
  const remove = useChartStudies((s) => s.remove);
  const setHidden = useChartStudies((s) => s.setHidden);

  useEffect(() => {
    if (!chart) {
      return;
    }

    const onMove = (param: MouseEventParams<Time>) => {
      const logical = param.logical;
      setHovered(param.point && logical !== undefined && logical >= 0 ? Math.round(logical) : null);
    };
    chart.subscribeCrosshairMove(onMove);
    return () => chart.unsubscribeCrosshairMove(onMove);
  }, [chart]);

  // The latest bar follows the price, at most a few times a second.
  useEffect(() => {
    let frame = 0;
    const stop = useTradingStore.subscribe((state, previous) => {
      if (state.prices[symbol] !== previous.prices[symbol] && frame === 0) {
        frame = requestAnimationFrame(() => {
          frame = 0;
          setTick((t) => t + 1);
        });
      }
    });
    return () => {
      stop();
      cancelAnimationFrame(frame);
    };
  }, [symbol]);

  const all = bars();
  const index = hovered !== null && hovered < all.length ? hovered : all.length - 1;
  const bar = all[index];
  const before = index > 0 ? all[index - 1] : undefined;
  const values = new Map((studies()?.valuesAt(index) ?? []).map((v) => [v.id, v.values]));
  const change = bar && before ? bar.close - before.close : null;
  const changeColor = change === null || change === 0 ? "text-muted" : change > 0 ? "text-profit" : "text-loss";
  const typeName = chartTypes.find((t) => t.type === chartType)?.name;

  return (
    <div className="flex flex-col gap-0.5 text-[11px] tabular-nums">
      {bar && (
        <p className="flex flex-wrap items-baseline gap-x-2.5 gap-y-0.5 text-muted" data-testid="chart-legend">
          {chartType !== "candles" && <span>{typeName}</span>}
          <Figure label="O" value={formatPrice(bar.open, digits)} />
          <Figure label="H" value={formatPrice(bar.high, digits)} />
          <Figure label="L" value={formatPrice(bar.low, digits)} />
          <Figure label="C" value={formatPrice(bar.close, digits)} />
          {change !== null && before && (
            <span className={changeColor}>
              {formatSignedPrice(change, digits)} ({formatSignedPercent((change / before.close) * 100)})
            </span>
          )}
        </p>
      )}
      {indicators.map((indicator, i) => {
        const shown = values.get(indicator.id) ?? [];
        // MACD's line, signal and histogram, and Bollinger's middle, upper and lower band.
        const precision = indicator.kind === "RSI" ? 1 : indicator.kind === "MACD" ? digits + 1 : digits;
        return (
          <div key={indicator.id} className="group pointer-events-auto flex w-fit items-center gap-1.5 rounded px-1 -mx-1 hover:bg-panel/80">
            <span aria-hidden="true" className="h-0.5 w-2.5 rounded-full" style={{ backgroundColor: colors[i % colors.length] }} />
            <button
              type="button"
              onClick={onEditIndicators}
              title={`${indicatorKinds[indicator.kind].name}. Change its period.`}
              className={`transition-colors duration-150 hover:text-foreground ${indicator.hidden ? "text-muted/60 line-through" : "text-muted"}`}
            >
              {indicatorLabel(indicator)}
            </button>
            {!indicator.hidden && shown.length > 0 && (
              <span className="text-foreground">{shown.map((v) => (v === null ? "-" : v.toFixed(precision))).join("  ")}</span>
            )}
            <span className="flex items-center opacity-0 transition-opacity duration-150 group-hover:opacity-100 group-focus-within:opacity-100">
              <button
                type="button"
                aria-label={indicator.hidden ? `Show ${indicatorLabel(indicator)}` : `Hide ${indicatorLabel(indicator)}`}
                title={indicator.hidden ? "Show" : "Hide for a while"}
                onClick={() => setHidden(indicator.id, !indicator.hidden)}
                className="rounded p-0.5 text-muted hover:text-foreground"
              >
                {indicator.hidden ? <EyeIcon className="size-3.5" /> : <EyeSlashIcon className="size-3.5" />}
              </button>
              <button
                type="button"
                aria-label={`Remove ${indicatorLabel(indicator)}`}
                title="Remove"
                onClick={() => remove(indicator.id)}
                className="rounded p-0.5 text-muted hover:text-loss"
              >
                <CloseIcon className="size-3" />
              </button>
            </span>
          </div>
        );
      })}
    </div>
  );
}

function Figure({ label, value }: { label: string; value: string }) {
  return (
    <span>
      {label} <span className="text-foreground">{value}</span>
    </span>
  );
}
