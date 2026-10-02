"use client";

import {
  CandlestickSeries,
  ColorType,
  createChart,
  type IChartApi,
  type ISeriesApi,
  type UTCTimestamp,
} from "lightweight-charts";
import { useEffect, useMemo, useRef, useState } from "react";

import type { InstrumentInfo, PositionSnapshot, Timeframe } from "@/lib/api/types";
import { applyPrice, timeframes, toBar, type Bar } from "@/lib/candles";
import { formatVolume } from "@/lib/format";
import { useCandles } from "@/lib/queries";
import { useTradingStore } from "@/lib/store";

const colors = {
  background: "#11151c",
  text: "#7d8590",
  grid: "#1a1f29",
  up: "#22c55e",
  down: "#ef4444",
  open: "#3b82f6",
  stopLoss: "#ef4444",
  takeProfit: "#22c55e",
};

/** Bid candles with live updates and lines for open positions, stop loss and take profit. */
export function PriceChart({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo | null }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const seriesRef = useRef<ISeriesApi<"Candlestick"> | null>(null);
  const lastBarRef = useRef<Bar | undefined>(undefined);
  const [timeframe, setTimeframe] = useState<Timeframe>("M1");

  const symbol = instrument?.symbol ?? null;
  const candles = useCandles(accountId, symbol, timeframe);

  // The chart lives as long as the component.
  useEffect(() => {
    const container = containerRef.current;
    if (!container) {
      return;
    }

    const chart = createChart(container, {
      autoSize: true,
      layout: { background: { type: ColorType.Solid, color: colors.background }, textColor: colors.text },
      grid: { vertLines: { color: colors.grid }, horzLines: { color: colors.grid } },
      timeScale: { timeVisible: true, secondsVisible: false },
    });
    const series = chart.addSeries(CandlestickSeries, {
      upColor: colors.up,
      downColor: colors.down,
      wickUpColor: colors.up,
      wickDownColor: colors.down,
      borderVisible: false,
    });
    chartRef.current = chart;
    seriesRef.current = series;

    return () => {
      chart.remove();
      chartRef.current = null;
      seriesRef.current = null;
    };
  }, []);

  useEffect(() => {
    if (instrument) {
      seriesRef.current?.applyOptions({
        priceFormat: { type: "price", precision: instrument.digits, minMove: 10 ** -instrument.digits },
      });
    }
  }, [instrument]);

  // History replaces everything on the chart.
  useEffect(() => {
    const series = seriesRef.current;
    if (!series || !candles.data) {
      return;
    }

    const bars = candles.data.map(toBar);
    series.setData(bars.map(toChartBar));
    lastBarRef.current = bars.at(-1);
    chartRef.current?.timeScale().scrollToRealTime();
  }, [candles.data]);

  // Live prices update the latest bar without re-rendering React.
  useEffect(() => {
    if (!symbol) {
      return;
    }

    return useTradingStore.subscribe((state, previous) => {
      const price = state.prices[symbol];
      if (!price || price === previous.prices[symbol] || !seriesRef.current) {
        return;
      }

      const bar = applyPrice(lastBarRef.current, price.bid, Date.parse(price.timestamp) / 1000, timeframe);
      if (bar) {
        seriesRef.current.update(toChartBar(bar));
        lastBarRef.current = bar;
      }
    });
  }, [symbol, timeframe]);

  const positions = useTradingStore((s) => s.account?.positions);
  const symbolPositions = useMemo(() => positions?.filter((p) => p.symbol === symbol) ?? [], [positions, symbol]);
  usePositionLines(seriesRef, symbolPositions);

  return (
    <section className="flex min-w-0 flex-col bg-panel">
      <div className="flex items-center gap-1 border-b border-border px-3 py-1.5 text-sm">
        <span className="mr-3 font-medium">{symbol ?? "-"}</span>
        {timeframes.map((tf) => (
          <button
            key={tf}
            type="button"
            onClick={() => setTimeframe(tf)}
            aria-pressed={tf === timeframe}
            className={`rounded px-2 py-0.5 ${tf === timeframe ? "bg-accent/20 text-foreground" : "text-muted hover:text-foreground"}`}
          >
            {tf}
          </button>
        ))}
        <span className="ml-auto text-xs text-muted">Bid, UTC</span>
      </div>
      <div className="relative min-h-0 flex-1">
        <div ref={containerRef} className="absolute inset-0" />
        {candles.isError && (
          <p className="absolute inset-0 flex items-center justify-center text-sm text-muted">Could not load candles.</p>
        )}
      </div>
    </section>
  );
}

// Redraws the lines only when a position, stop loss or take profit actually changed.
function usePositionLines(seriesRef: React.RefObject<ISeriesApi<"Candlestick"> | null>, positions: PositionSnapshot[]) {
  const specs: LineSpec[] = positions.flatMap((p) => [
    { price: p.openPrice, color: colors.open, dashed: false, title: `${p.side} ${formatVolume(p.volume)}` },
    ...(p.stopLoss != null ? [{ price: p.stopLoss, color: colors.stopLoss, dashed: true, title: "SL" }] : []),
    ...(p.takeProfit != null ? [{ price: p.takeProfit, color: colors.takeProfit, dashed: true, title: "TP" }] : []),
  ]);
  const specsKey = JSON.stringify(specs);

  useEffect(() => {
    const series = seriesRef.current;
    if (!series) {
      return;
    }

    const lines = (JSON.parse(specsKey) as LineSpec[]).map((spec) =>
      series.createPriceLine({ price: spec.price, color: spec.color, lineWidth: 1, lineStyle: spec.dashed ? 2 : 0, title: spec.title }),
    );

    return () => {
      for (const line of lines) {
        series.removePriceLine(line);
      }
    };
  }, [seriesRef, specsKey]);
}

interface LineSpec {
  price: number;
  color: string;
  dashed: boolean;
  title: string;
}

function toChartBar(bar: Bar) {
  return { ...bar, time: bar.time as UTCTimestamp };
}
