import type {
  IPrimitivePaneRenderer,
  IPrimitivePaneView,
  ISeriesPrimitive,
  ISeriesPrimitiveAxisView,
  Logical,
  PrimitiveHoveredItem,
  SeriesAttachedParameter,
  Time,
} from "lightweight-charts";

import {
  fibonacciLevels,
  fibonacciYs,
  hitDrawing,
  logicalOfTime,
  timeOfLogical,
  type ChartPoint,
  type Drawing,
  type DrawingPart,
  type DrawingPixels,
  type Pixel,
} from "@/lib/drawings";

type DrawTarget = Parameters<IPrimitivePaneRenderer["draw"]>[0];

export interface DrawingColors {
  line: string;
  fill: string;
  selected: string;
  label: string;
  labelText: string;
}

// How close to a drawing, in pixels, the pointer reaches it.
export const drawingReach = 6;

// The square at each end of a selected drawing, in pixels.
const handleSize = 7;

/**
 * Draws the trader's drawings on the candles, and the one being drawn. The selected drawing shows its ends, which can
 * be grabbed. Times are placed among the bars, so a drawing stays at its times on every timeframe. A horizontal line
 * also has its price on the price axis, and a retracement each level's share and price.
 */
export class DrawingsLayer implements ISeriesPrimitive<Time> {
  private param: SeriesAttachedParameter<Time> | null = null;
  private drawings: readonly Drawing[] = [];
  private draft: Drawing | null = null;
  private selected: string | null = null;
  private times: readonly number[] = [];
  private barSeconds = 60;
  private digits = 5;
  private drawingMode = false;
  private measurer: CanvasRenderingContext2D | null = null;
  private readonly views: readonly IPrimitivePaneView[];
  private axisViews: readonly ISeriesPrimitiveAxisView[] = [];

  constructor(
    private readonly colors: DrawingColors,
    /** The notes' font in media pixels, such as "12px Familjen Grotesk". */
    private readonly noteFont = "12px sans-serif",
  ) {
    const renderer: IPrimitivePaneRenderer = { draw: (target) => this.draw(target) };
    this.views = [{ zOrder: () => "top", renderer: () => renderer }];
  }

  attached(param: SeriesAttachedParameter<Time>) {
    this.param = param;
  }

  detached() {
    this.param = null;
  }

  paneViews() {
    return this.views;
  }

  priceAxisViews() {
    return this.axisViews;
  }

  /** The drawings, the one being drawn and the selected one's id. */
  set(drawings: readonly Drawing[], draft: Drawing | null, selected: string | null) {
    this.drawings = drawings;
    this.draft = draft;
    this.selected = selected;
    this.updateAxisViews();
    this.param?.requestUpdate();
  }

  /** While a tool is in use, the whole chart shows a crosshair cursor. */
  setDrawingMode(on: boolean) {
    this.drawingMode = on;
  }

  /** The bar times on the chart, which place the drawings, and the price decimals of their labels. */
  setBars(times: readonly number[], barSeconds: number, digits: number) {
    this.times = times;
    this.barSeconds = barSeconds;
    this.digits = digits;
    this.param?.requestUpdate();
  }

  /** The time and price at a point on the chart, or null outside the candles' area. */
  pointAt(x: number, y: number): ChartPoint | null {
    const param = this.param;
    if (!param || this.times.length === 0) {
      return null;
    }

    const timeScale = param.chart.timeScale();
    const first = timeScale.logicalToCoordinate(0 as Logical);
    const price = param.series.coordinateToPrice(y);
    if (first === null || price === null) {
      return null;
    }

    const logical = (x - first) / timeScale.options().barSpacing;
    return { time: timeOfLogical(this.times, logical, this.barSeconds), price };
  }

  /** The drawing and the part of it under the point, the selected one first. */
  hit(x: number, y: number): { id: string; part: DrawingPart } | null {
    const ordered = [...this.drawings].sort((a, b) => Number(b.id === this.selected) - Number(a.id === this.selected));
    for (const drawing of ordered) {
      const pixels = this.pixelsOf(drawing);
      const part = pixels && hitDrawing(pixels, { x, y }, drawingReach);
      if (part) {
        return { id: drawing.id, part };
      }
    }

    return null;
  }

  hitTest(x: number, y: number): PrimitiveHoveredItem | null {
    if (this.drawingMode) {
      return { cursorStyle: "crosshair", externalId: "drawing", zOrder: "top" };
    }

    const hit = this.hit(x, y);
    if (!hit) {
      return null;
    }

    const cursorStyle = hit.part === "body" ? (hit.id === this.selected ? "move" : "pointer") : "crosshair";
    return { cursorStyle, externalId: `drawing:${hit.id}`, zOrder: "top" };
  }

  /** Where a time and price are on the chart, such as a note being written. */
  pixelAt(point: ChartPoint): Pixel | null {
    return this.pixelOf(point);
  }

  private pixelOf(point: ChartPoint): Pixel | null {
    const param = this.param;
    if (!param) {
      return null;
    }

    // The chart places whole bars only, so a time between two bars is placed from the bar before it.
    const timeScale = param.chart.timeScale();
    const logical = logicalOfTime(this.times, point.time, this.barSeconds);
    const bar = Math.floor(logical);
    const barX = timeScale.logicalToCoordinate(bar as Logical);
    const y = param.series.priceToCoordinate(point.price);
    return barX === null || y === null ? null : { x: barX + (logical - bar) * timeScale.options().barSpacing, y };
  }

  private pixelsOf(drawing: Drawing): DrawingPixels | null {
    switch (drawing.kind) {
      case "horizontal": {
        const y = this.param?.series.priceToCoordinate(drawing.price);
        return y == null ? null : { kind: "horizontal", y };
      }
      case "vertical": {
        const at = this.pixelOf({ time: drawing.time, price: 0 });
        return at ? { kind: "vertical", x: at.x } : null;
      }
      case "text": {
        const at = this.pixelOf(drawing.at);
        return at ? { kind: "text", at, width: this.measure(drawing.text) } : null;
      }
      default: {
        const from = this.pixelOf(drawing.from);
        const to = this.pixelOf(drawing.to);
        return from && to ? { kind: drawing.kind, from, to } : null;
      }
    }
  }

  // A note's width on the chart, for grabbing it.
  private measure(text: string): number {
    if (typeof document === "undefined") {
      return text.length * 7;
    }

    this.measurer ??= document.createElement("canvas").getContext("2d");
    if (!this.measurer) {
      return text.length * 7;
    }

    this.measurer.font = this.noteFont;
    return this.measurer.measureText(text).width;
  }

  private updateAxisViews() {
    const all = this.draft ? [...this.drawings, this.draft] : this.drawings;
    this.axisViews = all
      .filter((d): d is Extract<Drawing, { kind: "horizontal" }> => d.kind === "horizontal")
      .map((d) => ({
        coordinate: () => this.param?.series.priceToCoordinate(d.price) ?? -100,
        text: () => d.price.toFixed(this.digits),
        textColor: () => this.colors.labelText,
        backColor: () => (d.id === this.selected ? this.colors.selected : this.colors.label),
      }));
  }

  private draw(target: DrawTarget) {
    const all = this.draft ? [...this.drawings, this.draft] : this.drawings;
    if (!this.param || all.length === 0 || this.times.length === 0) {
      return;
    }

    target.useBitmapCoordinateSpace(({ context, bitmapSize, horizontalPixelRatio: hr, verticalPixelRatio: vr }) => {
      for (const drawing of all) {
        const pixels = this.pixelsOf(drawing);
        if (!pixels) {
          continue;
        }

        const selected = drawing.id === this.selected || drawing === this.draft;
        const color = selected ? this.colors.selected : this.colors.line;
        context.save();
        context.strokeStyle = color;
        context.lineWidth = Math.max(1, Math.floor(hr)) * (selected ? 2 : 1);
        context.beginPath();
        switch (pixels.kind) {
          case "horizontal": {
            const y = Math.round(pixels.y * vr) + 0.5;
            context.moveTo(0, y);
            context.lineTo(bitmapSize.width, y);
            context.stroke();
            break;
          }
          case "vertical": {
            const x = Math.round(pixels.x * hr) + 0.5;
            context.moveTo(x, 0);
            context.lineTo(x, bitmapSize.height);
            context.stroke();
            break;
          }
          case "trend":
          case "arrow": {
            context.moveTo(pixels.from.x * hr, pixels.from.y * vr);
            context.lineTo(pixels.to.x * hr, pixels.to.y * vr);
            context.stroke();
            if (pixels.kind === "arrow") {
              this.arrowHead(context, pixels.from, pixels.to, hr, vr, color);
            }
            break;
          }
          case "rectangle": {
            const x = Math.min(pixels.from.x, pixels.to.x) * hr;
            const y = Math.min(pixels.from.y, pixels.to.y) * vr;
            const width = Math.abs(pixels.to.x - pixels.from.x) * hr;
            const height = Math.abs(pixels.to.y - pixels.from.y) * vr;
            context.fillStyle = this.colors.fill;
            context.fillRect(x, y, width, height);
            context.strokeRect(x, y, width, height);
            break;
          }
          case "fibonacci":
            this.fibonacci(context, drawing, pixels.from, pixels.to, bitmapSize.width, hr, vr, color);
            break;
          case "text":
            context.font = this.noteFont.replace(/^(\d+)px/, (_, size) => `${Number(size) * vr}px`);
            context.fillStyle = color;
            context.textBaseline = "bottom";
            context.fillText(drawing.kind === "text" ? drawing.text : "", pixels.at.x * hr, pixels.at.y * vr);
            break;
        }

        // The ends of the selected drawing, to be grabbed.
        if (selected && pixels.kind !== "horizontal" && pixels.kind !== "vertical" && pixels.kind !== "text") {
          context.fillStyle = this.colors.selected;
          for (const end of [pixels.from, pixels.to]) {
            context.fillRect((end.x - handleSize / 2) * hr, (end.y - handleSize / 2) * vr, handleSize * hr, handleSize * vr);
          }
        }
        context.restore();
      }
    });
  }

  // A filled head at the arrow's end, pointing the way it was drawn.
  private arrowHead(context: CanvasRenderingContext2D, from: Pixel, to: Pixel, hr: number, vr: number, color: string) {
    const angle = Math.atan2(to.y - from.y, to.x - from.x);
    const size = 10;
    context.fillStyle = color;
    context.beginPath();
    context.moveTo(to.x * hr, to.y * vr);
    for (const side of [-1, 1]) {
      const a = angle + Math.PI - side * 0.45;
      context.lineTo((to.x + Math.cos(a) * size) * hr, (to.y + Math.sin(a) * size) * vr);
    }
    context.closePath();
    context.fill();
  }

  // The retracement's levels from the left point to the right edge, each with its share and price, and the line
  // between the two points dotted.
  private fibonacci(context: CanvasRenderingContext2D, drawing: Drawing, from: Pixel, to: Pixel, width: number, hr: number, vr: number, color: string) {
    if (drawing.kind !== "fibonacci") {
      return;
    }

    const left = Math.min(from.x, to.x) * hr;
    context.font = this.noteFont.replace(/^(\d+)px/, (_, size) => `${Math.round(Number(size) * 0.9 * vr)}px`);
    context.textBaseline = "bottom";
    fibonacciLevels.forEach((level, index) => {
      const y = Math.round(fibonacciYs(from, to)[index] * vr) + 0.5;
      context.beginPath();
      context.moveTo(left, y);
      context.lineTo(width, y);
      context.stroke();
      const price = drawing.from.price + (drawing.to.price - drawing.from.price) * level;
      context.fillStyle = color;
      context.fillText(`${(level * 100).toFixed(1).replace(/\.0$/, "")}%  ${price.toFixed(this.digits)}`, left + 4 * hr, y - 2 * vr);
    });
    context.setLineDash([3 * hr, 3 * hr]);
    context.beginPath();
    context.moveTo(from.x * hr, from.y * vr);
    context.lineTo(to.x * hr, to.y * vr);
    context.stroke();
    context.setLineDash([]);
  }
}
