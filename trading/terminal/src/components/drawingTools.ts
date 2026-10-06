import type { IChartApi } from "lightweight-charts";
import { useCallback, useEffect, useRef, useState } from "react";

import { loadDrawings, moveDrawing, saveDrawings, type ChartPoint, type Drawing, type DrawingPart, type DrawingTool } from "@/lib/drawings";

import type { DrawingsLayer } from "./DrawingsLayer";

interface Drag {
  id: string;
  part: DrawingPart;
  start: ChartPoint;
  original: Drawing;
  pointerId: number;
}

/**
 * The trader's drawings on the symbol's chart and the tool being used. With a tool, a click draws a horizontal line,
 * and two clicks a trend line or a rectangle, after which the tool is put down. Without one, a click selects a
 * drawing, which can then be dragged by an end or as a whole, and Delete removes it. Esc lets go of the tool or the
 * selection. Lines of positions and orders are grabbed before drawings, since their handlers come first.
 */
export function useDrawingTools({
  containerRef,
  chartRef,
  layerRef,
  symbol,
  digits,
}: {
  containerRef: React.RefObject<HTMLDivElement | null>;
  chartRef: React.RefObject<IChartApi | null>;
  layerRef: React.RefObject<DrawingsLayer | null>;
  symbol: string | null;
  digits: number;
}) {
  const [tool, setTool] = useState<DrawingTool | null>(null);
  const [drawings, setDrawings] = useState<Drawing[]>(() => (symbol ? loadDrawings(symbol) : []));
  const [selected, setSelected] = useState<string | null>(null);
  const [loadedFor, setLoadedFor] = useState(symbol);

  // Each symbol has its own drawings.
  if (loadedFor !== symbol) {
    setLoadedFor(symbol);
    setDrawings(symbol ? loadDrawings(symbol) : []);
    setSelected(null);
    setTool(null);
  }

  const commit = useCallback(
    (next: Drawing[]) => {
      setDrawings(next);
      if (symbol) {
        saveDrawings(symbol, next);
      }
    },
    [symbol],
  );

  const latest = useRef({ tool, drawings, selected, digits, commit });
  useEffect(() => {
    latest.current = { tool, drawings, selected, digits, commit };
  });

  const draftRef = useRef<ChartPoint | null>(null);
  const dragRef = useRef<Drag | null>(null);

  useEffect(() => {
    draftRef.current = null;
    layerRef.current?.set(drawings, null, selected);
    layerRef.current?.setDrawingMode(tool !== null);
  }, [layerRef, drawings, selected, tool]);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) {
      return;
    }

    const at = (e: PointerEvent) => {
      const rect = container.getBoundingClientRect();
      return { x: e.clientX - rect.left, y: e.clientY - rect.top };
    };

    // Prices on the instrument's grid, and whole seconds.
    const rounded = (point: ChartPoint): ChartPoint => ({ time: Math.round(point.time), price: Number(point.price.toFixed(latest.current.digits)) });

    const draftOf = (tool: DrawingTool, from: ChartPoint, to: ChartPoint): Drawing =>
      tool === "horizontal" ? { id: "draft", kind: "horizontal", price: to.price } : { id: "draft", kind: tool, from, to };

    const onPointerDown = (e: PointerEvent) => {
      const chart = chartRef.current;
      const layer = layerRef.current;
      // A line of a position or an order was grabbed first.
      if (e.defaultPrevented || e.button !== 0 || !chart || !layer) {
        return;
      }

      const { x, y } = at(e);
      if (x > chart.timeScale().width()) {
        return;
      }

      const point = layer.pointAt(x, y);
      const { tool, drawings, selected, commit } = latest.current;
      if (!point) {
        return;
      }

      if (tool) {
        e.preventDefault();
        e.stopPropagation();
        const start = draftRef.current;
        if (tool !== "horizontal" && start === null) {
          draftRef.current = rounded(point);
          layer.set(drawings, draftOf(tool, rounded(point), rounded(point)), selected);
          return;
        }

        const id = crypto.randomUUID();
        const end = rounded(point);
        const drawing: Drawing = tool === "horizontal" ? { id, kind: "horizontal", price: end.price } : { id, kind: tool, from: start ?? end, to: end };
        draftRef.current = null;
        commit([...drawings, drawing]);
        setSelected(id);
        setTool(null);
        return;
      }

      const hit = layer.hit(x, y);
      if (!hit) {
        // A click elsewhere lets go of the selection, and the chart pans as usual.
        if (selected) {
          setSelected(null);
        }
        return;
      }

      const original = drawings.find((d) => d.id === hit.id);
      if (!original) {
        return;
      }

      e.preventDefault();
      e.stopPropagation();
      container.setPointerCapture(e.pointerId);
      setSelected(hit.id);
      dragRef.current = { id: hit.id, part: hit.part, start: point, original, pointerId: e.pointerId };
    };

    // The drawing being moved or drawn follows the pointer without waiting for React.
    let moved: Drawing | null = null;
    const onPointerMove = (e: PointerEvent) => {
      const layer = layerRef.current;
      if (!layer) {
        return;
      }

      const { x, y } = at(e);
      const point = layer.pointAt(x, y);
      const { tool, drawings, selected } = latest.current;
      if (!point) {
        return;
      }

      const start = draftRef.current;
      if (tool && start) {
        layer.set(drawings, draftOf(tool, start, rounded(point)), selected);
        return;
      }

      const drag = dragRef.current;
      if (drag && e.pointerId === drag.pointerId) {
        moved = moveDrawing(drag.original, drag.part, drag.start, point);
        layer.set(
          drawings.map((d) => (d.id === drag.id ? moved! : d)),
          null,
          drag.id,
        );
      }
    };

    const onPointerUp = (e: PointerEvent) => {
      const drag = dragRef.current;
      if (!drag || e.pointerId !== drag.pointerId) {
        return;
      }

      dragRef.current = null;
      if (container.hasPointerCapture(drag.pointerId)) {
        container.releasePointerCapture(drag.pointerId);
      }

      if (moved) {
        const done = roundDrawing(moved, latest.current.digits);
        latest.current.commit(latest.current.drawings.map((d) => (d.id === drag.id ? done : d)));
        moved = null;
      }
    };

    const onKeyDown = (e: KeyboardEvent) => {
      // Typing in a field, such as a price, never removes a drawing.
      if (e.target instanceof HTMLElement && (e.target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(e.target.tagName))) {
        return;
      }

      const { tool, drawings, selected, commit } = latest.current;
      if (e.key === "Escape") {
        if (tool || draftRef.current) {
          draftRef.current = null;
          setTool(null);
          layerRef.current?.set(drawings, null, selected);
        } else if (selected) {
          setSelected(null);
        }
      } else if ((e.key === "Delete" || e.key === "Backspace") && selected) {
        e.preventDefault();
        commit(drawings.filter((d) => d.id !== selected));
        setSelected(null);
      }
    };

    container.addEventListener("pointerdown", onPointerDown, { capture: true });
    container.addEventListener("pointermove", onPointerMove);
    container.addEventListener("pointerup", onPointerUp);
    window.addEventListener("keydown", onKeyDown);
    return () => {
      container.removeEventListener("pointerdown", onPointerDown, { capture: true });
      container.removeEventListener("pointermove", onPointerMove);
      container.removeEventListener("pointerup", onPointerUp);
      window.removeEventListener("keydown", onKeyDown);
    };
  }, [containerRef, chartRef, layerRef]);

  /** Picks up a tool, or puts it down when it is the one in use. */
  const chooseTool = (next: DrawingTool | null) => {
    draftRef.current = null;
    setTool((current) => (current === next ? null : next));
    setSelected(null);
  };

  /** Removes the selected drawing, or every drawing on the symbol when none is selected. */
  const remove = () => {
    commit(selected ? drawings.filter((d) => d.id !== selected) : []);
    setSelected(null);
  };

  return { tool, chooseTool, drawings, selected, remove };
}

function roundDrawing(drawing: Drawing, digits: number): Drawing {
  const point = (p: ChartPoint): ChartPoint => ({ time: Math.round(p.time), price: Number(p.price.toFixed(digits)) });
  return drawing.kind === "horizontal" ? { ...drawing, price: Number(drawing.price.toFixed(digits)) } : { ...drawing, from: point(drawing.from), to: point(drawing.to) };
}
