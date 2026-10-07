"use client";

import { floorLabel } from "@/lib/account";
import type { BreachReport } from "@/lib/api/types";
import { describeEvent, isWarning, type DigitsOf } from "@/lib/events";
import { formatMoment, formatMoney, formatPrice, formatSignedMoney, formatTime, formatVolume, timeZoneName } from "@/lib/format";
import { equityChart } from "@/lib/miniCharts";
import { useBreachReport } from "@/lib/queries";
import { useTimeZone } from "@/lib/timeZone";

import { ProofIcon } from "./icons";
import { Sheet, SheetSection } from "./Sheet";

/**
 * Why a loss limit was broken (ADR 0053): the limit, equity at that moment and equity price by price before it, with the
 * periods without prices, the prices that broke it with the feed's prices behind them, the open positions and every
 * event up to the end of trading.
 */
export function BreachReportSheet({ accountId, digitsOf }: { accountId: string; digitsOf: DigitsOf }) {
  const report = useBreachReport(accountId);
  const timeZone = useTimeZone();
  const data = report.data;

  return (
    <Sheet
      label="Breach report"
      title={data ? `The ${floorLabel(data.floorId).toLowerCase()} was broken` : "Breach report"}
      subtitle={data ? `${formatMoment(data.at, timeZone)} ${timeZoneName(timeZone)}` : ""}
    >
      {report.isError && <p className="px-5 py-4 text-sm text-loss">Could not load the report.</p>}
      {report.isPending && <p className="px-5 py-4 text-sm text-muted">Loading...</p>}
      {data && <ReportBody report={data} digitsOf={digitsOf} timeZone={timeZone} />}
    </Sheet>
  );
}

function ReportBody({ report, digitsOf, timeZone }: { report: BreachReport; digitsOf: DigitsOf; timeZone: string }) {
  return (
    <>
      <div className="grid grid-cols-3 gap-2 border-b border-border px-5 py-4">
        <Figure label="Limit" value={formatMoney(report.level)} />
        <Figure label="Equity" value={formatMoney(report.equity)} className="text-loss" />
        <Figure label="Below by" value={formatMoney(report.level - report.equity)} />
      </div>

      <SheetSection title="Equity, price by price" aside={`from ${formatTime(report.equityCurve[0]?.time ?? report.at, timeZone)}`}>
        <EquityChart report={report} timeZone={timeZone} />
        <p className="text-xs text-muted">Counted again from every price the trading platform received, with the positions open at the time.</p>
      </SheetSection>

      <SheetSection title="The prices that broke it">
        <table className="w-full text-sm">
          <thead>
            <tr className="text-left text-[11px] text-muted">
              <th className="pb-1 font-normal">Symbol</th>
              <th className="pb-1 text-right font-normal">Feed bid / ask</th>
              <th className="pb-1 text-right font-normal">Markup</th>
              <th className="pb-1 text-right font-normal">Your bid / ask</th>
            </tr>
          </thead>
          <tbody className="font-mono tabular-nums">
            {report.prices.map((p) => {
              const digits = digitsOf(p.symbol);
              return (
                <tr key={p.symbol} className="border-t border-border">
                  <td className="py-1.5 font-sans font-medium">{p.symbol}</td>
                  <td className="py-1.5 text-right">{p.feed ? `${formatPrice(p.feed.bid, digits)} / ${formatPrice(p.feed.ask, digits)}` : "-"}</td>
                  <td className="py-1.5 text-right text-muted">{p.bidMarkupPoints === null || p.askMarkupPoints === null ? "-" : `${p.bidMarkupPoints + p.askMarkupPoints} pts`}</td>
                  <td className="py-1.5 text-right">
                    {formatPrice(p.bid, digits)} / {formatPrice(p.ask, digits)}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </SheetSection>

      <SheetSection title="Open positions at that moment">
        <table className="w-full text-sm">
          <thead>
            <tr className="text-left text-[11px] text-muted">
              <th className="pb-1 font-normal">Position</th>
              <th className="pb-1 text-right font-normal">Opened at</th>
              <th className="pb-1 text-right font-normal">Price</th>
              <th className="pb-1 text-right font-normal">Result</th>
            </tr>
          </thead>
          <tbody className="font-mono tabular-nums">
            {report.positions.map((p) => {
              const digits = digitsOf(p.symbol);
              return (
                <tr key={p.positionId} className="border-t border-border">
                  <td className="py-1.5 font-sans">
                    {p.side} {formatVolume(p.volume)} {p.symbol}
                  </td>
                  <td className="py-1.5 text-right">{formatPrice(p.openPrice, digits)}</td>
                  <td className="py-1.5 text-right">{formatPrice(p.currentPrice, digits)}</td>
                  <td className={`py-1.5 text-right ${p.profit >= 0 ? "text-profit" : "text-loss"}`}>{formatSignedMoney(p.profit)}</td>
                </tr>
              );
            })}
            <tr className="border-t border-border">
              <td colSpan={3} className="py-1.5 font-sans text-muted">
                Balance {formatMoney(report.balance)}, with these results
              </td>
              <td className="py-1.5 text-right font-semibold">{formatMoney(report.equity)}</td>
            </tr>
          </tbody>
        </table>
      </SheetSection>

      <SheetSection title="Step by step">
        <ol className="flex flex-col gap-2">
          {report.events.map((e) => (
            <li key={e.sequence} className="grid grid-cols-[4.5rem_minmax(0,1fr)] gap-3">
              <span className="font-mono text-xs text-muted tabular-nums">{formatTime(e.event.timestamp, timeZone)}</span>
              <span className={`text-xs leading-relaxed ${isWarning(e.event) ? "text-warning" : ""}`}>{describeEvent(e.event, digitsOf)}</span>
            </li>
          ))}
        </ol>
        <p className="text-xs text-muted">Every position was closed at the prices that broke the limit. Balance after: {formatMoney(report.balanceAfter)}.</p>
      </SheetSection>

      <p className="flex gap-2 px-5 py-4 text-xs leading-relaxed text-muted">
        <ProofIcon className="mt-px size-4 shrink-0 text-accent" />
        <span>
          Every price and step here comes from the trading platform&apos;s journal, which keeps every price it receives. The same prices played again give the
          same result.
        </span>
      </p>
    </>
  );
}

function Figure({ label, value, className = "" }: { label: string; value: string; className?: string }) {
  return (
    <div className="flex flex-col gap-0.5 rounded-lg bg-background px-3 py-2">
      <span className="text-[11px] text-muted">{label}</span>
      <span className={`font-mono text-base font-semibold tabular-nums ${className}`}>{value}</span>
    </div>
  );
}

const chartBox = { width: 440, height: 160, padding: 12 };

// Equity as a line, the limit dashed, the periods without prices shaded and the breach as a dot.
function EquityChart({ report, timeZone }: { report: BreachReport; timeZone: string }) {
  const chart = equityChart(report.equityCurve, report.level, report.gaps, chartBox);
  const gapText = report.gaps.map((g) => `${formatTime(g.from, timeZone)} to ${formatTime(g.to, timeZone)}`).join(", ");
  return (
    <figure className="m-0 flex flex-col gap-1.5">
      <svg
        width="100%"
        height={chartBox.height}
        viewBox={`0 0 ${chartBox.width} ${chartBox.height}`}
        preserveAspectRatio="none"
        role="img"
        aria-label={`Equity before the breach, down to ${formatMoney(report.equity)} below the limit of ${formatMoney(report.level)}${gapText ? `, with no prices ${gapText}` : ""}.`}
        className="rounded-lg border border-border bg-background"
      >
        {chart.gaps.map((g) => (
          <rect key={g.x} x={g.x} y={0} width={g.width} height={chartBox.height} className="fill-warning/10" />
        ))}
        <line x1={0} x2={chartBox.width} y1={chart.levelY} y2={chart.levelY} className="stroke-loss" strokeWidth="1.5" strokeDasharray="6 5" vectorEffect="non-scaling-stroke" />
        <polyline fill="none" className="stroke-foreground" strokeWidth="1.75" vectorEffect="non-scaling-stroke" points={chart.line} />
        {chart.breach && <circle cx={chart.breach.x} cy={chart.breach.y} r="5" className="fill-loss" />}
      </svg>
      {gapText && <figcaption className="text-xs text-warning">No prices {gapText}.</figcaption>}
    </figure>
  );
}
