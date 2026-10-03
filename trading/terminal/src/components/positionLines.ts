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
import type { InstrumentInfo, PointValue, PositionSnapshot } from "@/lib/api/types";
import { formatSignedMoney, formatVolume } from "@/lib/format";
import { useModifyStops } from "@/lib/queries";
import { clampStop, estimatedProfit, type StopKind } from "@/lib/stops";
import { useTradingStore } from "@/lib/store";

import type { KeepInView } from "./KeepInView";

export interface PositionLineColors {
  open: string;
  stopLoss: string;
  takeProfit: string;
}

interface LineSpec {
  key: string;
  positionId: string;
  kind: "open" | StopKind;
  price: number;
  color: string;
  dashed: boolean;
  title: string;
}

interface Drag {
  key: string;
  positionId: string;
  kind: StopKind;
  original: number;
  price: number;
  pointerId: number;
}

interface Latest {
  positions: PositionSnapshot[];
  symbol: string | null;
  digits: number;
  pointValue: PointValue | null | undefined;
  modify: ReturnType<typeof useModifyStops>;
  onError: (text: string) => void;
}

type Lines = Map<string, { line: IPriceLine; spec: LineSpec }>;

// How close to a line, in pixels, a press grabs it.
export const grabDistance = 5;

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
 * Lines for the open price, stop loss and take profit of the symbol's positions. The stop lines show the estimated
 * result at their level, and can be dragged to a new level, which is sent to the engine when the mouse is released.
 * The price scale makes room for the stops, so one is never out of reach above or below the candles.
 */
export function usePositionLines({
  accountId,
  instrument,
  pointValue,
  colors,
  chartRef,
  seriesRef,
  handlesRef,
  keepInViewRef,
  containerRef,
  onError,
}: {
  accountId: string;
  instrument: InstrumentInfo | null;
  pointValue: PointValue | null | undefined;
  colors: PositionLineColors;
  chartRef: React.RefObject<IChartApi | null>;
  seriesRef: React.RefObject<ISeriesApi<"Candlestick"> | null>;
  handlesRef: React.RefObject<StopHandles | null>;
  keepInViewRef: React.RefObject<KeepInView | null>;
  containerRef: React.RefObject<HTMLDivElement | null>;
  onError: (text: string) => void;
}) {
  const symbol = instrument?.symbol ?? null;
  const digits = instrument?.digits ?? 5;
  const positions = useTradingStore((s) => s.account?.positions);
  const modify = useModifyStops(accountId);

  const linesRef = useRef<Lines>(new Map());
  const dragRef = useRef<Drag | null>(null);
  // The latest values for the pointer handlers, which are attached once.
  const latest = useRef<Latest>({ positions: [], symbol, digits, pointValue, modify, onError });

  const symbolPositions = (positions ?? []).filter((p) => p.symbol === symbol);
  const specs: LineSpec[] = symbolPositions.flatMap((p) => {
    const stop = (kind: StopKind, price: number | null): LineSpec[] =>
      price == null
        ? []
        : [
            {
              key: `${p.positionId}:${kind}`,
              positionId: p.positionId,
              kind,
              price,
              color: kind === "stopLoss" ? colors.stopLoss : colors.takeProfit,
              dashed: true,
              title: stopTitle(kind, p, price, digits, pointValue),
            },
          ];
    return [
      {
        key: `${p.positionId}:open`,
        positionId: p.positionId,
        kind: "open" as const,
        price: p.openPrice,
        color: colors.open,
        dashed: false,
        title: `${p.side} ${formatVolume(p.volume)}`,
      },
      ...stop("stopLoss", p.stopLoss),
      ...stop("takeProfit", p.takeProfit),
    ];
  });
  const specsKey = JSON.stringify(specs);

  useEffect(() => {
    latest.current = { positions: symbolPositions, symbol, digits, pointValue, modify, onError };
  });

  // Redraws the lines only when a position, stop loss, take profit or point value actually changed.
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
        lineStyle: spec.dashed ? 2 : 0,
        title: spec.title,
      });
      lines.set(spec.key, { line, spec });
    }

    // A line being dragged stays where the pointer is.
    const drag = dragRef.current;
    if (drag) {
      showDragged(lines, latest.current, drag);
    }

    const stops = parsed.filter((s) => s.kind !== "open");
    handlesRef.current?.setTargets(stops.map((s) => ({ key: s.key, price: s.price })));
    keepInViewRef.current?.set("stops", stops.map((s) => s.price));

    return () => {
      for (const { line } of lines.values()) {
        series.removePriceLine(line);
      }
      lines.clear();
    };
  }, [seriesRef, handlesRef, keepInViewRef, specsKey]);

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
        positionId: entry.spec.positionId,
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
      const position = latest.current.positions.find((p) => p.positionId === drag.positionId);
      if (wanted === null || !position) {
        return;
      }

      // The engine refuses stops on the wrong side of the price the position closes at.
      const quote = latest.current.symbol ? useTradingStore.getState().prices[latest.current.symbol] : undefined;
      const closePrice = quote ? (position.side === "Buy" ? quote.bid : quote.ask) : position.currentPrice;
      drag.price = clampStop(drag.kind, position.side, wanted, closePrice, latest.current.digits);
      showDragged(linesRef.current, latest.current, drag);
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

      const position = latest.current.positions.find((p) => p.positionId === drag.positionId);
      if (!commit || !position || drag.price === drag.original) {
        showDragged(linesRef.current, latest.current, { ...drag, price: drag.original });
        return;
      }

      // The line stays at the new level. The account update then draws it there for good.
      latest.current.modify.mutate(
        {
          positionId: position.positionId,
          stopLoss: drag.kind === "stopLoss" ? drag.price : position.stopLoss,
          takeProfit: drag.kind === "takeProfit" ? drag.price : position.takeProfit,
        },
        {
          onError: (error) => {
            showDragged(linesRef.current, latest.current, { ...drag, price: drag.original });
            latest.current.onError(error instanceof CommandRejectedError ? `Rejected: ${error.reason}` : "Could not reach the trading service.");
          },
        },
      );
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

// Moves a stop line without waiting for React, and shows the estimated result at its new level.
function showDragged(lines: Lines, latest: Latest, drag: Drag) {
  const entry = lines.get(drag.key);
  const position = latest.positions.find((p) => p.positionId === drag.positionId);
  if (!entry || !position) {
    return;
  }

  entry.line.applyOptions({
    price: drag.price,
    title: stopTitle(drag.kind, position, drag.price, latest.digits, latest.pointValue),
  });
}

function stopTitle(kind: StopKind, position: PositionSnapshot, price: number, digits: number, pointValue: PointValue | null | undefined) {
  const label = kind === "stopLoss" ? "SL" : "TP";
  return pointValue
    ? `${label} ${formatSignedMoney(estimatedProfit(position.side, position.volume, position.openPrice, price, digits, pointValue.perLot))}`
    : label;
}
