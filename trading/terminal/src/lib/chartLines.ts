// The lines the trader keeps an eye on in the chart: positions' and orders' prices and stops, the order being filled in
// and price alerts. The price scale follows the candles, so a line far away never squeezes them flat (ADR 0058). A line
// outside the view is told at the edge it is beyond, with what it is.

/** A line to keep in sight: its price, what it is, for example "TP +100.00", and its color. */
export interface ChartLine {
  price: number;
  label: string;
  color: string;
}

/** The lines beyond the top and the bottom of the view, the nearest first. */
export interface Offscreen {
  above: ChartLine[];
  below: ChartLine[];
}

/** Which lines lie outside the prices in view, from <paramref name="top"/> down to <paramref name="bottom"/>. */
export function offscreenLines(lines: readonly ChartLine[], top: number, bottom: number): Offscreen {
  return {
    above: lines.filter((l) => l.price > top).sort((a, b) => a.price - b.price),
    below: lines.filter((l) => l.price < bottom).sort((a, b) => b.price - a.price),
  };
}

/** The prices from the lowest to the highest line, or null without lines. */
export function priceRangeOf(lines: readonly ChartLine[]): { minValue: number; maxValue: number } | null {
  if (lines.length === 0) {
    return null;
  }

  const prices = lines.map((l) => l.price);
  return { minValue: Math.min(...prices), maxValue: Math.max(...prices) };
}

/** At most this many lines are told at an edge. More say how many more there are. */
export const edgeLineLimit = 3;
