const width = 48;
const height = 18;

/** A small line of the last 24 hours, green when the price rose and red when it fell. */
export function Sparkline({ points }: { points: readonly number[] }) {
  if (points.length < 2) {
    return <svg width={width} height={height} aria-hidden="true" />;
  }

  const min = Math.min(...points);
  const range = Math.max(...points) - min || 1;
  const coordinates = points
    .map((p, i) => `${((i / (points.length - 1)) * width).toFixed(1)},${(height - 1 - ((p - min) / range) * (height - 2)).toFixed(1)}`)
    .join(" ");
  const rising = points[points.length - 1] >= points[0];

  return (
    <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} aria-hidden="true" className={rising ? "text-profit" : "text-loss"}>
      <polyline points={coordinates} fill="none" stroke="currentColor" strokeWidth={1.4} strokeLinejoin="round" strokeLinecap="round" />
    </svg>
  );
}
