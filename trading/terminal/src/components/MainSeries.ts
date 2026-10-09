import {
  BarSeries,
  CandlestickSeries,
  LineSeries,
  LineStyle,
  type IChartApi,
  type ISeriesApi,
  type SeriesType,
  type UTCTimestamp,
} from "lightweight-charts";

import { heikinAshi, type Bar } from "@/lib/candles";
import type { CandleColors, ChartType } from "@/lib/settings";

export interface MainSeriesColors {
  candles: CandleColors;
  last: string;
  line: string;
}

const transparent = "rgba(0, 0, 0, 0)";

/**
 * The prices on the chart in the chosen type (ADR 0058). The candles series always holds the true bars: the lines of
 * positions and orders, the trades, the drawings and the last price belong to it. As candles it shows them; as bars, a
 * line or Heikin Ashi it turns see-through and another series on top draws them, so switching type never moves a
 * line or a drawing.
 */
export class MainSeries {
  readonly candles: ISeriesApi<"Candlestick">;
  private overlay: ISeriesApi<SeriesType> | null = null;
  private type: ChartType = "candles";
  private bars: readonly Bar[] = [];

  constructor(
    private readonly chart: IChartApi,
    private colors: MainSeriesColors,
  ) {
    this.candles = chart.addSeries(CandlestickSeries, {
      ...candleColors(colors.candles),
      borderVisible: false,
      priceLineColor: colors.last,
      priceLineStyle: LineStyle.Dashed,
    });
  }

  setType(type: ChartType) {
    if (type === this.type) {
      return;
    }

    if (this.overlay) {
      this.chart.removeSeries(this.overlay);
      this.overlay = null;
    }

    this.type = type;
    this.candles.applyOptions(type === "candles" ? candleColors(this.colors.candles) : hidden);
    const shared = { priceLineVisible: false, lastValueVisible: false };
    if (type === "bars") {
      this.overlay = this.chart.addSeries(BarSeries, { ...shared, upColor: this.colors.candles.up, downColor: this.colors.candles.down, thinBars: false });
    } else if (type === "line") {
      this.overlay = this.chart.addSeries(LineSeries, { ...shared, color: this.colors.line, lineWidth: 2, crosshairMarkerVisible: true });
    } else if (type === "heikinAshi") {
      this.overlay = this.chart.addSeries(CandlestickSeries, { ...shared, ...candleColors(this.colors.candles), borderVisible: false });
    }

    this.overlay?.applyOptions({ priceFormat: this.candles.options().priceFormat });
    this.setData(this.bars);
  }

  setColors(colors: MainSeriesColors) {
    this.colors = colors;
    this.candles.applyOptions({ priceLineColor: colors.last, ...(this.type === "candles" ? candleColors(colors.candles) : hidden) });
    if (this.type === "bars") {
      this.overlay?.applyOptions({ upColor: colors.candles.up, downColor: colors.candles.down });
    } else if (this.type === "line") {
      this.overlay?.applyOptions({ color: colors.line });
    } else if (this.type === "heikinAshi") {
      this.overlay?.applyOptions(candleColors(colors.candles));
    }
  }

  setPriceFormat(digits: number) {
    const priceFormat = { type: "price" as const, precision: digits, minMove: 10 ** -digits };
    this.candles.applyOptions({ priceFormat });
    this.overlay?.applyOptions({ priceFormat });
  }

  setData(bars: readonly Bar[]) {
    this.bars = bars;
    this.candles.setData(bars.map(toCandle));
    this.overlay?.setData(this.overlayData(bars));
  }

  /** The latest bar changed or a new one began. <paramref name="bars"/> are all of them, the latest last. */
  update(bars: readonly Bar[]) {
    this.bars = bars;
    const last = bars.at(-1);
    if (!last) {
      return;
    }

    this.candles.update(toCandle(last));
    if (this.type === "heikinAshi") {
      // Each Heikin Ashi bar follows the one before, so the latest is worked out from the two before it.
      const recent = heikinAshi(bars.slice(-50)).at(-1);
      if (recent) {
        this.overlay?.update(toCandle(recent));
      }
    } else if (this.overlay) {
      this.overlay.update(this.overlayData([last])[0]);
    }
  }

  private overlayData(bars: readonly Bar[]) {
    switch (this.type) {
      case "line":
        return bars.map((b) => ({ time: b.time as UTCTimestamp, value: b.close }));
      case "heikinAshi":
        return heikinAshi(bars).map(toCandle);
      default:
        return bars.map(toCandle);
    }
  }
}

const hidden = { upColor: transparent, downColor: transparent, wickUpColor: transparent, wickDownColor: transparent };

export function toCandle(bar: Bar) {
  return { time: bar.time as UTCTimestamp, open: bar.open, high: bar.high, low: bar.low, close: bar.close };
}

function candleColors({ up, down }: CandleColors) {
  return { upColor: up, downColor: down, wickUpColor: up, wickDownColor: down };
}
