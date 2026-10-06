import { useId } from "react";

import { shortAmount, weekBars, type WeekAmounts } from "@/lib/admin";
import { formatMoney } from "@/lib/format";

const width = 740;
const height = 224;
const plot = { left: 0, right: width - 40, top: 16, bottom: 196 };

/** A week's start as a short date, for example "13 Jul". */
function weekLabel(isoDate: string): string {
  return new Date(`${isoDate}T00:00:00Z`).toLocaleDateString("en-GB", { day: "numeric", month: "short", timeZone: "UTC" });
}

/**
 * Two amounts side by side for each week, the current week last, in one currency: for example a firm's sales and
 * payouts. <code>series</code> names the two, the first drawn in the brand color and the second striped in it, so the
 * two differ in any firm's colors without the second looking switched off. A table with the same figures is there for
 * screen readers.
 */
export function WeeklyChart({ weeks, currency, series, caption }: { weeks: WeekAmounts[]; currency: string; series: [string, string]; caption: string }) {
  const { bars, ticks, otherCurrencies } = weekBars(weeks, currency);
  const top = ticks[ticks.length - 1];
  const y = (value: number) => plot.bottom - (value / top) * (plot.bottom - plot.top);
  const group = (plot.right - plot.left) / Math.max(bars.length, 1);
  const bar = Math.min(16, group / 3);
  const labelled = (index: number) => index === bars.length - 1 || (index % 3 === 0 && index < bars.length - 2);
  const [first, second] = series;
  // An id of letters only, since it is used in url(#...).
  const stripes = `stripes-${useId().replace(/[^a-zA-Z0-9]/g, "")}`;

  return (
    <figure className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-4 text-xs text-muted" aria-hidden="true">
        <span className="flex items-center gap-1.5">
          <span className="size-2.5 rounded-sm bg-accent" />
          {first}
        </span>
        <span className="flex items-center gap-1.5">
          <span className="size-2.5 rounded-sm border border-accent" style={{ background: "repeating-linear-gradient(135deg, var(--accent) 0 1.5px, transparent 1.5px 3.5px)" }} />
          {second}
        </span>
      </div>
      {/* On a phone the chart keeps a width its labels can be read at, and scrolls sideways. The reversed row makes it
          start at the end, with this week and the amounts in view. */}
      <div className="flex flex-row-reverse items-start overflow-x-auto">
        <svg
          viewBox={`0 0 ${width} ${height}`}
          className="w-full min-w-[34rem] shrink-0"
          role="img"
          aria-label={`${first} and ${second.toLowerCase()} in ${currency} for each of the last ${bars.length} weeks. The table below has the figures.`}
        >
          <defs>
            <pattern id={stripes} width="5" height="5" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
              <rect width="2.5" height="5" className="fill-accent" />
            </pattern>
          </defs>
          {ticks.map((tick) => (
            <g key={tick}>
              <line x1={plot.left} x2={plot.right} y1={y(tick)} y2={y(tick)} className="stroke-border" strokeWidth={1} />
              <text x={plot.right + 8} y={y(tick) + 4} className="fill-muted text-[11px]">
                {shortAmount(tick)}
              </text>
            </g>
          ))}
          {bars.map((week, index) => {
            const x = plot.left + index * group + group / 2;
            return (
              <g key={week.start}>
                <title>{`Week of ${weekLabel(week.start)}: ${first.toLowerCase()} ${formatMoney(week.first)} ${currency}, ${second.toLowerCase()} ${formatMoney(week.second)} ${currency}`}</title>
                <rect x={plot.left + index * group} y={plot.top} width={group} height={plot.bottom - plot.top} className="fill-transparent" />
                {week.first > 0 && <rect x={x - bar - 2} y={y(week.first)} width={bar} height={plot.bottom - y(week.first)} rx={2} className="fill-accent" />}
                {week.second > 0 && (
                  <rect x={x + 2.5} y={y(week.second) + 0.5} width={bar - 1} height={Math.max(0, plot.bottom - y(week.second) - 0.5)} rx={2} fill={`url(#${stripes})`} className="stroke-accent" strokeWidth={1} />
                )}
                {labelled(index) && (
                  <text x={x} y={height - 6} textAnchor="middle" className="fill-muted text-[11px]">
                    {index === bars.length - 1 ? "This week" : weekLabel(week.start)}
                  </text>
                )}
              </g>
            );
          })}
        </svg>
      </div>
      <figcaption className="text-xs text-muted">
        {caption}
        {otherCurrencies && ` Only amounts in ${currency} are drawn.`}
      </figcaption>
      {/* A table takes the width of its content whatever its own width, so the box around it is what hides it. */}
      <div className="sr-only">
        <table>
          <caption>
            {first} and {second.toLowerCase()} per week, in {currency}
          </caption>
          <thead>
            <tr>
              <th scope="col">Week of</th>
              <th scope="col">{first}</th>
              <th scope="col">{second}</th>
            </tr>
          </thead>
          <tbody>
            {bars.map((week) => (
              <tr key={week.start}>
                <th scope="row">{weekLabel(week.start)}</th>
                <td>{formatMoney(week.first)}</td>
                <td>{formatMoney(week.second)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </figure>
  );
}
