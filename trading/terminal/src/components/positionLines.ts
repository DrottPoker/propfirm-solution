import type {
  IChartApi,
  IPriceLine,
  ISeriesApi,
  ISeriesPrimitive,
  PrimitiveHoveredItem,
  SeriesAttachedParameter,
  Time,
} from "lightweight-charts";
import { useEffect, useRef } from "react";

import { CommandRejectedError } from "@/lib/api/client";
import type { InstrumentInfo, OrderSnapshot, PointValue, PositionSnapshot, Side } from "@/lib/api/types";
import { rejectionText } from "@/lib/events";
import { formatSignedMoney, formatVolume } from "@/lib/format";
import { trailingPips } from "@/lib/orderTools";
import { useModifyOrder, useModifyStops } from "@/lib/queries";
import { clampEntry, clampStop, estimatedProfit, type StopKind } from "@/lib/stops";
import { useTradingStore } from "@/lib/store";

import type { ChartLines } from "./ChartLines";
import { isRemoved } from "./chartRemoval";

export interface PositionLineColors {
  open: string;
  stopLoss: string;
  takeProfit: string;
  order: string;
}

/** A line of a position or a pending order: its open or order price, its stop loss or its take profit. */
type LineKind = "open" | "entry" | StopKind;

interface LineSpec {
  key: string;
  owner: "position" | "order";
  id: string;
  kind: LineKind;
  price: number;
  color: string;
  style: number;
  title: string;
}

interface Drag {
  key: string;
  owner: "position" | "order";
  id: string;
  kind: "entry" | StopKind;
  original: number;
  price: number;
  pointerId: number;
}

interface Latest {
  positions: PositionSnapshot[];
  orders: OrderSnapshot[];
  symbol: string | null;
  digits: number;
  pointValue: PointValue | null | undefined;
  modify: ReturnType<typeof useModifyStops>;
  modifyOrder: ReturnType<typeof useModifyOrder>;
  onError: (text: string) => void;
}

type Lines = Map<string, { line: IPriceLine; spec: LineSpec }>;

// How close to a line, in pixels, a press grabs it.
export const grabDistance = 5;

// Line styles of the chart library: solid, dotted and dashed.
const solid = 0;
const dotted = 1;
const dashed = 2;

/**
 * Keys of a position's lines are its id and the kind, "id:stopLoss", which the right-click menu reads. A pending
 * order's lines use "|", "order|id|entry", so the menu never takes them for a position's.
 */
const positionKey = (positionId: string, kind: LineKind) => `${positionId}:${kind}`;
const orderKey = (orderId: string, kind: LineKind) => `order|${orderId}|${kind}`;

/** Tells the chart that the stop loss and take profit lines can be grabbed, so it shows a resize cursor over them. */
export class StopHandles implements ISeriesPrimitive<Time> {
  private param: SeriesAttachedParameter<Time> | null = null;
  private targets: readonly { key: string; price: number }[] = [];

  attached(param: SeriesAttachedParameter<Time>) {
    this.param = param;
  }

  detached() {
    this.param = null;
  }

  setTargets(targets: readonly { key: string; price: number }[]) {
    this.targets = targets;
  }

  /** The level of a stop line, by its key. */
  priceOf(key: string): number | null {
    return this.targets.find((t) => t.key === key)?.price ?? null;
  }

  /** The key of the line nearest to the y coordinate, if one is within reach. */
  targetAt(y: number): string | null {
    let best: { key: string; distance: number } | null = null;
    for (const target of this.targets) {
      const lineY = this.param?.series.priceToCoordinate(target.price);
      const distance = lineY == null ? Infinity : Math.abs(lineY - y);
      if (distance <= grabDistance && (best === null || distance < best.distance)) {
        best = { key: target.key, distance };
      }
    }
    return best?.key ?? null;
  }

  hitTest(_x: number, y: number): PrimitiveHoveredItem | null {
    const key = this.targetAt(y);
    return key ? { cursorStyle: "ns-resize", externalId: key, zOrder: "top" } : null;
  }
}

/**
 * Lines for the symbol's positions and pending orders. A position has its open price, stop loss and take profit; a
 * pending order its price, dotted, and its stop loss and take profit. Stop lines show the estimated result at their
 * level. While the market is open, stops and order prices can be dragged to a new level, which is sent to the engine
 * when the mouse is released. An order's stops move with its price, at the same distance. A line beyond the view is
 * told at its edge, and a press there brings every line into view (ADR 0058).
 */
export function usePositionLines({
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
  onError,
}: {
  accountId: string;
  instrument: InstrumentInfo | null;
  pointValue: PointValue | null | undefined;
  marketOpen: boolean;
  colors: PositionLineColors;
  chartRef: React.RefObject<IChartApi | null>;
  seriesRef: React.RefObject<ISeriesApi<"Candlestick"> | null>;
  handlesRef: React.RefObject<StopHandles | null>;
  chartLinesRef: React.RefObject<ChartLines | null>;
  containerRef: React.RefObject<HTMLDivElement | null>;
  onError: (text: string) => void;
}) {
  const symbol = instrument?.symbol ?? null;
  const digits = instrument?.digits ?? 5;
  const positions = useTradingStore((s) => s.account?.positions);
  const orders = useTradingStore((s) => s.account?.orders);
  const modify = useModifyStops(accountId);
  const modifyOrder = useModifyOrder(accountId);

  const linesRef = useRef<Lines>(new Map());
  const dragRef = useRef<Drag | null>(null);
  // The latest values for the pointer handlers, which are attached once.
  const latest = useRef<Latest>({ positions: [], orders: [], symbol, digits, pointValue, modify, modifyOrder, onError });

  const symbolPositions = (positions ?? []).filter((p) => p.symbol === symbol);
  const symbolOrders = (orders ?? []).filter((o) => o.symbol === symbol);
  const stopColor = (kind: StopKind) => (kind === "stopLoss" ? colors.stopLoss : colors.takeProfit);
  const specs: LineSpec[] = [
    ...symbolPositions.flatMap((p): LineSpec[] => [
      { key: positionKey(p.positionId, "open"), owner: "position", id: p.positionId, kind: "open", price: p.openPrice, color: colors.open, style: solid, title: `${p.side} ${formatVolume(p.volume)}` },
      ...stopSpecs(p.stopLoss, p.takeProfit).map(([kind, price]) => ({
        key: positionKey(p.positionId, kind),
        owner: "position" as const,
        id: p.positionId,
        kind,
        price,
        color: stopColor(kind),
        style: dashed,
        title: stopTitle(kind, p.side, p.volume, p.openPrice, price, digits, pointValue, kind === "stopLoss" ? p.trailingDistance : null),
      })),
    ]),
    ...symbolOrders.flatMap((o): LineSpec[] => [
      { key: orderKey(o.orderId, "entry"), owner: "order", id: o.orderId, kind: "entry", price: o.price, color: colors.order, style: dotted, title: orderTitle(o) },
      ...stopSpecs(o.stopLoss, o.takeProfit).map(([kind, price]) => ({
        key: orderKey(o.orderId, kind),
        owner: "order" as const,
        id: o.orderId,
        kind,
        price,
        color: stopColor(kind),
        style: dotted,
        title: stopTitle(kind, o.side, o.volume, o.price, price, digits, pointValue, kind === "stopLoss" ? o.trailingDistance : null),
      })),
    ]),
  ];
  const specsKey = JSON.stringify(specs);

  useEffect(() => {
    latest.current = { positions: symbolPositions, orders: symbolOrders, symbol, digits, pointValue, modify, modifyOrder, onError };
  });

  // Redraws the lines only when a position, an order, a stop or the point value actually changed.
  useEffect(() => {
    const series = seriesRef.current;
    if (!series) {
      return;
    }

    const lines = linesRef.current;
    const parsed = JSON.parse(specsKey) as LineSpec[];
    for (const spec of parsed) {
      const line = series.createPriceLine({
        price: spec.price,
        color: spec.color,
        lineWidth: 1,
        lineStyle: spec.style,
        title: spec.title,
      });
      lines.set(spec.key, { line, spec });
    }

    // A line being dragged stays where the pointer is.
    const drag = dragRef.current;
    if (drag) {
      showDragged(lines, latest.current, drag);
    }

    // A line beyond the view is told at its edge, rather than squeezing the candles to make room for it.
    chartLinesRef.current?.set("trades", parsed.map((s) => ({ price: s.price, label: s.title, color: s.color })));

    return () => {
      if (!isRemoved(series)) {
        for (const { line } of lines.values()) {
          series.removePriceLine(line);
        }
      }
      lines.clear();
    };
  }, [seriesRef, handlesRef, chartLinesRef, specsKey]);

  // Lines can be grabbed only while the market is open, since the engine refuses changes when it is closed.
  useEffect(() => {
    const grabbable = marketOpen ? (JSON.parse(specsKey) as LineSpec[]).filter((s) => s.kind !== "open") : [];
    handlesRef.current?.setTargets(grabbable.map((s) => ({ key: s.key, price: s.price })));
  }, [handlesRef, specsKey, marketOpen]);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) {
      return;
    }

    const yOf = (e: PointerEvent) => e.clientY - container.getBoundingClientRect().top;

    const onPointerDown = (e: PointerEvent) => {
      const chart = chartRef.current;
      const handles = handlesRef.current;
      const state = useTradingStore.getState();
      // Stops can be moved on a suspended account too, since that only protects positions already open.
      if (e.button !== 0 || !chart || !handles || state.connection !== "connected" || !state.account || state.account.status === "Disabled") {
        return;
      }

      // Presses on the price axis scale the chart as usual.
      if (e.clientX - container.getBoundingClientRect().left > chart.timeScale().width()) {
        return;
      }

      const key = handles.targetAt(yOf(e));
      const entry = key ? linesRef.current.get(key) : undefined;
      if (!key || !entry || entry.spec.kind === "open") {
        return;
      }

      // Cancelling the press keeps the chart from also panning while the line moves.
      e.preventDefault();
      e.stopPropagation();
      container.setPointerCapture(e.pointerId);
      dragRef.current = {
        key,
        owner: entry.spec.owner,
        id: entry.spec.id,
        kind: entry.spec.kind,
        original: entry.spec.price,
        price: entry.spec.price,
        pointerId: e.pointerId,
      };
    };

    const onPointerMove = (e: PointerEvent) => {
      const drag = dragRef.current;
      const series = seriesRef.current;
      if (!drag || e.pointerId !== drag.pointerId || !series) {
        return;
      }

      const wanted = series.coordinateToPrice(yOf(e));
      if (wanted === null) {
        return;
      }

      // The engine refuses stops on the wrong side of the price a position closes at or an order opens at, and an
      // order price on the wrong side of the market for its type.
      const { digits: current, symbol: currentSymbol } = latest.current;
      const quote = currentSymbol ? useTradingStore.getState().prices[currentSymbol] : undefined;
      if (drag.owner === "position") {
        const position = latest.current.positions.find((p) => p.positionId === drag.id);
        if (!position || drag.kind === "entry") {
          return;
        }

        const closePrice = quote ? (position.side === "Buy" ? quote.bid : quote.ask) : position.currentPrice;
        drag.price = clampStop(drag.kind, position.side, wanted, closePrice, current);
      } else {
        const order = latest.current.orders.find((o) => o.orderId === drag.id);
        if (!order) {
          return;
        }

        drag.price =
          drag.kind === "entry"
            ? quote && order.type !== "Market"
              ? clampEntry(order.type, order.side, wanted, quote, current)
              : order.price
            : clampStop(drag.kind, order.side, wanted, order.price, current);
      }

      showDragged(linesRef.current, latest.current, drag);
    };

    const fail = (drag: Drag) => (error: Error) => {
      showDragged(linesRef.current, latest.current, { ...drag, price: drag.original });
      latest.current.onError(error instanceof CommandRejectedError ? rejectionText(error.reason) : "Could not reach the trading service.");
    };

    const finish = (commit: boolean) => {
      const drag = dragRef.current;
      if (!drag) {
        return;
      }

      dragRef.current = null;
      if (container.hasPointerCapture(drag.pointerId)) {
        container.releasePointerCapture(drag.pointerId);
      }

      const position = drag.owner === "position" ? latest.current.positions.find((p) => p.positionId === drag.id) : undefined;
      const order = drag.owner === "order" ? latest.current.orders.find((o) => o.orderId === drag.id) : undefined;
      if (!commit || (!position && !order) || drag.price === drag.original) {
        showDragged(linesRef.current, latest.current, { ...drag, price: drag.original });
        return;
      }

      // The line stays at the new level. The account update then draws it there for good.
      if (position) {
        latest.current.modify.mutate(
          {
            positionId: position.positionId,
            stopLoss: drag.kind === "stopLoss" ? drag.price : position.stopLoss,
            takeProfit: drag.kind === "takeProfit" ? drag.price : position.takeProfit,
            trailingStop: position.trailingDistance !== null,
          },
          { onError: fail(drag) },
        );
      } else if (order) {
        // The order's stops keep their distance when its price moves.
        const shift = drag.kind === "entry" ? drag.price - order.price : 0;
        const moved = (level: number | null) => (level === null ? null : Number((level + shift).toFixed(latest.current.digits)));
        latest.current.modifyOrder.mutate(
          {
            orderId: order.orderId,
            price: drag.kind === "entry" ? drag.price : order.price,
            stopLoss: drag.kind === "stopLoss" ? drag.price : moved(order.stopLoss),
            takeProfit: drag.kind === "takeProfit" ? drag.price : moved(order.takeProfit),
            trailingStop: order.trailingDistance !== null,
          },
          { onError: fail(drag) },
        );
      }
    };

    const onPointerUp = (e: PointerEvent) => {
      if (dragRef.current?.pointerId === e.pointerId) {
        finish(true);
      }
    };
    const onPointerCancel = () => finish(false);
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        finish(false);
      }
    };

    container.addEventListener("pointerdown", onPointerDown, { capture: true });
    container.addEventListener("pointermove", onPointerMove);
    container.addEventListener("pointerup", onPointerUp);
    container.addEventListener("pointercancel", onPointerCancel);
    window.addEventListener("keydown", onKeyDown);
    return () => {
      container.removeEventListener("pointerdown", onPointerDown, { capture: true });
      container.removeEventListener("pointermove", onPointerMove);
      container.removeEventListener("pointerup", onPointerUp);
      container.removeEventListener("pointercancel", onPointerCancel);
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [chartRef, containerRef, handlesRef, seriesRef]);
}

function stopSpecs(stopLoss: number | null, takeProfit: number | null): [StopKind, number][] {
  const stops: [StopKind, number][] = [];
  if (stopLoss !== null) {
    stops.push(["stopLoss", stopLoss]);
  }

  if (takeProfit !== null) {
    stops.push(["takeProfit", takeProfit]);
  }

  return stops;
}

// "Buy limit 1.00": the order's side, type and volume.
function orderTitle(order: OrderSnapshot) {
  return `${order.side} ${order.type.toLowerCase()} ${formatVolume(order.volume)}`;
}

// Moves a line without waiting for React, and shows the estimated result at its new level. An order's stops follow
// its price.
function showDragged(lines: Lines, latest: Latest, drag: Drag) {
  const entry = lines.get(drag.key);
  if (!entry) {
    return;
  }

  if (drag.owner === "position") {
    const position = latest.positions.find((p) => p.positionId === drag.id);
    if (position && drag.kind !== "entry") {
      entry.line.applyOptions({
        price: drag.price,
        title: stopTitle(drag.kind, position.side, position.volume, position.openPrice, drag.price, latest.digits, latest.pointValue, drag.kind === "stopLoss" ? position.trailingDistance : null),
      });
    }
    return;
  }

  const order = latest.orders.find((o) => o.orderId === drag.id);
  if (!order) {
    return;
  }

  if (drag.kind !== "entry") {
    entry.line.applyOptions({
      price: drag.price,
      title: stopTitle(drag.kind, order.side, order.volume, order.price, drag.price, latest.digits, latest.pointValue, drag.kind === "stopLoss" ? order.trailingDistance : null),
    });
    return;
  }

  entry.line.applyOptions({ price: drag.price });
  const shift = drag.price - order.price;
  for (const [kind, level] of stopSpecs(order.stopLoss, order.takeProfit)) {
    lines.get(orderKey(order.orderId, kind))?.line.applyOptions({ price: level + shift });
  }
}

function stopTitle(
  kind: StopKind,
  side: Side,
  volume: number,
  entry: number,
  price: number,
  digits: number,
  pointValue: PointValue | null | undefined,
  trailing: number | null,
) {
  const label = kind === "stopLoss" ? (trailing === null ? "SL" : `Trailing SL (${trailingPips(trailing, digits)} pips)`) : "TP";
  return pointValue ? `${label} ${formatSignedMoney(estimatedProfit(side, volume, entry, price, digits, pointValue.perLot))}` : label;
}
