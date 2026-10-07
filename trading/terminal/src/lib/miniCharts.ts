import type { EquityPoint, PriceGap } from "./api/types";

// The breach report's chart drawn as SVG (ADR 0053): coordinates in a box of the given size,
// time from left to right and value from bottom to top.

export interface Box {
  width: number;
  height: number;
  /** Room kept free above and below the values. */
  padding: number;
}

/** Maps times and values onto the box. Equal values get the middle. */
function scale(times: readonly number[], values: readonly number[], box: Box) {
  const [t0, t1] = [Math.min(...times), Math.max(...times)];
  const [v0, v1] = [Math.min(...values), Math.max(...values)];
  return {
    x: (t: number) => (t1 === t0 ? box.width / 2 : ((t - t0) / (t1 - t0)) * box.width),
    y: (v: number) => (v1 === v0 ? box.height / 2 : box.padding + (1 - (v - v0) / (v1 - v0)) * (box.height - 2 * box.padding)),
  };
}

const point = (x: number, y: number) => `${x.toFixed(1)},${y.toFixed(1)}`;

/** Equity over time, the loss limit as a line, the periods without prices and the breach at the end. */
export function equityChart(points: readonly EquityPoint[], level: number, gaps: readonly PriceGap[], box: Box) {
  if (points.length === 0) {
    return { line: "", levelY: box.height / 2, gaps: [], breach: null };
  }

  const times = points.map((p) => Date.parse(p.time));
  const { x, y } = scale(times, [...points.map((p) => p.equity), level], box);
  const last = points[points.length - 1];
  return {
    line: points.map((p) => point(x(Date.parse(p.time)), y(p.equity))).join(" "),
    levelY: y(level),
    gaps: gaps.map((g) => {
      const from = x(Math.max(Date.parse(g.from), times[0]));
      return { x: from, width: Math.max(1, x(Date.parse(g.to)) - from) };
    }),
    breach: { x: x(Date.parse(last.time)), y: y(last.equity) },
  };
}
