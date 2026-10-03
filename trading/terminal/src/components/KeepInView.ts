import type { AutoscaleInfo, ISeriesPrimitive, SeriesAttachedParameter, Time } from "lightweight-charts";

/**
 * Makes the price scale include the given prices, so lines drawn there stay in view above or below the candles.
 * Each kind of line sets its own group, for example the positions' stops and the order being filled in.
 */
export class KeepInView implements ISeriesPrimitive<Time> {
  private param: SeriesAttachedParameter<Time> | null = null;
  private readonly groups = new Map<string, readonly number[]>();

  attached(param: SeriesAttachedParameter<Time>) {
    this.param = param;
  }

  detached() {
    this.param = null;
  }

  set(group: string, prices: readonly number[]) {
    this.groups.set(group, prices);
    this.param?.requestUpdate();
  }

  autoscaleInfo(): AutoscaleInfo | null {
    const prices = [...this.groups.values()].flat();
    return prices.length > 0 ? { priceRange: { minValue: Math.min(...prices), maxValue: Math.max(...prices) } } : null;
  }
}
