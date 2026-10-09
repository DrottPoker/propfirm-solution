import type { IPrimitivePaneRenderer, IPrimitivePaneView, ISeriesPrimitive, SeriesAttachedParameter, Time, UTCTimestamp } from "lightweight-charts";

type DrawTarget = Parameters<IPrimitivePaneRenderer["draw"]>[0];

/**
 * Covers a time on the time axis that does not fit at its left or right edge, so the axis never shows half a time such
 * as "6:45" for 06:45 (ADR 0058). The chart cuts such labels where the axis ends; this paints the axis' background over
 * what is left of them. The labels are the ones the chart formatted for the latest view, which it formats again
 * whenever the view moves.
 */
export class TimeAxisEdges implements ISeriesPrimitive<Time> {
  private param: SeriesAttachedParameter<Time> | null = null;
  private readonly labels = new Map<number, string>();
  private readonly views: readonly IPrimitivePaneView[];

  constructor(
    private readonly background: string,
    private readonly font: string,
  ) {
    const renderer: IPrimitivePaneRenderer = { draw: (target) => this.draw(target) };
    // After the axis' times, which it covers where they are cut.
    this.views = [{ zOrder: () => "normal", renderer: () => renderer }];
  }

  attached(param: SeriesAttachedParameter<Time>) {
    this.param = param;
  }

  detached() {
    this.param = null;
  }

  timeAxisPaneViews() {
    return this.views;
  }

  /** A label the chart formatted for the time, from the time axis' formatter. */
  record(time: number, label: string) {
    this.labels.set(time, label);
  }

  /** The view moved, so the chart formats the labels in view again. */
  forget() {
    this.labels.clear();
  }

  private draw(target: DrawTarget) {
    const timeScale = this.param?.chart.timeScale();
    if (!timeScale || this.labels.size === 0) {
      return;
    }

    target.useMediaCoordinateSpace(({ context, mediaSize }) => {
      context.font = this.font;
      context.fillStyle = this.background;
      // Below the axis' top border.
      const top = 1;
      for (const [time, label] of this.labels) {
        const x = timeScale.timeToCoordinate(time as UTCTimestamp);
        if (x === null) {
          continue;
        }

        const half = context.measureText(label).width / 2;
        if (x - half < 0) {
          context.fillRect(0, top, x + half + 2, mediaSize.height - top);
        } else if (x + half > mediaSize.width) {
          context.fillRect(x - half - 2, top, mediaSize.width - (x - half - 2), mediaSize.height - top);
        }
      }
    });
  }
}
