import { HistogramSeries, LineSeries, LineStyle, type IChartApi, type ISeriesApi, type UTCTimestamp } from "lightweight-charts";

import type { Bar } from "@/lib/candles";
import { bollinger, ema, macd, rsi, sma, type Series } from "@/lib/indicators";
import { bollingerWidth, indicatorKinds, macdPeriods, type Indicator } from "@/lib/chartStudies";

export interface StudyColors {
  /** The lines of the indicators, in turn. */
  lines: readonly string[];
  up: string;
  down: string;
  /** RSI's lines at 30 and 70. */
  guide: string;
}

type StudySeries = ISeriesApi<"Line"> | ISeriesApi<"Histogram">;

interface Drawn {
  indicator: Indicator;
  series: StudySeries[];
}

// An indicator's pane is a quarter of the main pane's height.
const paneStretch = 0.25;

/**
 * The indicators on the chart. Moving averages and Bollinger Bands are drawn over the candles, and RSI and MACD each
 * in a pane of their own under them. The values are worked out from all candles on the chart whenever they change:
 * all of them when the history changes, and only the latest point on each live price.
 */
export class IndicatorSeries {
  private drawn: Drawn[] = [];
  private bars: readonly Bar[] = [];
  private digits = 5;

  constructor(
    private readonly chart: IChartApi,
    private readonly colors: StudyColors,
  ) {}

  /** Draws these indicators instead of the ones before. */
  setIndicators(indicators: readonly Indicator[], digits: number) {
    this.digits = digits;
    for (const { series } of this.drawn) {
      for (const s of series) {
        this.chart.removeSeries(s);
      }
    }
    this.drawn = [];

    // Panes left empty by the removed series go, the candles' own pane stays.
    for (let index = this.chart.panes().length - 1; index > 0; index--) {
      if (this.chart.panes()[index].getSeries().length === 0) {
        this.chart.removePane(index);
      }
    }

    let pane = 1;
    indicators.forEach((indicator, index) => {
      const color = this.colors.lines[index % this.colors.lines.length];
      const own = indicatorKinds[indicator.kind].ownPane;
      this.drawn.push({ indicator, series: this.create(indicator, own ? pane : 0, color) });
      if (own) {
        this.chart.panes()[pane]?.setStretchFactor(paneStretch);
        pane++;
      }
    });

    this.render(true);
  }

  /** The candles changed as a whole, for example a new symbol, timeframe or older history. */
  setBars(bars: readonly Bar[]) {
    this.bars = bars;
    this.render(true);
  }

  /** The latest candle moved or a new one started. */
  updateLast(bars: readonly Bar[]) {
    this.bars = bars;
    this.render(false);
  }

  private create(indicator: Indicator, pane: number, color: string): StudySeries[] {
    const line = (options: { color: string; style?: LineStyle; precision?: number }) =>
      this.chart.addSeries(
        LineSeries,
        {
          color: options.color,
          lineWidth: 1,
          lineStyle: options.style ?? LineStyle.Solid,
          priceLineVisible: false,
          lastValueVisible: false,
          crosshairMarkerVisible: false,
          priceFormat: { type: "price", precision: options.precision ?? this.digits, minMove: 10 ** -(options.precision ?? this.digits) },
        },
        pane,
      );

    switch (indicator.kind) {
      case "SMA":
      case "EMA":
        return [line({ color })];
      case "Bollinger":
        return [line({ color }), line({ color, style: LineStyle.Dashed }), line({ color, style: LineStyle.Dashed })];
      case "RSI": {
        const series = line({ color, precision: 1 });
        for (const level of [30, 70]) {
          series.createPriceLine({ price: level, color: this.colors.guide, lineWidth: 1, lineStyle: LineStyle.Dotted, axisLabelVisible: false, title: "" });
        }
        return [series];
      }
      case "MACD": {
        const histogram = this.chart.addSeries(
          HistogramSeries,
          { priceLineVisible: false, lastValueVisible: false, priceFormat: { type: "price", precision: this.digits + 1, minMove: 10 ** -(this.digits + 1) } },
          pane,
        );
        return [line({ color, precision: this.digits + 1 }), line({ color: this.colors.guide, precision: this.digits + 1 }), histogram];
      }
    }
  }

  // The values of each series of the indicator, one per candle.
  private values(indicator: Indicator, closes: readonly number[]): Series[] {
    switch (indicator.kind) {
      case "SMA":
        return [sma(closes, indicator.period)];
      case "EMA":
        return [ema(closes, indicator.period)];
      case "Bollinger": {
        const bands = bollinger(closes, indicator.period, bollingerWidth);
        return [bands.middle, bands.upper, bands.lower];
      }
      case "RSI":
        return [rsi(closes, indicator.period)];
      case "MACD": {
        const result = macd(closes, macdPeriods.fast, macdPeriods.slow, macdPeriods.signal);
        return [result.macd, result.signal, result.histogram];
      }
    }
  }

  private render(all: boolean) {
    if (this.drawn.length === 0) {
      return;
    }

    const closes = this.bars.map((b) => b.close);
    const last = this.bars.length - 1;
    for (const { indicator, series } of this.drawn) {
      const values = this.values(indicator, closes);
      series.forEach((s, index) => {
        const histogram = indicator.kind === "MACD" && index === 2;
        const point = (i: number) => {
          const time = this.bars[i].time as UTCTimestamp;
          const value = values[index][i];
          if (value === null) {
            return { time };
          }

          return histogram ? { time, value, color: value >= 0 ? this.colors.up : this.colors.down } : { time, value };
        };

        if (all) {
          s.setData(this.bars.map((_, i) => point(i)));
        } else if (last >= 0) {
          s.update(point(last));
        }
      });
    }
  }
}
