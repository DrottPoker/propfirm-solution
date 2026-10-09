"use client";

import {
  ColorType,
  createChart,
  HistogramSeries,
  LineStyle,
  TickMarkType,
  type IChartApi,
  type IPriceLine,
  type ISeriesApi,
  type LogicalRange,
  type MouseEventParams,
  type Time,
  type UTCTimestamp,
} from "lightweight-charts";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";

import { CommandRejectedError } from "@/lib/api/client";
import type { InstrumentInfo, Timeframe } from "@/lib/api/types";
import { applyPrice, mergeOlder, secondsOf, timeframes, toBar, type Bar } from "@/lib/candles";
import { useChartStudies } from "@/lib/chartStudies";
import { chartTrades } from "@/lib/chartTrades";
import { mix, withAlpha, type Palette } from "@/lib/colors";
import { dayFigures } from "@/lib/daySummary";
import { rejectionText } from "@/lib/events";
import { formatChartTick, formatMinute, formatPrice, formatSignedPercent, formatSignedPrice, timeZoneName, type ChartTick } from "@/lib/format";
import { orderBlockText, useOrderBlock } from "@/lib/orderBlock";
import { useOrderDraft, type GhostLine } from "@/lib/orderDraft";
import { parseVolume } from "@/lib/orderInput";
import { useProfile } from "@/lib/profileContext";
import { isClosed, useNow } from "@/lib/marketHours";
import { fetchOlderCandles, useCandles, useDaySummary, useMarket, useModifyStops, usePointValue } from "@/lib/queries";
import type { DrawingTool } from "@/lib/drawings";
import { chartTypes, useSettings, type CandleColors, type ChartType } from "@/lib/settings";
import { maxAlerts, useAlerts } from "@/lib/alerts";
import { useTradingStore } from "@/lib/store";
import { isTyping, timeframeOfKey } from "@/lib/shortcuts";
import { spreadText } from "@/lib/ticket";
import { volumeOf } from "@/lib/ticketStore";
import { usePalette } from "@/lib/theme";
import { useTimeZone } from "@/lib/timeZone";

import { AlertButton } from "./AlertButton";
import { ChartLegend } from "./ChartLegend";
import { ChartOrderQuestion, PhoneTradeBar, QuickTrade, useChartOrders } from "./ChartTrading";
import { ChartLines, offscreenTagId } from "./ChartLines";
import { isRemoved, markRemoved } from "./chartRemoval";
import { ClosedTag } from "./ClosedTag";
import { useDrawingTools } from "./drawingTools";
import { DrawingsLayer } from "./DrawingsLayer";
import {
  ArrowIcon,
  BarsIcon,
  CandlesIcon,
  ChevronDownIcon,
  CollapseIcon,
  DrawIcon,
  ExpandIcon,
  FibonacciIcon,
  HeikinAshiIcon,
  HorizontalLineIcon,
  IndicatorsIcon,
  LineChartIcon,
  QuickTradeIcon,
  RectangleIcon,
  TextIcon,
  TrashIcon,
  TrendLineIcon,
  VerticalLineIcon,
  VolumeBarsIcon,
} from "./icons";
import { NoteInput } from "./NoteInput";
import { IndicatorMenu } from "./IndicatorMenu";
import { IndicatorSeries } from "./IndicatorSeries";
import { MainSeries } from "./MainSeries";
import { Menu } from "./Menu";
import { OldPriceTag, useOldPrice } from "./PriceAlerts";
import { PriceMenu, type MenuOrders, type MenuPlacement, type StopLineRef } from "./PriceMenu";
import { grabDistance, StopHandles, usePositionLines } from "./positionLines";
import { SymbolIcon } from "./SymbolIcon";
import { showError } from "./TradeNotices";
import { TimeAxisEdges } from "./TimeAxisEdges";
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
  { tool: "vertical", label: "Vertical line", hint: "Click where the line goes.", Icon: VerticalLineIcon },
  { tool: "trend", label: "Trend line", hint: "Click where the line starts, then where it ends.", Icon: TrendLineIcon },
  { tool: "arrow", label: "Arrow", hint: "Click where the arrow starts, then where it points.", Icon: ArrowIcon },
  { tool: "rectangle", label: "Rectangle", hint: "Click one corner, then the opposite one.", Icon: RectangleIcon },
  { tool: "fibonacci", label: "Fibonacci retracement", hint: "Click the start of the move, then its end.", Icon: FibonacciIcon },
  { tool: "text", label: "Note", hint: "Click where the note goes, then write it.", Icon: TextIcon },
];

// The volume is a low band under the candles, so it is there to glance at without pulling the eye from the price.
const volumeBand = 0.13;
const priceMargins = { withVolume: { top: 0.08, bottom: 0.2 }, withoutVolume: { top: 0.08, bottom: 0.08 } };

// The buttons above the chart: the timeframes, the volume and full screen.
const toolClass = "shrink-0 rounded-md py-1 font-medium transition duration-150 ease-out-soft active:translate-y-px";

// The chart's colors in the theme: candles in the trader's colors on the panel (green and red unless chosen otherwise in
// Settings), and lines of our own, such as the last price, in brass (ADR 0058).
function chartColors(palette: Palette) {
  return {
    background: palette.panel,
    raised: palette.raised,
    text: palette.muted,
    grid: mix(palette.border, palette.panel, 0.45),
    border: palette.border,
    // The line between the chart and an indicator's pane, brass while dragged.
    separatorHover: withAlpha(palette.brass, 0.25),
    crosshair: mix(palette.muted, palette.panel, 0.6),
    last: palette.brass,
    // A line chart's line of closes.
    line: palette.foreground,
    // The trader's price alerts, apart from the brass of positions and the red and green of stops.
    alert: "#7aa7e0",
    // The price a buy opens at, quieter than the bid's brass line, since the candles are bids.
    ask: mix(palette.foreground, palette.panel, 0.55),
    open: palette.brass,
    // A pending order's price, dotted and a little dimmer than an open position's.
    order: mix(palette.brass, palette.panel, 0.25),
    stopLoss: palette.loss,
    takeProfit: palette.profit,
    buy: palette.profit,
    sell: palette.loss,
    // The indicators' lines in turn, apart from the candles' green and red and from brass, which marks our own lines.
    studies: ["#7aa7e0", "#b48ee6", "#5cc8c0", "#e08ab4", "#d9c38c", palette.foreground],
    studyGuide: mix(palette.muted, palette.panel, 0.6),
    drawing: {
      line: mix(palette.foreground, palette.panel, 0.75),
      fill: withAlpha(palette.foreground, 0.05),
      selected: palette.brass,
      label: mix(palette.foreground, palette.panel, 0.75),
      labelText: palette.panel,
    },
    // 75 % opaque, so the candles show through the line.
    tradeLine: withAlpha(palette.muted, 0.75),
    // The order being filled in, at half the strength of real lines.
    ghost: {
      entry: withAlpha(palette.brass, 0.5),
      stopLoss: withAlpha(palette.loss, 0.5),
      takeProfit: withAlpha(palette.profit, 0.5),
    } satisfies Record<GhostLine["kind"], string>,
    // The chart draws labels without transparency, so these are the ghost colors mixed half and half with the panel.
    ghostLabel: {
      entry: mix(palette.brass, palette.panel, 0.5),
      stopLoss: mix(palette.loss, palette.panel, 0.5),
      takeProfit: mix(palette.profit, palette.panel, 0.5),
    } satisfies Record<GhostLine["kind"], string>,
    ghostText: mix(palette.foreground, palette.panel, 0.75),
  };
}

type ChartColors = ReturnType<typeof chartColors>;

/**
 * Bid prices as candles, bars, a line or Heikin Ashi, with tick volume, live updates, a legend, arrows for trades and
 * lines for open positions and orders. Stop loss and take profit lines can be dragged to new levels.
 */
export function PriceChart({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo | null }) {
  // The terminal draws the chart anew when the theme changes, so the colors hold for its life.
  const palette = usePalette();
  const colors = useMemo(() => chartColors(palette), [palette]);
  const sectionRef = useRef<HTMLElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const mainRef = useRef<MainSeries | null>(null);
  const seriesRef = useRef<ISeriesApi<"Candlestick"> | null>(null);
  const volumeRef = useRef<ISeriesApi<"Histogram"> | null>(null);
  const markersRef = useRef<TradeMarkers | null>(null);
  const handlesRef = useRef<StopHandles | null>(null);
  const chartLinesRef = useRef<ChartLines | null>(null);
  const studiesRef = useRef<IndicatorSeries | null>(null);
  const drawingsRef = useRef<DrawingsLayer | null>(null);
  const edgesRef = useRef<TimeAxisEdges | null>(null);
  // The chart, once made, for the parts that follow it, such as the legend.
  const [chartApi, setChartApi] = useState<IChartApi | null>(null);
  // Every candle on the chart, the older ones loaded by scrolling back first. Live prices change the last one.
  const barsRef = useRef<Bar[]>([]);
  // The symbol and timeframe of the history on the chart. Live prices and older candles wait for it, so a price that
  // comes before the history, or one of a newly chosen symbol, never lands on candles that are not its own.
  const historyRef = useRef<string | null>(null);
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
  const showGrid = useSettings((s) => s.chartGrid);
  const showAsk = useSettings((s) => s.chartAsk);
  const showTrades = useSettings((s) => s.chartTrades);
  const candleColors = useSettings((s) => s.candles);
  const chartType = useSettings((s) => s.chartType);
  const changeSetting = useSettings((s) => s.change);
  const changeChartType = useSettings((s) => s.changeChartType);
  // The volume bars take the candles' colors, read when they are drawn.
  const candleColorsRef = useRef(candleColors);
  // Whether the history has tick volume, which the feed's older history lacks: then the band would only show its end.
  const [volumeKnown, setVolumeKnown] = useState(true);

  const symbol = instrument?.symbol ?? null;
  const candles = useCandles(accountId, symbol, timeframe);
  const timeZone = useTimeZone();

  // The chart lives as long as the component.
  useEffect(() => {
    const container = containerRef.current;
    if (!container) {
      return;
    }

    // The axes' figures in the text's typeface, with figures as wide as each other. The chart draws on a canvas, so it
    // needs the name.
    const fontFamily = getComputedStyle(container).getPropertyValue("--font-text").trim();
    const chart = createChart(container, {
      autoSize: true,
      layout: {
        background: { type: ColorType.Solid, color: colors.background },
        textColor: colors.text,
        fontSize: 11,
        ...(fontFamily ? { fontFamily } : {}),
        // TradingView is named with a link in About instead, as its licence allows (ADR 0058).
        attributionLogo: false,
        panes: { separatorColor: colors.border, separatorHoverColor: colors.separatorHover, enableResize: true },
      },
      grid: { vertLines: { color: colors.grid }, horzLines: { color: colors.grid } },
      crosshair: {
        vertLine: { color: colors.crosshair, labelBackgroundColor: colors.raised },
        horzLine: { color: colors.crosshair, labelBackgroundColor: colors.raised },
      },
      rightPriceScale: { borderColor: colors.border, scaleMargins: priceMargins.withVolume },
      timeScale: {
        borderColor: colors.border,
        timeVisible: true,
        secondsVisible: false,
        barSpacing: defaultBarSpacing,
        rightOffset: defaultRightOffset,
        // The chart never scrolls past its first candle, so no empty space opens on the left and the first time on
        // the axis stays whole. Older candles load before the edge is reached.
        fixLeftEdge: true,
      },
    });
    const main = new MainSeries(chart, { candles: candleColorsRef.current, last: colors.last, line: colors.line });
    const series = main.candles;
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
    const chartLines = new ChartLines({ tag: colors.raised, axis: colors.background, text: colors.text }, fontFamily || "sans-serif");
    series.attachPrimitive(chartLines);
    const drawingsLayer = new DrawingsLayer(colors.drawing, `12px ${fontFamily || "sans-serif"}`);
    series.attachPrimitive(drawingsLayer);
    const edges = new TimeAxisEdges(colors.background, `11px ${fontFamily || "sans-serif"}`);
    series.attachPrimitive(edges);
    // The chart formats the times in view again after every move, so the edges forget the ones before.
    const onRangeChange = () => edges.forget();
    chart.timeScale().subscribeVisibleLogicalRangeChange(onRangeChange);
    const studies = new IndicatorSeries(chart, { lines: colors.studies, ...studyUpDown(candleColorsRef.current), guide: colors.studyGuide });

    // A press on a tag at the edge for lines beyond the view lets the scale take them in, until the chart is reset.
    const onClick = (param: MouseEventParams<Time>) => {
      if (param.hoveredInfo?.objectId === offscreenTagId) {
        chartLines.showAll();
      }
    };
    chart.subscribeClick(onClick);

    chartRef.current = chart;
    mainRef.current = main;
    seriesRef.current = series;
    volumeRef.current = volume;
    markersRef.current = markers;
    handlesRef.current = handles;
    chartLinesRef.current = chartLines;
    drawingsRef.current = drawingsLayer;
    edgesRef.current = edges;
    studiesRef.current = studies;
    setChartApi(chart);

    return () => {
      chart.unsubscribeClick(onClick);
      chart.timeScale().unsubscribeVisibleLogicalRangeChange(onRangeChange);
      markRemoved(chart, series);
      chart.remove();
      chartRef.current = null;
      mainRef.current = null;
      seriesRef.current = null;
      volumeRef.current = null;
      markersRef.current = null;
      handlesRef.current = null;
      chartLinesRef.current = null;
      drawingsRef.current = null;
      edgesRef.current = null;
      studiesRef.current = null;
      setChartApi(null);
    };
  }, [colors]);

  // The indicators are the same on every symbol, with the instrument's decimals.
  const digits = instrument?.digits ?? 5;
  useEffect(() => {
    studiesRef.current?.setIndicators(indicators, digits);
  }, [indicators, digits]);

  useEffect(() => {
    mainRef.current?.setType(chartType);
  }, [chartType]);

  // The trader's candle colors, also on the volume bars already drawn.
  useEffect(() => {
    candleColorsRef.current = candleColors;
    mainRef.current?.setColors({ candles: candleColors, last: colors.last, line: colors.line });
    volumeRef.current?.setData(barsRef.current.map((bar) => toVolume(bar, candleColors)));
    const { up, down } = studyUpDown(candleColors);
    studiesRef.current?.setUpDown(up, down);
  }, [candleColors, colors]);

  useEffect(() => {
    chartRef.current?.applyOptions({ grid: { vertLines: { visible: showGrid }, horzLines: { visible: showGrid } } });
  }, [showGrid]);

  // The ask as a line of its own, which follows every new price, since a buy opens there and the candles are bids.
  useEffect(() => {
    const series = seriesRef.current;
    if (!series || !symbol || !showAsk) {
      return;
    }

    let line: IPriceLine | null = null;
    const place = (ask: number | undefined) => {
      if (ask === undefined) {
        return;
      }

      if (line) {
        line.applyOptions({ price: ask });
      } else {
        line = series.createPriceLine({ price: ask, color: colors.ask, lineWidth: 1, lineStyle: LineStyle.Dashed, axisLabelVisible: true, title: "Ask" });
      }
    };
    place(useTradingStore.getState().prices[symbol]?.ask);
    const stop = useTradingStore.subscribe((state, previous) => {
      const price = state.prices[symbol];
      if (price && price !== previous.prices[symbol]) {
        place(price.ask);
      }
    });
    return () => {
      stop();
      if (line && !isRemoved(series)) {
        series.removePriceLine(line);
      }
    };
  }, [symbol, showAsk, colors]);

  // Without the volume, or without tick volume in the history, the candles take its place.
  const volumeShown = showVolume && volumeKnown;
  useEffect(() => {
    volumeRef.current?.applyOptions({ visible: volumeShown });
    chartRef.current?.priceScale("right").applyOptions({ scaleMargins: volumeShown ? priceMargins.withVolume : priceMargins.withoutVolume });
  }, [volumeShown]);

  // Bars stay in UTC seconds; only the labels are in the account's time zone, like every other time on screen.
  useEffect(() => {
    chartRef.current?.applyOptions({
      localization: { timeFormatter: (time: Time) => (typeof time === "number" ? formatMinute(new Date(time * 1000), timeZone) : String(time)) },
      timeScale: {
        tickMarkFormatter: (time: Time, tick: TickMarkType) => {
          if (typeof time !== "number") {
            return null;
          }

          // Told to the edges, which cover a time cut at the axis' ends.
          const label = formatChartTick(time, chartTicks[tick], timeZone);
          edgesRef.current?.record(time, label);
          return label;
        },
      },
    });
  }, [timeZone]);

  useEffect(() => {
    if (instrument) {
      mainRef.current?.setPriceFormat(instrument.digits);
    }
  }, [instrument]);

  // History replaces everything on the chart.
  useEffect(() => {
    if (!mainRef.current || !volumeRef.current || !candles.data) {
      return;
    }

    const bars = candles.data.map(toBar);
    barsRef.current = bars;
    historyRef.current = historyKey(symbol, timeframe);
    olderRef.current = { loading: false, exhausted: false };
    mainRef.current.setData(bars);
    volumeRef.current.setData(bars.map((bar) => toVolume(bar, candleColorsRef.current)));
    setVolumeKnown(hasTickVolume(bars));
    studiesRef.current?.setBars(bars);
    drawingsRef.current?.setBars(bars.map((b) => b.time), secondsOf(timeframe), digits);
    chartLinesRef.current?.setLastPrice(bars.at(-1)?.close ?? null);
    showLatest(chartRef.current, bars.length);
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
      const ready = historyRef.current === historyKey(symbol, timeframe);
      if (!ready || !range || range.from > olderThreshold || state.loading || state.exhausted || bars.length === 0) {
        return;
      }

      state.loading = true;
      fetchOlderCandles(accountId, symbol, timeframe, bars[0].time)
        .then((candles) => {
          // A new history replaced the candles meanwhile.
          if (!current || barsRef.current !== bars || !mainRef.current || !volumeRef.current) {
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
          mainRef.current.setData(merged);
          volumeRef.current.setData(merged.map((bar) => toVolume(bar, candleColorsRef.current)));
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
      if (!isRemoved(chart)) {
        chart.timeScale().unsubscribeVisibleLogicalRangeChange(onRange);
      }
    };
  }, [accountId, symbol, timeframe, digits]);

  // Live prices update the latest bar without re-rendering React.
  useEffect(() => {
    if (!symbol) {
      return;
    }

    return useTradingStore.subscribe((state, previous) => {
      const price = state.prices[symbol];
      if (!price || price === previous.prices[symbol] || !mainRef.current || !volumeRef.current || historyRef.current !== historyKey(symbol, timeframe)) {
        return;
      }

      const bars = barsRef.current;
      const last = bars.at(-1);
      const bar = applyPrice(last, price.bid, Date.parse(price.timestamp) / 1000, timeframe);
      if (bar) {
        if (last && bar.time === last.time) {
          bars[bars.length - 1] = bar;
        } else {
          bars.push(bar);
          drawingsRef.current?.setBars(bars.map((b) => b.time), secondsOf(timeframe), digits);
        }
        mainRef.current.update(bars);
        volumeRef.current.update(toVolume(bar, candleColorsRef.current));
        chartLinesRef.current?.setLastPrice(bar.close);
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
    chartLinesRef,
    containerRef,
    onError: showMessage,
  });
  useGhostLines(seriesRef, chartLinesRef, symbol, colors);
  useAlertLines(seriesRef, chartLinesRef, symbol, digits, colors);
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
  const resetChart = useResetChart(chartRef, chartLinesRef);

  // Orders from the chart's buttons and right-click menu, which ask first when the order panel does (ADR 0058).
  const chartTrading = useSettings((s) => s.chartTrading);
  const chartOrders = useChartOrders(accountId);
  const { block: orderBlock } = useOrderBlock(accountId, symbol);
  const profile = useProfile();
  const menuOrders = (shown: InstrumentInfo, at: MenuPlacement): MenuOrders => {
    if (orderBlock) {
      return { ok: false, reason: orderBlockText(orderBlock, shown.symbol) };
    }

    const volume = parseVolume(volumeOf(shown, profile), shown);
    return volume.ok && volume.value !== null
      ? { ok: true, volume: volume.value, place: (order) => chartOrders.place(order, at) }
      : { ok: false, reason: "Set a valid volume in the order panel first." };
  };

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
  useTradeMarkers(markersRef, showTrades ? symbol : null, timeframe);

  const changeTimeframe = useCallback((next: Timeframe) => {
    setTimeframe(next);
    // Each timeframe opens at the latest candles, at the scale that follows them.
    chartLinesRef.current?.reset();
  }, []);

  // The digits choose the timeframes in order, 1 for M1 (ADR 0058), unless a field is being typed in.
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      const next = e.altKey || e.ctrlKey || e.metaKey || isTyping(e.target) ? null : timeframeOfKey(e.key);
      if (next) {
        e.preventDefault();
        changeTimeframe(next);
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [changeTimeframe]);
  const ChartTypeIcon = chartTypeIcons[chartType];

  return (
    <section ref={sectionRef} data-tour="chart" className="flex min-w-0 flex-col overflow-hidden bg-panel">
      {instrument ? <QuoteHeader accountId={accountId} instrument={instrument} /> : <div className="h-12 border-b border-border" />}

      <div className="@container flex h-9 shrink-0 items-center gap-0.5 border-b border-border px-2 text-xs">
        {/* Every timeframe as a button where there is room, otherwise the chosen one opens a list of them. */}
        <span className="flex items-center gap-0.5 @max-2xl:hidden" role="group" aria-label="Timeframe">
          {timeframes.map((tf) => (
            <button
              key={tf}
              type="button"
              onClick={() => changeTimeframe(tf)}
              aria-pressed={tf === timeframe}
              title={timeframeNames[tf]}
              className={`${toolClass} px-2 ${tf === timeframe ? "bg-accent/15 text-accent" : "text-muted hover:bg-raised hover:text-foreground"}`}
            >
              {tf}
            </button>
          ))}
        </span>
        <span className="@2xl:hidden">
          <Menu
            label="Timeframe"
            buttonLabel={`Timeframe ${timeframe}`}
            title="Timeframe"
            button={
              <span className="flex items-center gap-1">
                {timeframe}
                <ChevronDownIcon className="size-3 text-muted" />
              </span>
            }
            items={timeframes.map((tf) => ({ value: tf, label: tf, hint: timeframeNames[tf] }))}
            value={timeframe}
            onChoose={changeTimeframe}
            className={`${toolClass} px-2 text-accent hover:bg-raised`}
          />
        </span>
        <Divider />
        <Menu
          label="Chart type"
          buttonLabel="Chart type"
          title={`Chart type: ${chartTypes.find((t) => t.type === chartType)?.name}`}
          button={<ChartTypeIcon className="size-4" />}
          items={chartTypes.map((t) => {
            const Icon = chartTypeIcons[t.type];
            return { value: t.type, label: t.name, icon: <Icon className="size-4" /> };
          })}
          value={chartType}
          onChoose={changeChartType}
          className={`${toolClass} p-1.5 text-muted hover:bg-raised hover:text-foreground`}
        />
        <button
          type="button"
          onClick={() => setIndicatorsOpen((open) => !open)}
          aria-expanded={indicatorsOpen}
          title="Moving averages, Bollinger Bands, RSI and MACD"
          className={`${toolClass} flex items-center gap-1.5 px-2 ${indicatorsOpen || indicators.length > 0 ? "text-foreground" : "text-muted hover:bg-raised hover:text-foreground"}`}
        >
          <IndicatorsIcon className={`size-4 ${indicators.length > 0 ? "text-accent" : ""}`} />
          <span className="@max-lg:sr-only">Indicators{indicators.length > 0 ? ` (${indicators.length})` : ""}</span>
        </button>
        <Divider />
        {/* The drawing tools in one list, the tool in use shown on the button. */}
        <Menu
          label="Drawing tools"
          buttonLabel="Drawing tools"
          title={activeTool ? `Drawing: ${activeTool.label}. Esc to stop.` : "Draw on the chart"}
          button={
            <span className="flex items-center gap-1.5">
              {activeTool ? <activeTool.Icon className="size-4" /> : <DrawIcon className="size-4" />}
              <span className="@max-xl:sr-only">Draw</span>
            </span>
          }
          items={drawingTools.map(({ tool, label, Icon }) => ({ value: tool, label, icon: <Icon className="size-4" /> }))}
          value={drawing.tool}
          onChoose={(tool) => drawing.chooseTool(tool)}
          className={`${toolClass} px-2 ${activeTool ? "bg-accent/15 text-accent" : "text-muted hover:bg-raised hover:text-foreground"}`}
        />
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
          <TrashIcon className="size-4" />
        </button>
        {instrument && (
          <AlertButton
            symbol={instrument.symbol}
            digits={digits}
            onAdd={addAlert}
            className={`${toolClass} p-1.5 text-muted hover:bg-raised hover:text-foreground`}
          />
        )}
        <span className="ml-auto flex shrink-0 items-center gap-0.5">
          <button
            type="button"
            onClick={() => changeSetting("chartVolume", !showVolume)}
            aria-pressed={showVolume}
            disabled={!volumeKnown}
            title={
              !volumeKnown
                ? "This part of the history has no tick volume"
                : showVolume
                  ? "Hide the tick volume under the candles"
                  : "Show the tick volume under the candles"
            }
            className={`${toolClass} flex items-center gap-1.5 px-2 disabled:opacity-40 ${volumeShown ? "text-foreground" : "text-muted hover:bg-raised hover:text-foreground"}`}
          >
            <VolumeBarsIcon className={`size-4 ${volumeShown ? "text-accent" : ""}`} />
            <span className="@max-3xl:sr-only">Volume</span>
          </button>
          <button
            type="button"
            onClick={() => changeSetting("chartTrading", !chartTrading)}
            aria-pressed={chartTrading}
            title={chartTrading ? "Hide the buy and sell buttons on the chart" : "Buy and sell from the chart, also in full screen"}
            className={`${toolClass} flex items-center gap-1.5 px-2 ${chartTrading ? "text-foreground" : "text-muted hover:bg-raised hover:text-foreground"}`}
          >
            <QuickTradeIcon className={`size-4 ${chartTrading ? "text-accent" : ""}`} />
            <span className="@max-3xl:sr-only">Trade</span>
          </button>
          <button
            type="button"
            onClick={fullScreen.toggle}
            aria-label={fullScreen.active ? "Exit full screen" : "Full screen"}
            title={fullScreen.active ? "Exit full screen (Esc)" : "Full screen"}
            className={`${toolClass} p-1.5 text-muted hover:bg-raised hover:text-foreground`}
          >
            {fullScreen.active ? <CollapseIcon /> : <ExpandIcon />}
          </button>
        </span>
      </div>

      <div className="relative min-h-0 flex-1">
        <div ref={containerRef} className="absolute inset-0" />
        {instrument && (
          // The buy and sell buttons first, as in other platforms, and the legend under them.
          <div className="pointer-events-none absolute top-1.5 left-2.5 z-10 flex max-w-[calc(100%-6rem)] flex-col items-start gap-1.5">
            {chartTrading && (
              <span className="max-lg:hidden">
                <QuickTrade accountId={accountId} instrument={instrument} onOrder={(order) => chartOrders.place(order)} />
              </span>
            )}
            <ChartLegend
              chart={chartApi}
              bars={() => barsRef.current}
              studies={() => studiesRef.current}
              symbol={instrument.symbol}
              digits={digits}
              chartType={chartType}
              colors={colors.studies}
              onEditIndicators={() => setIndicatorsOpen(true)}
            />
          </div>
        )}
        {chartOrders.asking && chartOrders.asking.order.symbol === symbol && (
          <ChartOrderQuestion
            asking={chartOrders.asking}
            digits={digits}
            disabled={orderBlock !== null || chartOrders.pending}
            onConfirm={chartOrders.confirm}
            onCancel={chartOrders.cancel}
          />
        )}
        {indicatorsOpen && <IndicatorMenu colors={colors.studies} onClose={closeIndicators} />}
        {drawing.note && (
          <NoteInput
            key={drawing.note.id ?? "new"}
            at={drawing.note.pixel}
            text={drawing.note.text}
            onSave={drawing.saveNote}
            onCancel={drawing.cancelNote}
          />
        )}
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
            alertId={menu.alertId}
            orders={menuOrders(instrument, menu)}
            onModify={modifyStops}
            onResetChart={resetChart}
            onAddAlert={(price) => addAlert(instrument.symbol, price)}
            onRemoveAlert={(id) => useAlerts.getState().remove(id)}
            onAddLine={drawing.addLine}
            onClose={closeMenu}
          />
        )}
        {message && (
          <p role="alert" className="absolute top-10 left-1/2 z-10 -translate-x-1/2 animate-fade rounded-lg border border-loss/40 bg-panel px-3 py-1.5 text-sm text-loss shadow-float">
            {message}
          </p>
        )}
      </div>
      {instrument && (
        <div className="shrink-0 border-t border-border lg:hidden">
          <PhoneTradeBar accountId={accountId} instrument={instrument} />
        </div>
      )}
    </section>
  );
}

function Divider() {
  return <span aria-hidden="true" className="mx-1 h-4 w-px shrink-0 bg-border" />;
}

const timeframeNames: Record<Timeframe, string> = {
  M1: "1 minute",
  M5: "5 minutes",
  M15: "15 minutes",
  M30: "30 minutes",
  H1: "1 hour",
  H4: "4 hours",
  D1: "1 day",
  W1: "1 week",
  MN: "1 month",
};

const chartTypeIcons: Record<ChartType, (props: { className?: string }) => React.ReactNode> = {
  candles: CandlesIcon,
  bars: BarsIcon,
  line: LineChartIcon,
  heikinAshi: HeikinAshiIcon,
};

function historyKey(symbol: string | null, timeframe: Timeframe): string {
  return `${symbol}/${timeframe}`;
}

/** Whether most of the bars have tick volume. The feed's older history has none, so its band would show only the end. */
export function hasTickVolume(bars: readonly Bar[]): boolean {
  return bars.length > 0 && bars.filter((b) => b.ticks > 0).length >= bars.length / 2;
}

/**
 * The latest candles at the default spacing, or every candle when they are too few to fill the chart, as on a monthly
 * chart of a short history, so the chart never opens half empty.
 */
function showLatest(chart: IChartApi | null, count: number) {
  if (!chart) {
    return;
  }

  const timeScale = chart.timeScale();
  if (count * defaultBarSpacing < timeScale.width()) {
    timeScale.fitContent();
  } else {
    timeScale.applyOptions({ barSpacing: defaultBarSpacing });
    timeScale.scrollToRealTime();
  }
}

// The symbol, the live bid and how it moved over the last 24 hours. A narrow chart, such as on a phone, keeps the
// symbol, the bid and the change in percent.
function QuoteHeader({ accountId, instrument }: { accountId: string; instrument: InstrumentInfo }) {
  const { symbol, digits } = instrument;
  const price = useTradingStore((s) => s.prices[symbol]);
  const day = dayFigures(useDaySummary(accountId, symbol).data, price?.bid);
  const market = useMarket(accountId, symbol);
  const oldFor = useOldPrice(accountId, symbol, useNow(5_000));
  const changeColor = day.change === null ? "text-muted" : day.change >= 0 ? "text-profit" : "text-loss";
  const timeZone = useTimeZone();

  return (
    <div className="@container flex h-14 items-center gap-x-5 gap-y-1 overflow-hidden border-b border-border px-3">
      <span className="flex shrink-0 items-center gap-2">
        <SymbolIcon base={instrument.baseCurrency} quote={instrument.quoteCurrency} />
        <span className="text-base font-semibold">{symbol}</span>
        {market && isClosed(market) && <ClosedTag market={market} />}
        {oldFor !== null && <OldPriceTag ageMs={oldFor} />}
      </span>
      <span className="flex shrink-0 items-baseline gap-2 whitespace-nowrap">
        <span className="live text-xl font-semibold" title={`The bid, the price a sell closes at. The chart shows bid prices, in ${timeZoneName(timeZone)}.`}>
          {formatPrice(price?.bid, digits)}
        </span>
        {day.change !== null && (
          <span className={`live text-sm tabular-nums ${changeColor}`} title="Change of the bid over 24 hours">
            <span className="@max-md:hidden">{formatSignedPrice(day.change, digits)} </span>({formatSignedPercent(day.changePercent)})
          </span>
        )}
      </span>
      <span className="flex gap-4 text-xs whitespace-nowrap @max-3xl:hidden">
        <Stat label="24h high" value={formatPrice(day.high, digits)} />
        <Stat label="24h low" value={formatPrice(day.low, digits)} />
        <Stat label="Spread" value={price ? spreadText(price, instrument) : "-"} />
      </span>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <span className="flex gap-1.5">
      <span className="text-muted">{label}</span>
      <span className="tabular-nums">{value}</span>
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
function useResetChart(chartRef: React.RefObject<IChartApi | null>, chartLinesRef: React.RefObject<ChartLines | null>) {
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
    chartLinesRef.current?.reset();
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
  }, [chartRef, chartLinesRef]);

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
  alertId: string | null;
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

      // A price alert's line, which the menu offers to remove.
      const near = (price: number) => Math.abs((series?.priceToCoordinate(price) ?? Infinity) - y) <= grabDistance;
      const alert = line ? undefined : useAlerts.getState().alerts.find((a) => a.symbol === current.symbol && near(a.price));

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
      const price = (key ? handlesRef.current?.priceOf(key) : null) ?? ghost?.price ?? alert?.price ?? Math.round(wanted * scale) / scale;
      if (price <= 0) {
        return;
      }

      e.preventDefault();
      setMenu({
        x,
        y,
        flipX: x > rect.width - 260,
        flipY: y > rect.height / 2,
        symbol: current.symbol,
        price,
        line,
        ghost: ghost?.kind ?? null,
        alertId: alert?.id ?? null,
      });
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

/** Adds a price alert, set from where the bid is now, and says so. */
function addAlert(symbol: string, price: number) {
  const bid = useTradingStore.getState().prices[symbol]?.bid;
  if (bid === undefined) {
    showError(`There is no ${symbol} price yet to set an alert from.`);
    return;
  }

  if (!useAlerts.getState().add(symbol, price, bid)) {
    showError(`At most ${maxAlerts} alerts at a time. Remove one under Alerts first.`);
  }
}

// The price alerts of the symbol as dashed lines with a bell's name on the axis. A line beyond the view is told at its
// edge, like the lines of positions.
function useAlertLines(
  seriesRef: React.RefObject<ISeriesApi<"Candlestick"> | null>,
  chartLinesRef: React.RefObject<ChartLines | null>,
  symbol: string | null,
  digits: number,
  colors: ChartColors,
) {
  const alerts = useAlerts((s) => s.alerts);
  const key = JSON.stringify(alerts.filter((a) => a.symbol === symbol).map((a) => a.price));

  useEffect(() => {
    const series = seriesRef.current;
    if (!series) {
      return;
    }

    const prices = JSON.parse(key) as number[];
    const lines = prices.map((price) =>
      series.createPriceLine({ price, color: colors.alert, lineWidth: 1, lineStyle: LineStyle.SparseDotted, title: "Alert", axisLabelColor: colors.alert, axisLabelTextColor: colors.background }),
    );
    chartLinesRef.current?.set("alerts", prices.map((price) => ({ price, label: `Alert ${price.toFixed(digits)}`, color: colors.alert })));
    return () => {
      if (!isRemoved(series)) {
        lines.forEach((line) => series.removePriceLine(line));
      }
    };
  }, [seriesRef, chartLinesRef, key, digits, colors]);
}

const noGhosts: GhostLine[] = [];

// Faint lines where the order being filled in would put its price, stop loss and take profit. Lines move in place as
// the price moves, and are only created or removed when one appears or goes away.
function useGhostLines(
  seriesRef: React.RefObject<ISeriesApi<"Candlestick"> | null>,
  chartLinesRef: React.RefObject<ChartLines | null>,
  symbol: string | null,
  colors: ChartColors,
) {
  // A position or an order being changed from the tables shows its new levels instead of the order panel's.
  const ghosts = useOrderDraft((s) => (s.edit ? (s.edit.symbol === symbol ? s.edit.lines : noGhosts) : s.symbol === symbol ? s.lines : noGhosts));
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

    chartLinesRef.current?.set("ghosts", ghosts.map((g) => ({ price: g.price, label: g.title, color: colors.ghostLabel[g.kind] })));
  }, [seriesRef, chartLinesRef, ghosts, colors]);
}

// Arrows where the symbol's positions opened and closed, none without a symbol. Redrawn only when a trade or the
// timeframe changed.
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

// MACD's bars above and below zero, in the candles' colors but lighter.
function studyUpDown({ up, down }: CandleColors) {
  return { up: withAlpha(up, 0.55), down: withAlpha(down, 0.55) };
}

// Faint, since every candle has about as many prices and full colors would look like a barcode.
function toVolume(bar: Bar, { up, down }: CandleColors) {
  return { time: bar.time as UTCTimestamp, value: bar.ticks, color: withAlpha(bar.close >= bar.open ? up : down, 0.14) };
}
