import type {
  IPrimitivePaneRenderer,
  IPrimitivePaneView,
  ISeriesPrimitive,
  SeriesAttachedParameter,
  Time,
  UTCTimestamp,
} from "lightweight-charts";

import type { Side, Timeframe } from "@/lib/api/types";
import { barStart } from "@/lib/candles";
import type { ChartTrade, TradePoint } from "@/lib/chartTrades";

type DrawTarget = Parameters<IPrimitivePaneRenderer["draw"]>[0];

export interface TradeMarkerColors {
  buy: string;
  sell: string;
  line: string;
  outline: string;
}

// An arrow pointing right with its tip at 0,0, in media pixels.
const arrow: readonly (readonly [number, number])[] = [
  [0, 0],
  [-6, -5],
  [-6, -2],
  [-12, -2],
  [-12, 2],
  [-6, 2],
  [-6, 5],
];

/**
 * Draws each trade on the candles: an arrow beside the opening candle pointing at the open price, an arrow
 * on the other side of the closing candle pointing at the close price, and a grey dashed line between them.
 * The arrows have the color of the deal, so closing a buy is a sell.
 */
export class TradeMarkers implements ISeriesPrimitive<Time> {
  private param: SeriesAttachedParameter<Time> | null = null;
  private trades: readonly ChartTrade[] = [];
  private timeframe: Timeframe = "M1";
  private readonly views: readonly IPrimitivePaneView[];

  constructor(private readonly colors: TradeMarkerColors) {
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

  setTrades(trades: readonly ChartTrade[], timeframe: Timeframe) {
    this.trades = trades;
    this.timeframe = timeframe;
    this.param?.requestUpdate();
  }

  private draw(target: DrawTarget) {
    const param = this.param;
    if (!param || this.trades.length === 0) {
      return;
    }

    const timeScale = param.chart.timeScale();
    // From the middle of the candle to the arrow tip: half a candle body and a small gap.
    const offset = Math.max(1, timeScale.options().barSpacing * 0.4) + 3;

    // The candle that holds the time. Candles outside the loaded history have no coordinate.
    const coordinateOf = (point: TradePoint | null) => {
      if (!point) {
        return null;
      }

      const x = timeScale.timeToCoordinate(barStart(Math.floor(point.time), this.timeframe) as UTCTimestamp);
      const y = param.series.priceToCoordinate(point.price);
      return x === null || y === null ? null : { x, y };
    };

    target.useBitmapCoordinateSpace(({ context, horizontalPixelRatio: hr, verticalPixelRatio: vr }) => {
      for (const trade of this.trades) {
        const open = coordinateOf(trade.open);
        const close = coordinateOf(trade.close);
        const openTip = open && { x: open.x - offset, y: open.y };
        const closeTip = close && { x: close.x + offset, y: close.y };

        if (openTip && closeTip) {
          context.save();
          context.strokeStyle = this.colors.line;
          context.lineWidth = Math.max(1, Math.floor(hr));
          context.setLineDash([4 * hr, 3 * hr]);
          context.beginPath();
          context.moveTo(openTip.x * hr, openTip.y * vr);
          context.lineTo(closeTip.x * hr, closeTip.y * vr);
          context.stroke();
          context.restore();
        }

        if (openTip) {
          this.drawArrow(context, openTip, 1, this.colorOf(trade.side), hr, vr);
        }

        if (closeTip) {
          this.drawArrow(context, closeTip, -1, this.colorOf(trade.side === "Buy" ? "Sell" : "Buy"), hr, vr);
        }

        // A part closed before the rest gets a closing arrow of its own, without a line.
        for (const part of trade.parts) {
          const at = coordinateOf(part);
          if (at) {
            this.drawArrow(context, { x: at.x + offset, y: at.y }, -1, this.colorOf(trade.side === "Buy" ? "Sell" : "Buy"), hr, vr);
          }
        }
      }
    });
  }

  private colorOf(side: Side) {
    return side === "Buy" ? this.colors.buy : this.colors.sell;
  }

  // Direction 1 points right, -1 points left.
  private drawArrow(
    context: CanvasRenderingContext2D,
    tip: { x: number; y: number },
    direction: 1 | -1,
    color: string,
    hr: number,
    vr: number,
  ) {
    context.beginPath();
    for (const [index, [dx, dy]] of arrow.entries()) {
      const x = (tip.x + direction * dx) * hr;
      const y = (tip.y + dy) * vr;
      if (index === 0) {
        context.moveTo(x, y);
      } else {
        context.lineTo(x, y);
      }
    }
    context.closePath();
    // The outline first, so the fill covers its inner half and only a thin edge shows against the candles.
    context.strokeStyle = this.colors.outline;
    context.lineWidth = 2 * Math.max(1, Math.floor(hr));
    context.stroke();
    context.fillStyle = color;
    context.fill();
  }
}
