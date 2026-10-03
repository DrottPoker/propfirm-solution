"use client";

import {
  CandlestickSeries,
  ColorType,
  createChart,
  HistogramSeries,
  LineStyle,
  type IChartApi,
  type IPriceLine,
  type ISeriesApi,
  type UTCTimestamp,
} from "lightweight-charts";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";

import { CommandRejectedError } from "@/lib/api/client";
import type { InstrumentInfo, Timeframe } from "@/lib/api/types";
import { applyPrice, timeframes, toBar, type Bar } from "@/lib/candles";
import { chartTrades } from "@/lib/chartTrades";
import { dayFigures } from "@/lib/daySummary";
import { formatPrice, formatSignedPercent, formatSignedPrice } from "@/lib/format";
import { spreadPoints } from "@/lib/instruments";
import { useOrderDraft, type GhostLine } from "@/lib/orderDraft";
import { useCandles, useDaySummary, useModifyStops, usePointValue } from "@/lib/queries";
import { useTradingStore } from "@/lib/store";

import { CollapseIcon, ExpandIcon } from "./icons";
import { KeepInView } from "./KeepInView";
import { PriceMenu, type MenuPlacement, type StopLineRef } from "./PriceMenu";
import { grabDistance, StopHandles, usePositionLines } from "./positionLines";
import { SymbolIcon } from "./SymbolIcon";
import { TradeMarkers } from "./TradeMarkers";

// The chart's own defaults, set explicitly so Reset chart knows where to go back to.
const defaultBarSpacing = 6;
const defaultRightOffset = 0;
const resetAnimationMs = 150;

const colors = {
  background: "#0d1320",
  text: "#8591a5",
  grid: "#151d2c",
  border: "#1d2738",
  crosshair: "#52607a",
  up: "#22c55e",
  down: "#f04438",
  upVolume: "rgba(34, 197, 94, 0.35)",
  downVolume: "rgba(240, 68, 56, 0.35)",
  last: "#2f7cf6",
  open: "#2f7cf6",
  stopLoss: "#f04438",
  takeProfit: "#22c55e",
  buy: "#12b76a",
  sell: "#ef4444",
  // 75 % opaque, so the candles show through the line.
  tradeLine: "rgba(133, 145, 165, 0.75)",
  // The order being filled in, at half the strength of real lines.
  ghost: {
    entry: "rgba(47, 124, 246, 0.5)",
    stopLoss: "rgba(240, 68, 56, 0.5)",
    takeProfit: "rgba(34, 197, 94, 0.5)",
  } satisfies Record<GhostLine["kind"], string>,
  // The chart draws labels without transparency, so these are the ghost colors mixed half and half with the panel.
  ghostLabel: {
    entry: "rgb(30, 72, 139)",
    stopLoss: "rgb(127, 44, 44)",
    takeProfit: "rgb(24, 108, 63)",
  } satisfies Record<GhostLine["kind"], string>,
  ghostText: "rgb(184, 191, 203)",
};

/**
 * Bid candles with tick volume, live updates, arrows for trades and lines for open positions. Stop loss and take
 * profit lines can be dragged to new levels.
 */
export function PriceChart({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo | null }) {
  const sectionRef = useRef<HTMLElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const seriesRef = useRef<ISeriesApi<"Candlestick"> | null>(null);
  const volumeRef = useRef<ISeriesApi<"Histogram"> | null>(null);
  const markersRef = useRef<TradeMarkers | null>(null);
  const handlesRef = useRef<StopHandles | null>(null);
  const keepInViewRef = useRef<KeepInView | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [menu, setMenu] = useState<OpenMenu | null>(null);
  const closeMenu = useCallback(() => setMenu(null), []);
  const lastBarRef = useRef<Bar | undefined>(undefined);
  const [timeframe, setTimeframe] = useState<Timeframe>("M1");
  const fullScreen = useFullScreen(sectionRef);

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
      layout: { background: { type: ColorType.Solid, color: colors.background }, textColor: colors.text, fontSize: 11 },
      grid: { vertLines: { color: colors.grid }, horzLines: { color: colors.grid } },
      crosshair: {
        vertLine: { color: colors.crosshair, labelBackgroundColor: colors.border },
        horzLine: { color: colors.crosshair, labelBackgroundColor: colors.border },
      },
      rightPriceScale: { borderColor: colors.border, scaleMargins: { top: 0.08, bottom: 0.22 } },
      timeScale: {
        borderColor: colors.border,
        timeVisible: true,
        secondsVisible: false,
        barSpacing: defaultBarSpacing,
        rightOffset: defaultRightOffset,
      },
    });
    const series = chart.addSeries(CandlestickSeries, {
      upColor: colors.up,
      downColor: colors.down,
      wickUpColor: colors.up,
      wickDownColor: colors.down,
      borderVisible: false,
      priceLineColor: colors.last,
      priceLineStyle: LineStyle.Dashed,
    });
    const volume = chart.addSeries(HistogramSeries, {
      priceScaleId: "volume",
      priceFormat: { type: "volume" },
      lastValueVisible: false,
      priceLineVisible: false,
    });
    volume.priceScale().applyOptions({ scaleMargins: { top: 0.84, bottom: 0 } });
    const markers = new TradeMarkers({ buy: colors.buy, sell: colors.sell, line: colors.tradeLine, outline: colors.background });
    series.attachPrimitive(markers);
    const handles = new StopHandles();
    series.attachPrimitive(handles);
    const keepInView = new KeepInView();
    series.attachPrimitive(keepInView);

    chartRef.current = chart;
    seriesRef.current = series;
    volumeRef.current = volume;
    markersRef.current = markers;
    handlesRef.current = handles;
    keepInViewRef.current = keepInView;

    return () => {
      chart.remove();
      chartRef.current = null;
      seriesRef.current = null;
      volumeRef.current = null;
      markersRef.current = null;
      handlesRef.current = null;
      keepInViewRef.current = null;
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
    if (!seriesRef.current || !volumeRef.current || !candles.data) {
      return;
    }

    const bars = candles.data.map(toBar);
    seriesRef.current.setData(bars.map(toCandle));
    volumeRef.current.setData(bars.map(toVolume));
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
      if (!price || price === previous.prices[symbol] || !seriesRef.current || !volumeRef.current) {
        return;
      }

      const bar = applyPrice(lastBarRef.current, price.bid, Date.parse(price.timestamp) / 1000, timeframe);
      if (bar) {
        seriesRef.current.update(toCandle(bar));
        volumeRef.current.update(toVolume(bar));
        lastBarRef.current = bar;
      }
    });
  }, [symbol, timeframe]);

  const pointValue = usePointValue(accountId, symbol).data;
  const showMessage = useMessage(setMessage);
  usePositionLines({
    accountId,
    instrument,
    pointValue,
    colors,
    chartRef,
    seriesRef,
    handlesRef,
    keepInViewRef,
    containerRef,
    onError: showMessage,
  });
  useGhostLines(seriesRef, keepInViewRef, symbol);
  useRightClick(containerRef, chartRef, seriesRef, handlesRef, instrument, setMenu);
  const resetChart = useResetChart(chartRef);

  const modify = useModifyStops(accountId);
  const modifyStops = (positionId: string, stopLoss: number | null, takeProfit: number | null) =>
    modify.mutate(
      { positionId, stopLoss, takeProfit },
      { onError: (e) => showMessage(e instanceof CommandRejectedError ? `Rejected: ${e.reason}` : "Could not reach the trading service.") },
    );
  useTradeMarkers(markersRef, symbol, timeframe);

  return (
    <section ref={sectionRef} className="flex min-w-0 flex-col rounded-lg border border-border bg-panel">
      {instrument ? <QuoteHeader accountId={accountId} instrument={instrument} /> : <div className="h-14 border-b border-border" />}

      <div className="flex items-center gap-1 border-b border-border px-3 py-1.5 text-xs">
        {timeframes.map((tf) => (
          <button
            key={tf}
            type="button"
            onClick={() => setTimeframe(tf)}
            aria-pressed={tf === timeframe}
            className={`rounded-md px-2.5 py-1 font-medium ${tf === timeframe ? "bg-accent/20 text-foreground" : "text-muted hover:bg-raised hover:text-foreground"}`}
          >
            {tf}
          </button>
        ))}
        <span className="ml-auto text-muted" title="Candles show the bid. Times are in UTC.">
          Bid · UTC
        </span>
        <button
          type="button"
          onClick={fullScreen.toggle}
          aria-label={fullScreen.active ? "Exit full screen" : "Full screen"}
          title={fullScreen.active ? "Exit full screen" : "Full screen"}
          className="ml-2 rounded-md p-1.5 text-muted hover:bg-raised hover:text-foreground"
        >
          {fullScreen.active ? <CollapseIcon /> : <ExpandIcon />}
        </button>
      </div>

      <div className="relative min-h-0 flex-1">
        <div ref={containerRef} className="absolute inset-0" />
        {candles.isError && (
          <p className="absolute inset-0 flex items-center justify-center text-sm text-muted">Could not load candles.</p>
        )}
        {menu && instrument && menu.symbol === instrument.symbol && (
          <PriceMenu
            instrument={instrument}
            price={menu.price}
            placement={menu}
            line={menu.line}
            ghost={menu.ghost}
            pointValue={pointValue}
            onModify={modifyStops}
            onResetChart={resetChart}
            onClose={closeMenu}
          />
        )}
        {message && (
          <p role="alert" className="absolute top-2 left-2 rounded-md border border-loss/40 bg-panel px-3 py-1.5 text-sm text-loss shadow-lg">
            {message}
          </p>
        )}
      </div>
    </section>
  );
}

// The symbol, the live bid and how it moved over the last 24 hours.
function QuoteHeader({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo }) {
  const { symbol, digits } = instrument;
  const price = useTradingStore((s) => s.prices[symbol]);
  const day = dayFigures(useDaySummary(accountId, symbol).data, price?.bid);
  const changeColor = day.change === null ? "text-muted" : day.change >= 0 ? "text-profit" : "text-loss";

  return (
    <div className="flex h-14 items-center gap-x-5 gap-y-1 overflow-hidden border-b border-border px-3">
      <span className="flex items-center gap-2">
        <SymbolIcon base={instrument.baseCurrency} quote={instrument.quoteCurrency} />
        <span className="text-base font-semibold">{symbol}</span>
      </span>
      <span className="flex items-baseline gap-2">
        <span className="font-mono text-xl font-semibold tabular-nums">{formatPrice(price?.bid, digits)}</span>
        {day.change !== null && (
          <span className={`font-mono text-sm tabular-nums ${changeColor}`} title="Change of the bid over 24 hours">
            {formatSignedPrice(day.change, digits)} ({formatSignedPercent(day.changePercent)})
          </span>
        )}
      </span>
      <span className="flex gap-4 text-xs whitespace-nowrap">
        <Stat label="24h high" value={formatPrice(day.high, digits)} />
        <Stat label="24h low" value={formatPrice(day.low, digits)} />
        <Stat label="Spread" value={price ? `${spreadPoints(price.bid, price.ask, digits)} points` : "-"} />
      </span>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <span className="flex gap-1.5">
      <span className="text-muted">{label}</span>
      <span className="font-mono tabular-nums">{value}</span>
    </span>
  );
}

// Shows a message for a few seconds, for example a stop the engine refused.
function useMessage(setMessage: (message: string | null) => void) {
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  useEffect(() => () => clearTimeout(timer.current), []);

  return useCallback(
    (text: string) => {
      setMessage(text);
      clearTimeout(timer.current);
      timer.current = setTimeout(() => setMessage(null), 5_000);
    },
    [setMessage],
  );
}

// Full screen for the chart panel. Escape leaves it too, so the state follows the browser.
function useFullScreen(ref: React.RefObject<HTMLElement | null>) {
  const [active, setActive] = useState(false);

  useEffect(() => {
    const onChange = () => setActive(document.fullscreenElement !== null && document.fullscreenElement === ref.current);
    document.addEventListener("fullscreenchange", onChange);
    return () => document.removeEventListener("fullscreenchange", onChange);
  }, [ref]);

  const toggle = () => {
    if (document.fullscreenElement) {
      void document.exitFullscreen();
    } else {
      void ref.current?.requestFullscreen();
    }
  };

  return { active, toggle };
}

// Like Reset chart view in TradingView: the default zoom at the latest candle, and the price scale following the
// candles again after the trader dragged it. Alt+R does the same from anywhere in the terminal.
function useResetChart(chartRef: React.RefObject<IChartApi | null>) {
  const frameRef = useRef<number | undefined>(undefined);
  useEffect(() => () => cancelAnimationFrame(frameRef.current ?? 0), []);

  // The chart's own scroll to the latest candle takes 400 ms and cannot be made shorter, so this eases the zoom and
  // the scroll to the defaults itself. With reduced motion the chart jumps straight there.
  const reset = useCallback(() => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }

    cancelAnimationFrame(frameRef.current ?? 0);
    chart.priceScale("right").applyOptions({ autoScale: true });
    const timeScale = chart.timeScale();
    const finish = () => timeScale.applyOptions({ barSpacing: defaultBarSpacing, rightOffset: defaultRightOffset });
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      finish();
      timeScale.scrollToPosition(defaultRightOffset, false);
      return;
    }

    const from = { spacing: timeScale.options().barSpacing, position: timeScale.scrollPosition() };
    const start = performance.now();
    const step = (now: number) => {
      const progress = Math.min(1, (now - start) / resetAnimationMs);
      const eased = 1 - (1 - progress) ** 3;
      timeScale.applyOptions({ barSpacing: from.spacing + (defaultBarSpacing - from.spacing) * eased });
      timeScale.scrollToPosition(from.position + (defaultRightOffset - from.position) * eased, false);
      if (progress < 1) {
        frameRef.current = requestAnimationFrame(step);
      } else {
        finish();
      }
    };
    frameRef.current = requestAnimationFrame(step);
  }, [chartRef]);

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      // The physical R key, so it works on every keyboard layout. AltGr counts as Ctrl and Alt and is left alone.
      if (e.altKey && !e.ctrlKey && !e.metaKey && e.code === "KeyR") {
        e.preventDefault();
        reset();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [reset]);

  return reset;
}

// A right click on the candles opens the price menu at the level under the mouse, rounded to a point. The price axis
// keeps the browser's own menu. Scrolling the chart closes the menu, since the level moves away from it.
interface OpenMenu extends MenuPlacement {
  symbol: string;
  price: number;
  line: StopLineRef | null;
  ghost: GhostLine["kind"] | null;
}

function useRightClick(
  containerRef: React.RefObject<HTMLDivElement | null>,
  chartRef: React.RefObject<IChartApi | null>,
  seriesRef: React.RefObject<ISeriesApi<"Candlestick"> | null>,
  handlesRef: React.RefObject<StopHandles | null>,
  instrument: InstrumentInfo | null,
  setMenu: (menu: OpenMenu | null) => void,
) {
  const instrumentRef = useRef(instrument);
  useEffect(() => {
    instrumentRef.current = instrument;
  });

  useEffect(() => {
    const container = containerRef.current;
    if (!container) {
      return;
    }

    const onContextMenu = (e: MouseEvent) => {
      const chart = chartRef.current;
      const series = seriesRef.current;
      const current = instrumentRef.current;
      const rect = container.getBoundingClientRect();
      const x = e.clientX - rect.left;
      const y = e.clientY - rect.top;
      const wanted = series?.coordinateToPrice(y);
      if (!chart || !current || wanted == null || x > chart.timeScale().width()) {
        return;
      }

      // Stop line keys are the position id and the kind, as the position lines make them.
      const key = handlesRef.current?.targetAt(y);
      const separator = key?.lastIndexOf(":") ?? -1;
      const line =
        key && separator > 0 ? { positionId: key.slice(0, separator), kind: key.slice(separator + 1) as StopLineRef["kind"] } : null;

      // Without a position's stop line there, the nearest ghost line of the order being filled in.
      const draft = useOrderDraft.getState();
      const ghost =
        line || draft.symbol !== current.symbol
          ? null
          : (draft.lines
              .map((g) => ({ ghost: g, distance: Math.abs((series?.priceToCoordinate(g.price) ?? Infinity) - y) }))
              .filter((g) => g.distance <= grabDistance)
              .sort((a, b) => a.distance - b.distance)[0]?.ghost ?? null);

      // On a line the level is the line's own, a few pixels away from the mouse at most.
      const scale = 10 ** current.digits;
      const price = (key ? handlesRef.current?.priceOf(key) : null) ?? ghost?.price ?? Math.round(wanted * scale) / scale;
      if (price <= 0) {
        return;
      }

      e.preventDefault();
      setMenu({ x, y, flipX: x > rect.width - 260, flipY: y > rect.height / 2, symbol: current.symbol, price, line, ghost: ghost?.kind ?? null });
    };
    const onWheel = () => setMenu(null);

    container.addEventListener("contextmenu", onContextMenu);
    container.addEventListener("wheel", onWheel, { passive: true });
    return () => {
      container.removeEventListener("contextmenu", onContextMenu);
      container.removeEventListener("wheel", onWheel);
    };
  }, [containerRef, chartRef, seriesRef, handlesRef, setMenu]);
}

const noGhosts: GhostLine[] = [];

// Faint lines where the order being filled in would put its price, stop loss and take profit. Lines move in place as
// the price moves, and are only created or removed when one appears or goes away.
function useGhostLines(
  seriesRef: React.RefObject<ISeriesApi<"Candlestick"> | null>,
  keepInViewRef: React.RefObject<KeepInView | null>,
  symbol: string | null,
) {
  const ghosts = useOrderDraft((s) => (s.symbol === symbol ? s.lines : noGhosts));
  const linesRef = useRef(new Map<GhostLine["kind"], IPriceLine>());

  useEffect(() => {
    const series = seriesRef.current;
    if (!series) {
      return;
    }

    const lines = linesRef.current;
    for (const [kind, line] of lines) {
      if (!ghosts.some((g) => g.kind === kind)) {
        series.removePriceLine(line);
        lines.delete(kind);
      }
    }

    for (const ghost of ghosts) {
      const line = lines.get(ghost.kind);
      if (line) {
        line.applyOptions({ price: ghost.price, title: ghost.title });
      } else {
        lines.set(
          ghost.kind,
          series.createPriceLine({
            price: ghost.price,
            color: colors.ghost[ghost.kind],
            lineWidth: 1,
            lineStyle: LineStyle.LargeDashed,
            title: ghost.title,
            // The labels are faint too, or they would look like real ones.
            axisLabelColor: colors.ghostLabel[ghost.kind],
            axisLabelTextColor: colors.ghostText,
          }),
        );
      }
    }

    keepInViewRef.current?.set("ghosts", ghosts.map((g) => g.price));
  }, [seriesRef, keepInViewRef, ghosts]);
}

// Arrows where the symbol's positions opened and closed. Redrawn only when a trade or the timeframe changed.
function useTradeMarkers(markersRef: React.RefObject<TradeMarkers | null>, symbol: string | null, timeframe: Timeframe) {
  const events = useTradingStore((s) => s.events);
  const positions = useTradingStore((s) => s.account?.positions);
  const tradesKey = useMemo(
    () => JSON.stringify(symbol ? chartTrades(events.map((e) => e.event), positions ?? [], symbol) : []),
    [events, positions, symbol],
  );

  useEffect(() => {
    markersRef.current?.setTrades(JSON.parse(tradesKey), timeframe);
  }, [markersRef, tradesKey, timeframe]);
}

function toCandle(bar: Bar) {
  return { time: bar.time as UTCTimestamp, open: bar.open, high: bar.high, low: bar.low, close: bar.close };
}

function toVolume(bar: Bar) {
  return { time: bar.time as UTCTimestamp, value: bar.ticks, color: bar.close >= bar.open ? colors.upVolume : colors.downVolume };
}
