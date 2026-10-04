"use client";

import { useMemo, useState } from "react";

import type { BalanceChangeKind, Performance } from "@/lib/api/types";
import { balanceDomain, levelAt, linearScale, nearestIndex, niceTicks, stepPath, timeAxis } from "@/lib/chart";
import { formatAxisMoney, formatDateTime, formatMoney, formatSignedMoney } from "@/lib/format";
import { useElementWidth } from "@/lib/useElementWidth";

const height = 260;
const margin = { top: 14, right: 76, bottom: 28, left: 10 };

type Point = { time: number; value: number; change: number | null; kind: BalanceChangeKind | "Now" };

const kindText: Record<Point["kind"], string> = {
  Created: "Account opened",
  Opened: "Position opened, commission",
  Closed: "Position closed",
  Adjusted: "Deposit or withdrawal",
  Now: "Equity now",
};

function pointText(point: Point): string {
  return point.kind === "Adjusted" ? ((point.change ?? 0) < 0 ? "Withdrawal" : "Deposit") : kindText[point.kind];
}

/**
 * The stage's balance after every change, as a step line, with the profit target and the loss limits. Equity now
 * is a dot at the end while the stage is traded, since equity is not kept over time. Hovering, or the arrow keys,
 * read each change, and the same changes are in a table below.
 */
export function BalanceChart({
  performance,
  timeZone,
  equity,
  floating,
  endTime,
}: {
  performance: Performance;
  /** The challenge's time zone, which the times are shown in. */
  timeZone: string;
  equity: number | null;
  floating: number | null;
  endTime: number;
}) {
  const { ref: container, element, width } = useElementWidth<HTMLDivElement>();
  const [active, setActive] = useState<number | null>(null);

  const model = useMemo(() => {
    const balance: Point[] = performance.balance.map((b) => ({ time: Date.parse(b.time), value: b.balance, change: b.change, kind: b.kind }));
    const daily = performance.dailyFloor.map((f) => ({ time: Date.parse(f.time), level: f.level }));
    const maxLoss = performance.maxLossFloor.map((f) => ({ time: Date.parse(f.time), level: f.level }));
    // From the first change to now, or to the last change once the stage is over. A range of less than a minute is
    // widened backwards, so the line never runs on past now.
    const first = balance[0]?.time ?? endTime;
    const end = Math.max(endTime, balance.at(-1)?.time ?? endTime);
    const start = Math.min(first, end - 60_000);
    const points = equity == null ? balance : [...balance, { time: end, value: equity, change: floating, kind: "Now" as const }];
    const { domain, maxLossShown } = balanceDomain({
      balances: balance.map((b) => b.value),
      equity,
      target: performance.profitTarget,
      daily: daily.map((d) => d.level),
      maxLoss: maxLoss.map((m) => m.level),
      minSpan: performance.initialBalance * 0.02,
    });
    return { balance, points, daily, maxLoss, first, start, end, domain, maxLossShown };
  }, [performance, equity, floating, endTime]);

  if (model.balance.length === 0) {
    return <p className="py-10 text-center text-sm text-muted">Nothing has happened on this stage&apos;s trading account yet.</p>;
  }

  const right = Math.max(width - margin.right, margin.left + 1);
  const bottom = height - margin.bottom;
  const x = linearScale([model.start, model.end], [margin.left, right]);
  const y = linearScale(model.domain, [bottom, margin.top]);
  const line = stepPath(
    model.balance.map((p) => ({ x: x(p.time), y: y(p.value) })),
    x(model.end),
  );
  const yTicks = niceTicks(model.domain[0], model.domain[1], 5).filter((t) => t >= model.domain[0] && t <= model.domain[1]);
  const xTicks = timeAxis(model.start, model.end, width < 480 ? 3 : 5, timeZone);
  const target = performance.profitTarget;
  const current = active === null ? null : (model.points[active] ?? null);
  const dailyAtCurrent = current ? levelAt(model.daily, current.time) : null;
  const lastDaily = model.daily.at(-1)?.level ?? null;
  const lastMaxLoss = model.maxLoss.at(-1)?.level ?? null;

  const pick = (clientX: number) => {
    const box = element?.getBoundingClientRect();
    if (!box) {
      return;
    }

    const time = model.start + ((clientX - box.left - margin.left) / (right - margin.left)) * (model.end - model.start);
    setActive(nearestIndex(model.points.map((p) => p.time), time));
  };

  const step = (event: React.KeyboardEvent) => {
    const last = model.points.length - 1;
    const moves: Record<string, number> = {
      ArrowLeft: Math.max((active ?? last + 1) - 1, 0),
      ArrowRight: Math.min((active ?? -1) + 1, last),
      Home: 0,
      End: last,
    };
    if (event.key in moves) {
      event.preventDefault();
      setActive(moves[event.key]);
    } else if (event.key === "Escape") {
      setActive(null);
    }
  };

  return (
    <figure className="m-0 flex flex-col gap-3">
      <div
        ref={container}
        tabIndex={0}
        role="group"
        aria-label="Balance chart. Use the left and right arrow keys to read each change."
        onPointerMove={(e) => pick(e.clientX)}
        onPointerLeave={() => setActive(null)}
        onKeyDown={step}
        onBlur={() => setActive(null)}
        className="relative touch-pan-y rounded outline-none focus-visible:ring-2 focus-visible:ring-accent"
        style={{ height }}
      >
        {width > 0 && (
          <svg width={width} height={height} aria-hidden="true" className="block overflow-visible">
            {yTicks.map((tick) => (
              <g key={tick}>
                <line x1={margin.left} x2={right} y1={y(tick)} y2={y(tick)} className="stroke-border" strokeWidth={1} />
                <text x={right + 8} y={y(tick)} dy="0.32em" className="fill-muted font-mono text-[11px] tabular-nums">
                  {formatAxisMoney(tick)}
                </text>
              </g>
            ))}
            {xTicks.map((tick, i) => (
              <text
                key={tick.time}
                x={x(tick.time)}
                y={height - 8}
                textAnchor={i === 0 ? "start" : i === xTicks.length - 1 && tick.time === model.end ? "end" : "middle"}
                className="fill-muted text-[11px]"
              >
                {tick.label}
              </text>
            ))}

            <path d={`${line}V${bottom}H${x(model.first)}Z`} className="fill-accent/10" />
            {target != null && <line x1={margin.left} x2={right} y1={y(target)} y2={y(target)} className="stroke-profit" strokeWidth={1.5} strokeDasharray="6 4" />}
            {model.daily.length > 0 && (
              <path
                d={stepPath(model.daily.map((d) => ({ x: x(Math.max(d.time, model.start)), y: y(d.level) })), x(model.end))}
                fill="none"
                className="stroke-warning"
                strokeWidth={1.5}
                strokeDasharray="3 3"
              />
            )}
            {model.maxLossShown && (
              <path
                d={stepPath(model.maxLoss.map((d) => ({ x: x(Math.max(d.time, model.start)), y: y(d.level) })), x(model.end))}
                fill="none"
                className="stroke-loss"
                strokeWidth={1.5}
                strokeDasharray="1 3"
                strokeLinecap="round"
              />
            )}
            <path d={line} fill="none" className="stroke-accent" strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
            {equity != null && <circle cx={x(model.end)} cy={y(equity)} r={4.5} className="fill-accent stroke-panel" strokeWidth={2} />}

            {current && (
              <g>
                <line x1={x(current.time)} x2={x(current.time)} y1={margin.top} y2={bottom} className="stroke-muted" strokeWidth={1} />
                <circle cx={x(current.time)} cy={y(current.value)} r={4.5} className="fill-accent stroke-panel" strokeWidth={2} />
              </g>
            )}
          </svg>
        )}

        {current && (
          <div
            role="status"
            className="pointer-events-none absolute top-2 z-10 flex w-56 flex-col gap-1 rounded-md border border-border bg-background px-3 py-2 text-xs shadow-lg"
            style={{ left: Math.min(Math.max(x(current.time) + 12, 0), Math.max(width - 232, 0)) }}
          >
            <span className="text-muted">{formatDateTime(new Date(current.time).toISOString(), timeZone)}</span>
            <span className="font-mono text-sm font-medium">{formatMoney(current.value)}</span>
            <span className="text-muted">
              {pointText(current)}
              {current.change != null && current.kind !== "Created" && <span className="font-mono"> {formatSignedMoney(current.change)}</span>}
              {current.kind === "Now" && current.change != null && " in open positions"}
            </span>
            {dailyAtCurrent != null && <span className="text-muted">Daily loss limit {formatMoney(dailyAtCurrent)}</span>}
          </div>
        )}
      </div>

      <figcaption className="flex flex-wrap gap-x-5 gap-y-1.5 text-xs text-muted">
        <Key kind="line">Balance after each change</Key>
        {equity != null && <Key kind="dot">Equity now {formatMoney(equity)}</Key>}
        {target != null && <Key kind="target">Profit target {formatMoney(target)}</Key>}
        {lastDaily != null && (
          <Key kind="daily">
            Daily loss limit {formatMoney(lastDaily)}
            {model.daily.length > 1 && ", moves each day"}
          </Key>
        )}
        {lastMaxLoss != null && (
          <Key kind={model.maxLossShown ? "max" : "none"}>
            Max loss limit {formatMoney(lastMaxLoss)}
            {!model.maxLossShown && ", below the chart"}
          </Key>
        )}
      </figcaption>

      <details className="text-sm">
        <summary className="cursor-pointer text-muted hover:text-foreground">Show the balance as a table</summary>
        <div className="mt-2 max-h-72 overflow-auto">
          <table className="w-full text-sm">
            <thead className="sticky top-0 bg-panel text-left text-muted">
              <tr>
                <th scope="col" className="py-1.5 font-normal">
                  Time
                </th>
                <th scope="col" className="py-1.5 pl-4 font-normal">
                  What
                </th>
                <th scope="col" className="py-1.5 pl-4 text-right font-normal">
                  Change
                </th>
                <th scope="col" className="py-1.5 pl-4 text-right font-normal">
                  Balance
                </th>
              </tr>
            </thead>
            <tbody>
              {[...model.balance].reverse().map((point, i) => (
                <tr key={`${point.time}-${i}`} className="border-t border-border">
                  <td className="py-1.5 text-muted">{formatDateTime(new Date(point.time).toISOString(), timeZone)}</td>
                  <td className="py-1.5 pl-4">{pointText(point)}</td>
                  <td className="py-1.5 pl-4 text-right font-mono tabular-nums">{point.kind === "Created" ? "" : formatSignedMoney(point.change)}</td>
                  <td className="py-1.5 pl-4 text-right font-mono tabular-nums">{formatMoney(point.value)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </figure>
  );
}

function Key({ kind, children }: { kind: "line" | "dot" | "target" | "daily" | "max" | "none"; children: React.ReactNode }) {
  return (
    <span className="flex items-center gap-1.5">
      {kind !== "none" && (
        <svg width="16" height="8" aria-hidden="true" className="shrink-0">
          {kind === "dot" ? (
            <circle cx="8" cy="4" r="3.5" className="fill-accent" />
          ) : (
            <line
              x1="1"
              x2="15"
              y1="4"
              y2="4"
              strokeWidth={kind === "line" ? 2 : 1.5}
              strokeLinecap="round"
              strokeDasharray={kind === "target" ? "6 4" : kind === "daily" ? "3 3" : kind === "max" ? "1 3" : undefined}
              className={kind === "line" ? "stroke-accent" : kind === "target" ? "stroke-profit" : kind === "daily" ? "stroke-warning" : "stroke-loss"}
            />
          )}
        </svg>
      )}
      {children}
    </span>
  );
}
