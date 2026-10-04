import { shortAmount, weekBars } from "@/lib/admin";
import type { Week } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";

const width = 740;
const height = 224;
const plot = { left: 0, right: width - 40, top: 16, bottom: 196 };

/** A week's start as a short date, for example "13 Jul". */
function weekLabel(isoDate: string): string {
  return new Date(`${isoDate}T00:00:00Z`).toLocaleDateString("en-GB", { day: "numeric", month: "short", timeZone: "UTC" });
}

/**
 * Sales and payouts side by side for each week, the current week last, in the firm's currency. A table with the same
 * figures is there for screen readers.
 */
export function WeeklyChart({ weeks, currency }: { weeks: Week[]; currency: string }) {
  const { bars, ticks, otherCurrencies } = weekBars(weeks, currency);
  const top = ticks[ticks.length - 1];
  const y = (value: number) => plot.bottom - (value / top) * (plot.bottom - plot.top);
  const group = (plot.right - plot.left) / Math.max(bars.length, 1);
  const bar = Math.min(16, group / 3);
  const labelled = (index: number) => index === bars.length - 1 || (index % 3 === 0 && index < bars.length - 2);

  return (
    <figure className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-4 text-xs text-muted" aria-hidden="true">
        <span className="flex items-center gap-1.5">
          <span className="size-2.5 rounded-sm bg-accent" />
          Sales
        </span>
        <span className="flex items-center gap-1.5">
          <span className="size-2.5 rounded-sm bg-foreground/70" />
          Payouts
        </span>
      </div>
      {/* On a phone the chart keeps a width its labels can be read at, and scrolls sideways. The reversed row makes it
          start at the end, with this week and the amounts in view. */}
      <div className="flex flex-row-reverse items-start overflow-x-auto">
        <svg viewBox={`0 0 ${width} ${height}`} className="w-full min-w-[34rem] shrink-0" role="img" aria-label={`Sales and payouts in ${currency} for each of the last ${bars.length} weeks. The table below has the figures.`}>
          {ticks.map((tick) => (
            <g key={tick}>
              <line x1={plot.left} x2={plot.right} y1={y(tick)} y2={y(tick)} className="stroke-border" strokeWidth={1} />
              <text x={plot.right + 8} y={y(tick) + 4} className="fill-muted font-mono text-[11px]">
                {shortAmount(tick)}
              </text>
            </g>
          ))}
          {bars.map((week, index) => {
            const x = plot.left + index * group + group / 2;
            return (
              <g key={week.start}>
                <title>{`Week of ${weekLabel(week.start)}: sales ${formatMoney(week.sales)} ${currency}, payouts ${formatMoney(week.payouts)} ${currency}`}</title>
                <rect x={plot.left + index * group} y={plot.top} width={group} height={plot.bottom - plot.top} className="fill-transparent" />
                {week.sales > 0 && <rect x={x - bar - 2} y={y(week.sales)} width={bar} height={plot.bottom - y(week.sales)} rx={2} className="fill-accent" />}
                {week.payouts > 0 && <rect x={x + 2} y={y(week.payouts)} width={bar} height={plot.bottom - y(week.payouts)} rx={2} className="fill-foreground/70" />}
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
        Sales are challenges bought in your portal, without refunds. Payouts are those you marked as paid.
        {otherCurrencies && ` Only amounts in ${currency} are drawn.`}
      </figcaption>
      <table className="sr-only">
        <caption>Sales and payouts per week, in {currency}</caption>
        <thead>
          <tr>
            <th scope="col">Week of</th>
            <th scope="col">Sales</th>
            <th scope="col">Payouts</th>
          </tr>
        </thead>
        <tbody>
          {bars.map((week) => (
            <tr key={week.start}>
              <th scope="row">{weekLabel(week.start)}</th>
              <td>{formatMoney(week.sales)}</td>
              <td>{formatMoney(week.payouts)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </figure>
  );
}
