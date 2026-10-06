"use client";

import {
  CandlestickSeries,
  ColorType,
  createChart,
  HistogramSeries,
  LineStyle,
  TickMarkType,
  type IChartApi,
  type IPriceLine,
  type ISeriesApi,
  type LogicalRange,
  type Time,
  type UTCTimestamp,
} from "lightweight-charts";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";

import { CommandRejectedError } from "@/lib/api/client";
import type { InstrumentInfo, Timeframe } from "@/lib/api/types";
import { applyPrice, mergeOlder, secondsOf, timeframes, toBar, type Bar } from "@/lib/candles";
import { useChartStudies } from "@/lib/chartStudies";
import { chartTrades } from "@/lib/chartTrades";
import { kronant, mix, withAlpha } from "@/lib/colors";
import { dayFigures } from "@/lib/daySummary";
import { rejectionText } from "@/lib/events";
import { formatChartTick, formatMinute, formatPrice, formatSignedPercent, formatSignedPrice, timeZoneName, type ChartTick } from "@/lib/format";
import { spreadPoints } from "@/lib/instruments";
import { useOrderDraft, type GhostLine } from "@/lib/orderDraft";
import { isClosed } from "@/lib/marketHours";
import { fetchOlderCandles, useCandles, useDaySummary, useMarket, useModifyStops, usePointValue } from "@/lib/queries";
import type { DrawingTool } from "@/lib/drawings";
import { useSettings } from "@/lib/settings";
import { useTradingStore } from "@/lib/store";
import { useTimeZone } from "@/lib/timeZone";

import { ClosedTag } from "./ClosedTag";
import { useDrawingTools } from "./drawingTools";
import { DrawingsLayer } from "./DrawingsLayer";
import { CollapseIcon, ExpandIcon, HorizontalLineIcon, IndicatorsIcon, RectangleIcon, TrashIcon, TrendLineIcon, VolumeBarsIcon } from "./icons";
import { IndicatorMenu } from "./IndicatorMenu";
import { IndicatorSeries } from "./IndicatorSeries";
import { KeepInView } from "./KeepInView";
import { PriceMenu, type MenuPlacement, type StopLineRef } from "./PriceMenu";
import { grabDistance, StopHandles, usePositionLines } from "./positionLines";
import { SymbolIcon } from "./SymbolIcon";
import { TradeMarkers } from "./TradeMarkers";

const chartTicks: Record<TickMarkType, ChartTick> = {
  [TickMarkType.Year]: "year",
  [TickMarkType.Month]: "month",
  [TickMarkType.DayOfMonth]: "day",
  [TickMarkType.Time]: "time",
  [TickMarkType.TimeWithSeconds]: "seconds",
};

// The chart's own defaults, set explicitly so Reset chart knows where to go back to.
const defaultBarSpacing = 6;
const defaultRightOffset = 0;
const resetAnimationMs = 150;

// Older candles are loaded when the trader scrolls to within this many candles of the first one.
const olderThreshold = 20;

// How long removing every drawing waits for the second click.
const confirmRemoveMs = 4_000;

const drawingTools: { tool: DrawingTool; label: string; hint: string; Icon: (props: { className?: string }) => React.ReactNode }[] = [
  { tool: "horizontal", label: "Horizontal line", hint: "Click where the line goes.", Icon: HorizontalLineIcon },
  { tool: "trend", label: "Trend line", hint: "Click where the line starts, then where it ends.", Icon: TrendLineIcon },
  { tool: "rectangle", label: "Rectangle", hint: "Click one corner, then the opposite one.", Icon: RectangleIcon },
];

// The volume is a low band under the candles, so it is there to glance at without pulling the eye from the price.
const volumeBand = 0.13;
const priceMargins = { withVolume: { top: 0.08, bottom: 0.2 }, withoutVolume: { top: 0.08, bottom: 0.08 } };

// The buttons above the chart: the timeframes, the volume and full screen.
const toolClass = "shrink-0 rounded-md py-1 font-medium transition duration-150 ease-out-soft active:translate-y-px";

// Kronant's colors: candles in green and red on the panel, and lines of our own, such as the last price, in brass.
const colors = {
  background: kronant.panel,
  text: kronant.muted,
  grid: mix(kronant.border, kronant.panel, 0.45),
  border: kronant.border,
  crosshair: mix(kronant.muted, kronant.panel, 0.6),
  up: kronant.profit,
  down: kronant.loss,
  // Faint, since every candle has about as many prices and full colors would look like a barcode.
  upVolume: withAlpha(kronant.profit, 0.14),
  downVolume: withAlpha(kronant.loss, 0.14),
  last: kronant.brass,
  open: kronant.brass,
  // A pending order's price, dotted and a little dimmer than an open position's.
  order: mix(kronant.brass, kronant.panel, 0.25),
  stopLoss: kronant.loss,
  takeProfit: kronant.profit,
  buy: kronant.profit,
  sell: kronant.loss,
  // The indicators' lines in turn, apart from the candles' green and red and from brass, which marks our own lines.
  studies: ["#7aa7e0", "#b48ee6", "#5cc8c0", "#e08ab4", "#d9c38c", kronant.foreground],
  studyGuide: mix(kronant.muted, kronant.panel, 0.6),
  studyUp: withAlpha(kronant.profit, 0.55),
  studyDown: withAlpha(kronant.loss, 0.55),
  drawing: {
    line: mix(kronant.foreground, kronant.panel, 0.75),
    fill: withAlpha(kronant.foreground, 0.05),
    selected: kronant.brass,
    label: mix(kronant.foreground, kronant.panel, 0.75),
    labelText: kronant.panel,
  },
  // 75 % opaque, so the candles show through the line.
  tradeLine: withAlpha(kronant.muted, 0.75),
  // The order being filled in, at half the strength of real lines.
  ghost: {
    entry: withAlpha(kronant.brass, 0.5),
    stopLoss: withAlpha(kronant.loss, 0.5),
    takeProfit: withAlpha(kronant.profit, 0.5),
  } satisfies Record<GhostLine["kind"], string>,
  // The chart draws labels without transparency, so these are the ghost colors mixed half and half with the panel.
  ghostLabel: {
    entry: mix(kronant.brass, kronant.panel, 0.5),
    stopLoss: mix(kronant.loss, kronant.panel, 0.5),
    takeProfit: mix(kronant.profit, kronant.panel, 0.5),
  } satisfies Record<GhostLine["kind"], string>,
  ghostText: mix(kronant.foreground, kronant.panel, 0.75),
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
  const studiesRef = useRef<IndicatorSeries | null>(null);
  const drawingsRef = useRef<DrawingsLayer | null>(null);
  // Every candle on the chart, the older ones loaded by scrolling back first. Live prices change the last one.
  const barsRef = useRef<Bar[]>([]);
  const olderRef = useRef({ loading: false, exhausted: false });
  const [indicatorsOpen, setIndicatorsOpen] = useState(false);
  const closeIndicators = useCallback(() => setIndicatorsOpen(false), []);
  const indicators = useChartStudies((s) => s.indicators);
  const [message, setMessage] = useState<string | null>(null);
  const [menu, setMenu] = useState<OpenMenu | null>(null);
  const closeMenu = useCallback(() => setMenu(null), []);
  const [timeframe, setTimeframe] = useState<Timeframe>("M1");
  const fullScreen = useFullScreen(sectionRef);
  const showVolume = useSettings((s) => s.chartVolume);
  const changeSetting = useSettings((s) => s.change);

  const symbol = instrument?.symbol ?? null;
  const candles = useCandles(accountId, symbol, timeframe);
  const timeZone = useTimeZone();

  // The chart lives as long as the component.
  useEffect(() => {
    const container = containerRef.current;
    if (!container) {
      return;
    }

    // The axes' figures in the monospace, like every other price. The chart draws on a canvas, so it needs the name.
    const fontFamily = getComputedStyle(container).getPropertyValue("--font-code").trim();
    const chart = createChart(container, {
      autoSize: true,
      layout: {
        background: { type: ColorType.Solid, color: colors.background },
        textColor: colors.text,
        fontSize: 11,
        ...(fontFamily ? { fontFamily } : {}),
      },
      grid: { vertLines: { color: colors.grid }, horzLines: { color: colors.grid } },
      crosshair: {
        vertLine: { color: colors.crosshair, labelBackgroundColor: colors.border },
        horzLine: { color: colors.crosshair, labelBackgroundColor: colors.border },
      },
      rightPriceScale: { borderColor: colors.border, scaleMargins: priceMargins.withVolume },
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
    volume.priceScale().applyOptions({ scaleMargins: { top: 1 - volumeBand, bottom: 0 } });
    const markers = new TradeMarkers({ buy: colors.buy, sell: colors.sell, line: colors.tradeLine, outline: colors.background });
    series.attachPrimitive(markers);
    const handles = new StopHandles();
    series.attachPrimitive(handles);
    const keepInView = new KeepInView();
    series.attachPrimitive(keepInView);
    const drawingsLayer = new DrawingsLayer(colors.drawing);
    series.attachPrimitive(drawingsLayer);
    const studies = new IndicatorSeries(chart, { lines: colors.studies, up: colors.studyUp, down: colors.studyDown, guide: colors.studyGuide });

    chartRef.current = chart;
    seriesRef.current = series;
    volumeRef.current = volume;
    markersRef.current = markers;
    handlesRef.current = handles;
    keepInViewRef.current = keepInView;
    drawingsRef.current = drawingsLayer;
    studiesRef.current = studies;

    return () => {
      chart.remove();
      chartRef.current = null;
      seriesRef.current = null;
      volumeRef.current = null;
      markersRef.current = null;
      handlesRef.current = null;
      keepInViewRef.current = null;
      drawingsRef.current = null;
      studiesRef.current = null;
    };
  }, []);

  // The indicators are the same on every symbol, with the instrument's decimals.
  const digits = instrument?.digits ?? 5;
  useEffect(() => {
    studiesRef.current?.setIndicators(indicators, digits);
  }, [indicators, digits]);

  // Without the volume, the candles take its place.
  useEffect(() => {
    volumeRef.current?.applyOptions({ visible: showVolume });
    chartRef.current?.priceScale("right").applyOptions({ scaleMargins: showVolume ? priceMargins.withVolume : priceMargins.withoutVolume });
  }, [showVolume]);

  // Bars stay in UTC seconds; only the labels are in the account's time zone, like every other time on screen.
  useEffect(() => {
    chartRef.current?.applyOptions({
      localization: { timeFormatter: (time: Time) => (typeof time === "number" ? formatMinute(new Date(time * 1000), timeZone) : String(time)) },
      timeScale: {
        tickMarkFormatter: (time: Time, tick: TickMarkType) => (typeof time === "number" ? formatChartTick(time, chartTicks[tick], timeZone) : null),
      },
    });
  }, [timeZone]);

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
    barsRef.current = bars;
    olderRef.current = { loading: false, exhausted: false };
    seriesRef.current.setData(bars.map(toCandle));
    volumeRef.current.setData(bars.map(toVolume));
    studiesRef.current?.setBars(bars);
    drawingsRef.current?.setBars(bars.map((b) => b.time), secondsOf(timeframe), digits);
    chartRef.current?.timeScale().scrollToRealTime();
    // Only new history redraws: the timeframe and decimals come with it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [candles.data]);

  // Scrolling near the first candle loads older ones in front of it, keeping the view where it is.
  useEffect(() => {
    const chart = chartRef.current;
    if (!chart || !symbol) {
      return;
    }

    let current = true;
    const onRange = (range: LogicalRange | null) => {
      const state = olderRef.current;
      const bars = barsRef.current;
      if (!range || range.from > olderThreshold || state.loading || state.exhausted || bars.length === 0) {
        return;
      }

      state.loading = true;
      fetchOlderCandles(accountId, symbol, timeframe, bars[0].time)
        .then((candles) => {
          // A new history replaced the candles meanwhile.
          if (!current || barsRef.current !== bars || !seriesRef.current || !volumeRef.current) {
            return;
          }

          const merged = mergeOlder(candles.map(toBar), bars);
          const added = merged.length - bars.length;
          if (added === 0) {
            state.exhausted = true;
            return;
          }

          const visible = chart.timeScale().getVisibleLogicalRange();
          barsRef.current = merged;
          seriesRef.current.setData(merged.map(toCandle));
          volumeRef.current.setData(merged.map(toVolume));
          if (visible) {
            chart.timeScale().setVisibleLogicalRange({ from: visible.from + added, to: visible.to + added });
          }
          studiesRef.current?.setBars(merged);
          drawingsRef.current?.setBars(merged.map((b) => b.time), secondsOf(timeframe), digits);
        })
        // Tried again on the next scroll.
        .catch(() => undefined)
        .finally(() => {
          state.loading = false;
        });
    };

    chart.timeScale().subscribeVisibleLogicalRangeChange(onRange);
    return () => {
      current = false;
      chart.timeScale().unsubscribeVisibleLogicalRangeChange(onRange);
    };
  }, [accountId, symbol, timeframe, digits]);

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

      const bars = barsRef.current;
      const last = bars.at(-1);
      const bar = applyPrice(last, price.bid, Date.parse(price.timestamp) / 1000, timeframe);
      if (bar) {
        seriesRef.current.update(toCandle(bar));
        volumeRef.current.update(toVolume(bar));
        if (last && bar.time === last.time) {
          bars[bars.length - 1] = bar;
        } else {
          bars.push(bar);
          drawingsRef.current?.setBars(bars.map((b) => b.time), secondsOf(timeframe), digits);
        }
        studiesRef.current?.updateLast(bars);
      }
    });
  }, [symbol, timeframe, digits]);

  const pointValue = usePointValue(accountId, symbol).data;
  const marketOpen = !isClosed(useMarket(accountId, symbol));
  const showMessage = useMessage(setMessage);
  usePositionLines({
    accountId,
    instrument,
    pointValue,
    marketOpen,
    colors,
    chartRef,
    seriesRef,
    handlesRef,
    keepInViewRef,
    containerRef,
    onError: showMessage,
  });
  useGhostLines(seriesRef, keepInViewRef, symbol);
  const drawing = useDrawingTools({ containerRef, chartRef, layerRef: drawingsRef, symbol, digits });
  const [confirmRemove, setConfirmRemove] = useState(false);
  useEffect(() => {
    if (!confirmRemove) {
      return;
    }

    const timer = setTimeout(() => setConfirmRemove(false), confirmRemoveMs);
    return () => clearTimeout(timer);
  }, [confirmRemove]);
  // The selected drawing goes at once, and all of them on a second click.
  const removeDrawings = () => {
    if (drawing.selected || confirmRemove) {
      setConfirmRemove(false);
      drawing.remove();
    } else {
      setConfirmRemove(true);
    }
  };
  const activeTool = drawingTools.find((t) => t.tool === drawing.tool);
  useRightClick(containerRef, chartRef, seriesRef, handlesRef, instrument, setMenu);
  const resetChart = useResetChart(chartRef);

  const modify = useModifyStops(accountId);
  // A trailing stop keeps trailing at the new level, and ends with the stop loss.
  const modifyStops = (positionId: string, stopLoss: number | null, takeProfit: number | null) =>
    modify.mutate(
      {
        positionId,
        stopLoss,
        takeProfit,
        trailingStop: stopLoss !== null && useTradingStore.getState().account?.positions.find((p) => p.positionId === positionId)?.trailingDistance != null,
      },
      { onError: (e) => showMessage(e instanceof CommandRejectedError ? rejectionText(e.reason) : "Could not reach the trading service.") },
    );
  useTradeMarkers(markersRef, symbol, timeframe);

  return (
    <section
      ref={sectionRef}
      className="flex min-w-0 flex-col overflow-hidden rounded-xl border border-border bg-panel shadow-card [&:fullscreen]:rounded-none"
    >
      {instrument ? <QuoteHeader accountId={accountId} instrument={instrument} /> : <div className="h-14 border-b border-border" />}

      <div className="@container flex items-center gap-1 overflow-x-auto border-b border-border px-3 py-1.5 text-xs">
        {timeframes.map((tf) => (
          <button
            key={tf}
            type="button"
            onClick={() => setTimeframe(tf)}
            aria-pressed={tf === timeframe}
            className={`${toolClass} px-2 @md:px-2.5 ${tf === timeframe ? "bg-accent/15 text-accent" : "text-muted hover:bg-raised hover:text-foreground"}`}
          >
            {tf}
          </button>
        ))}
        <span aria-hidden="true" className="mx-1.5 h-4 w-px shrink-0 bg-border" />
        <button
          type="button"
          onClick={() => changeSetting("chartVolume", !showVolume)}
          aria-pressed={showVolume}
          title={showVolume ? "Hide the tick volume under the candles" : "Show the tick volume under the candles"}
          className={`${toolClass} flex items-center gap-1.5 px-2 ${showVolume ? "text-foreground" : "text-muted hover:bg-raised hover:text-foreground"}`}
        >
          <VolumeBarsIcon className={`size-3.5 ${showVolume ? "text-accent" : ""}`} />
          <span className="@max-md:sr-only">Volume</span>
        </button>
        <button
          type="button"
          onClick={() => setIndicatorsOpen((open) => !open)}
          aria-expanded={indicatorsOpen}
          title="Moving averages, Bollinger Bands, RSI and MACD"
          className={`${toolClass} flex items-center gap-1.5 px-2 ${indicatorsOpen || indicators.length > 0 ? "text-foreground" : "text-muted hover:bg-raised hover:text-foreground"}`}
        >
          <IndicatorsIcon className={`size-3.5 ${indicators.length > 0 ? "text-accent" : ""}`} />
          <span className="@max-md:sr-only">Indicators{indicators.length > 0 ? ` (${indicators.length})` : ""}</span>
        </button>
        <span aria-hidden="true" className="mx-1.5 h-4 w-px shrink-0 bg-border" />
        {drawingTools.map(({ tool, label, Icon }) => (
          <button
            key={tool}
            type="button"
            onClick={() => drawing.chooseTool(tool)}
            aria-pressed={drawing.tool === tool}
            aria-label={label}
            title={label}
            className={`${toolClass} p-1.5 ${drawing.tool === tool ? "bg-accent/15 text-accent" : "text-muted hover:bg-raised hover:text-foreground"}`}
          >
            <Icon className="size-3.5" />
          </button>
        ))}
        <button
          type="button"
          onClick={removeDrawings}
          disabled={drawing.drawings.length === 0}
          aria-label={drawing.selected ? "Remove the selected drawing" : "Remove all drawings"}
          title={
            drawing.selected
              ? "Remove the selected drawing (Delete)"
              : confirmRemove
                ? `Click again to remove all ${drawing.drawings.length} drawings on ${symbol}`
                : `Remove all drawings on ${symbol}`
          }
          className={`${toolClass} p-1.5 disabled:opacity-40 ${confirmRemove ? "bg-loss/15 text-loss" : "text-muted hover:bg-raised hover:text-foreground"}`}
        >
          <TrashIcon className="size-3.5" />
        </button>
        <span
          className="ml-auto shrink-0 pl-2 whitespace-nowrap text-muted @max-xl:hidden"
          title={`Candles show the bid. Times are in ${timeZone}, the time zone of the account's trading day.`}
        >
          Bid · {timeZoneName(timeZone)}
        </span>
        <button
          type="button"
          onClick={fullScreen.toggle}
          aria-label={fullScreen.active ? "Exit full screen" : "Full screen"}
          title={fullScreen.active ? "Exit full screen" : "Full screen"}
          className={`${toolClass} ml-2 p-1.5 text-muted hover:bg-raised hover:text-foreground`}
        >
          {fullScreen.active ? <CollapseIcon /> : <ExpandIcon />}
        </button>
      </div>

      <div className="relative min-h-0 flex-1">
        <div ref={containerRef} className="absolute inset-0" />
        {indicatorsOpen && <IndicatorMenu colors={colors.studies} onClose={closeIndicators} />}
        {activeTool && (
          <p role="status" className="pointer-events-none absolute top-2 left-1/2 z-10 -translate-x-1/2 animate-fade rounded-lg border border-border bg-panel px-3 py-1.5 text-xs text-muted shadow-float">
            {activeTool.label}: {activeTool.hint} Esc to stop.
          </p>
        )}
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
            marketOpen={marketOpen}
            onModify={modifyStops}
            onResetChart={resetChart}
            onClose={closeMenu}
          />
        )}
        {message && (
          <p role="alert" className="absolute top-2 left-2 animate-fade rounded-lg border border-loss/40 bg-panel px-3 py-1.5 text-sm text-loss shadow-float">
            {message}
          </p>
        )}
      </div>
    </section>
  );
}

// The symbol, the live bid and how it moved over the last 24 hours. A narrow chart, such as on a phone, keeps the
// symbol, the bid and the change in percent.
function QuoteHeader({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo }) {
  const { symbol, digits } = instrument;
  const price = useTradingStore((s) => s.prices[symbol]);
  const day = dayFigures(useDaySummary(accountId, symbol).data, price?.bid);
  const market = useMarket(accountId, symbol);
  const changeColor = day.change === null ? "text-muted" : day.change >= 0 ? "text-profit" : "text-loss";

  return (
    <div className="@container flex h-14 items-center gap-x-5 gap-y-1 overflow-hidden border-b border-border px-3">
      <span className="flex shrink-0 items-center gap-2">
        <SymbolIcon base={instrument.baseCurrency} quote={instrument.quoteCurrency} />
        <span className="text-base font-semibold">{symbol}</span>
        {market && isClosed(market) && <ClosedTag market={market} />}
      </span>
      <span className="flex shrink-0 items-baseline gap-2 whitespace-nowrap">
        <span className="font-mono text-xl font-semibold tabular-nums">{formatPrice(price?.bid, digits)}</span>
        {day.change !== null && (
          <span className={`font-mono text-sm tabular-nums ${changeColor}`} title="Change of the bid over 24 hours">
            <span className="@max-md:hidden">{formatSignedPrice(day.change, digits)} </span>({formatSignedPercent(day.changePercent)})
          </span>
        )}
      </span>
      <span className="flex gap-4 text-xs whitespace-nowrap @max-3xl:hidden">
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
