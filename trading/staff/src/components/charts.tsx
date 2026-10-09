// Small charts drawn in SVG: bars over the last hour and a bar from the middle for long against short.

/** One bar per value, such as prices per minute over the last hour, with the ones to notice in the warning color. */
export function Bars({ values, label, notice, height = 70 }: { values: number[]; label: string; notice?: (index: number) => boolean; height?: number }) {
  const max = Math.max(1, ...values);
  const width = values.length * 10;
  return (
    <svg viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" role="img" aria-label={label} className="block w-full" style={{ height }}>
      {values.map((value, index) => {
        const barHeight = Math.max(value > 0 ? 2 : 0, Math.round((value / max) * (height - 4)));
        return (
          <rect
            key={index}
            x={index * 10}
            y={height - barHeight}
            width={7}
            height={barHeight}
            rx={1.5}
            className={notice?.(index) ? "fill-warning" : "fill-accent"}
          />
        );
      })}
    </svg>
  );
}

/** A bar from the middle: red to the left for net short, green to the right for net long, as a share of the largest. */
export function LongShortBar({ value, largest }: { value: number; largest: number }) {
  const share = largest === 0 ? 0 : Math.max(value === 0 ? 0 : 0.03, Math.min(1, Math.abs(value) / largest));
  return (
    <span aria-hidden="true" className="flex h-2 w-full overflow-hidden rounded bg-raised">
      <span className="flex flex-1 justify-end">{value < 0 && <span className="h-full rounded-l bg-loss" style={{ width: `${share * 100}%` }} />}</span>
      <span className="w-px bg-muted" />
      <span className="flex flex-1">{value > 0 && <span className="h-full rounded-r bg-profit" style={{ width: `${share * 100}%` }} />}</span>
    </span>
  );
}
