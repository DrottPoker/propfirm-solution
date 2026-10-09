import type {
  AutoscaleInfo,
  IPrimitivePaneRenderer,
  IPrimitivePaneView,
  ISeriesPrimitive,
  PrimitiveHoveredItem,
  SeriesAttachedParameter,
  Time,
} from "lightweight-charts";

import { edgeLineLimit, offscreenLines, priceRangeOf, type ChartLine } from "@/lib/chartLines";

type DrawTarget = Parameters<IPrimitivePaneRenderer["draw"]>[0];

export interface ChartLineColors {
  /** The tags' background, the panel's raised color. */
  tag: string;
  /** The price axis' background, which covers its figures under a label. */
  axis: string;
  text: string;
}

/** What a press on an edge tag is, in the chart's hover and click events. */
export const offscreenTagId = "offscreen-lines";

// The tags at an edge, in media pixels.
const tagHeight = 20;
const tagGap = 4;
const edgeMargin = 8;

// The label of a price line on the axis is the font's height and its padding; the cover is a little taller.
const axisLabelHeight = 22;

interface Tag {
  x: number;
  y: number;
  width: number;
  text: string;
  color: string;
}

/**
 * The lines the trader keeps an eye on, grouped by what draws them (ADR 0058). The price scale follows the candles, so a
 * stop far away never squeezes them flat. A line beyond the view is told by a tag at the top or bottom edge, such as
 * "TP +100.00 ↑", and a press on a tag lets the scale take in every line until the chart is reset. On the price axis
 * the figures under the lines' labels and the last price are covered, so a label never sits on top of another number.
 */
export class ChartLines implements ISeriesPrimitive<Time> {
  private param: SeriesAttachedParameter<Time> | null = null;
  private readonly groups = new Map<string, readonly ChartLine[]>();
  private lastPrice: number | null = null;
  private includeAll = false;
  private tags: Tag[] = [];
  private readonly views: readonly IPrimitivePaneView[];
  private readonly axisViews: readonly IPrimitivePaneView[];

  constructor(
    private readonly colors: ChartLineColors,
    private readonly fontFamily: string,
  ) {
    const renderer: IPrimitivePaneRenderer = { draw: (target) => this.drawTags(target) };
    this.views = [{ zOrder: () => "top", renderer: () => renderer }];
    const axisRenderer: IPrimitivePaneRenderer = { draw: (target) => this.drawAxisCover(target) };
    // Drawn after the axis' figures and before the labels, so the labels stand on a clean background.
    this.axisViews = [{ zOrder: () => "normal", renderer: () => axisRenderer }];
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

  priceAxisPaneViews() {
    return this.axisViews;
  }

  set(group: string, lines: readonly ChartLine[]) {
    this.groups.set(group, lines);
    this.param?.requestUpdate();
  }

  /** The last price, whose label on the axis is kept clear too. */
  setLastPrice(price: number | null) {
    this.lastPrice = price;
  }

  /** Lets the scale take in every line, as after a press on a tag. */
  showAll() {
    this.includeAll = true;
    this.param?.requestUpdate();
  }

  /** Back to a scale that follows the candles, as Reset chart does. */
  reset() {
    this.includeAll = false;
    this.param?.requestUpdate();
  }

  autoscaleInfo(): AutoscaleInfo | null {
    if (!this.includeAll) {
      return null;
    }

    const range = priceRangeOf(this.lines());
    return range ? { priceRange: range } : null;
  }

  hitTest(x: number, y: number): PrimitiveHoveredItem | null {
    const hit = this.tags.some((t) => x >= t.x && x <= t.x + t.width && y >= t.y && y <= t.y + tagHeight);
    return hit ? { cursorStyle: "pointer", externalId: offscreenTagId, zOrder: "top" } : null;
  }

  private lines(): ChartLine[] {
    return [...this.groups.values()].flat();
  }

  private drawTags(target: DrawTarget) {
    const series = this.param?.series;
    this.tags = [];
    if (!series) {
      return;
    }

    target.useMediaCoordinateSpace(({ context, mediaSize }) => {
      const top = series.coordinateToPrice(0);
      const bottom = series.coordinateToPrice(mediaSize.height);
      if (top === null || bottom === null) {
        return;
      }

      const { above, below } = offscreenLines(this.lines(), top, bottom);
      context.font = `11px ${this.fontFamily}`;
      context.textBaseline = "middle";
      const place = (lines: ChartLine[], fromTop: boolean) => {
        const shown = lines.slice(0, edgeLineLimit);
        const texts = shown.map((l) => ({ text: `${l.label} ${fromTop ? "↑" : "↓"}`, color: l.color }));
        if (lines.length > shown.length) {
          texts.push({ text: `${lines.length - shown.length} more ${fromTop ? "↑" : "↓"}`, color: this.colors.text });
        }

        texts.forEach(({ text, color }, index) => {
          const width = Math.ceil(context.measureText(text).width) + 16;
          const x = mediaSize.width - width - edgeMargin;
          const y = fromTop ? edgeMargin + index * (tagHeight + tagGap) : mediaSize.height - edgeMargin - tagHeight - index * (tagHeight + tagGap);
          context.fillStyle = this.colors.tag;
          context.strokeStyle = color;
          context.lineWidth = 1;
          context.beginPath();
          context.roundRect(x + 0.5, y + 0.5, width - 1, tagHeight - 1, 4);
          context.fill();
          context.stroke();
          context.fillStyle = color;
          context.fillText(text, x + 8, y + tagHeight / 2 + 0.5);
          this.tags.push({ x, y, width, text, color });
        });
      };
      place(above, true);
      place(below, false);
    });
  }

  private drawAxisCover(target: DrawTarget) {
    const series = this.param?.series;
    if (!series) {
      return;
    }

    const prices = [...this.lines().map((l) => l.price), ...(this.lastPrice === null ? [] : [this.lastPrice])];
    target.useMediaCoordinateSpace(({ context, mediaSize }) => {
      context.fillStyle = this.colors.axis;
      for (const price of prices) {
        const y = series.priceToCoordinate(price);
        if (y !== null && y > -axisLabelHeight && y < mediaSize.height + axisLabelHeight) {
          context.fillRect(0, y - axisLabelHeight / 2, mediaSize.width, axisLabelHeight);
        }
      }
    });
  }
}
