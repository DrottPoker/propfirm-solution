"use client";

import Link from "next/link";

import { useBranding } from "@/app/providers";
import type { AccountDetails, BreachReport } from "@/lib/api/types";
import { formatDateTime, formatLots, formatMoney, formatSignedMoney, timeZoneName } from "@/lib/format";
import { belowBy, equityChart, floorLabel, priceDigits, priceText, stepText, stepTone } from "@/lib/proof";
import { type Role, useBreachReport, useFirmAccount, useMyAccount } from "@/lib/queries";

import { FileCheckIcon, PrintIcon } from "./icons";
import { AdminPage, Loading, Message, PageHeader, Panel, secondaryButtonClass, StatTile, TraderPage } from "./ui";

// Why a loss limit was broken, with the feed's prices behind it (ADR 0053), on a page of its own that prints as a
// document. A trade's details are a panel instead, in TradeDetails.tsx.

export function TraderBreachReport({ accountId }: { accountId: string }) {
  const details = useMyAccount(accountId);
  return <BreachReportFor role="trader" accountId={accountId} details={details.data} error={details.error} />;
}

export function AdminBreachReport({ accountId }: { accountId: string }) {
  const details = useFirmAccount(accountId);
  return <BreachReportFor role="admin" accountId={accountId} details={details.data} error={details.error} />;
}

type Props = { role: Role; accountId: string; details: AccountDetails | undefined; error: Error | null };

/** The account's page, for the trader or in the admin panel. */
function accountHref(role: Role, accountId: string): string {
  return role === "admin" ? `/admin/accounts/${accountId}` : `/accounts/${accountId}`;
}

function Frame({ role, children }: { role: Role; children: React.ReactNode }) {
  return role === "admin" ? <AdminPage narrow>{children}</AdminPage> : <TraderPage narrow>{children}</TraderPage>;
}

function Breadcrumb({ role, details, page }: { role: Role; details: AccountDetails; page: string }) {
  return (
    <nav aria-label="Breadcrumb" className="text-sm text-muted print:hidden">
      <Link href={role === "admin" ? "/admin/accounts" : "/"} className="hover:text-foreground">
        Accounts
      </Link>{" "}
      <span aria-hidden="true">/</span>{" "}
      <Link href={accountHref(role, details.account.id)} className="hover:text-foreground">
        #{details.account.number} {details.challenge.name}, {details.account.stageName}
      </Link>{" "}
      <span aria-hidden="true">/</span> <span className="text-foreground">{page}</span>
    </nav>
  );
}

function BreachReportFor({ role, accountId, details, error }: Props) {
  const report = useBreachReport(accountId, role);
  const branding = useBranding();
  if (error || report.error) {
    return <Message text={(error ?? report.error)?.message ?? ""} />;
  }

  if (!details || !report.data) {
    return <Loading label="Loading the breach report" />;
  }

  const data = report.data;
  const timeZone = details.challenge.tradingDay.timeZone;
  // The report has no instrument decimals, so each symbol shows as many as its prices have.
  const digitsOf = (symbol: string) =>
    priceDigits([
      ...data.prices.filter((p) => p.symbol === symbol).flatMap((p) => [p.bid, p.ask, p.feed?.bid, p.feed?.ask]),
      ...data.positions.filter((p) => p.symbol === symbol).flatMap((p) => [p.openPrice, p.currentPrice]),
    ]);
  const reinstated = details.account.status === "Active" || details.account.status === "OpeningAccount";
  return (
    <Frame role={role}>
      <PageHeader
        back={<Breadcrumb role={role} details={details} page="Breach report" />}
        title={`The ${floorLabel(data.floorId).toLowerCase()} was broken`}
        description={`${formatDateTime(data.at, timeZone)} ${timeZoneName(timeZone)}, on account ${data.accountId}.`}
      />

      {reinstated && (
        <p role="note" className="flex flex-wrap items-center gap-3 rounded-xl border border-accent/40 bg-accent/10 px-4 py-3.5 text-sm">
          <span className="flex-1">
            {branding.name} has reinstated this phase. It trades again on the same account, with a balance of {formatMoney(details.account.balance)}{" "}
            {details.account.currency}.
          </span>
          <Link href={accountHref(role, accountId)} className={`${secondaryButtonClass} text-sm print:hidden`}>
            Open the account
          </Link>
        </p>
      )}

      <dl className="grid grid-cols-3 gap-3">
        <StatTile label={floorLabel(data.floorId)} value={formatMoney(data.level)} />
        <StatTile label="Equity at that moment" value={formatMoney(data.equity)} tone="text-loss" />
        <StatTile label="Below the limit by" value={formatMoney(belowBy(data))} />
      </dl>

      <Panel title="Equity, price by price">
        <EquityChart report={data} timeZone={timeZone} />
        <p className="text-sm text-muted">Equity is counted again from every price the trading platform received, with the positions open at the time.</p>
      </Panel>

      <Panel title={`The prices that broke it, received ${formatTime(data.at, timeZone, true)}`}>
        <div className="-mx-5 overflow-x-auto px-5">
          <table className="w-full min-w-[36rem] text-sm">
            <thead className="text-left text-muted">
              <tr>
                <th scope="col" className="py-2 font-normal">
                  Symbol
                </th>
                <th scope="col" className="py-2 pl-4 text-right font-normal">
                  From the feed, bid / ask
                </th>
                <th scope="col" className="py-2 pl-4 text-right font-normal">
                  {role === "admin" ? "Your markup" : `${branding.name}'s markup`}
                </th>
                <th scope="col" className="py-2 pl-4 text-right font-normal">
                  {role === "admin" ? "The trader's bid / ask" : "Your bid / ask"}
                </th>
              </tr>
            </thead>
            <tbody className="font-mono tabular-nums">
              {data.prices.map((p) => {
                const digits = digitsOf(p.symbol);
                return (
                  <tr key={p.symbol} className="border-t border-border">
                    <td className="py-2.5 font-sans font-medium">{p.symbol}</td>
                    <td className="py-2.5 pl-4 text-right">{p.feed ? `${priceText(p.feed.bid, digits)} / ${priceText(p.feed.ask, digits)}` : "-"}</td>
                    <td className="py-2.5 pl-4 text-right text-muted">
                      {p.bidMarkupPoints === null || p.askMarkupPoints === null ? "-" : `${p.bidMarkupPoints + p.askMarkupPoints} points`}
                    </td>
                    <td className="py-2.5 pl-4 text-right">
                      {priceText(p.bid, digits)} / {priceText(p.ask, digits)}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </Panel>

      <Panel title="Open positions at that moment">
        <div className="-mx-5 overflow-x-auto px-5">
          <table className="w-full min-w-[36rem] text-sm">
            <thead className="text-left text-muted">
              <tr>
                <th scope="col" className="py-2 font-normal">
                  Symbol
                </th>
                <th scope="col" className="py-2 pl-4 font-normal">
                  Side
                </th>
                <th scope="col" className="py-2 pl-4 text-right font-normal">
                  Lots
                </th>
                <th scope="col" className="py-2 pl-4 text-right font-normal">
                  Open price
                </th>
                <th scope="col" className="py-2 pl-4 text-right font-normal">
                  Price then
                </th>
                <th scope="col" className="py-2 pl-4 text-right font-normal">
                  Result
                </th>
              </tr>
            </thead>
            <tbody className="font-mono tabular-nums">
              {data.positions.map((p) => (
                <tr key={p.positionId} className="border-t border-border">
                  <td className="py-2.5 font-sans font-medium">{p.symbol}</td>
                  <td className={`py-2.5 pl-4 font-sans ${p.side === "Buy" ? "text-profit" : "text-loss"}`}>{p.side}</td>
                  <td className="py-2.5 pl-4 text-right">{formatLots(p.volume)}</td>
                  <td className="py-2.5 pl-4 text-right">{priceText(p.openPrice, digitsOf(p.symbol))}</td>
                  <td className="py-2.5 pl-4 text-right">{priceText(p.currentPrice, digitsOf(p.symbol))}</td>
                  <td className={`py-2.5 pl-4 text-right ${p.profit >= 0 ? "text-profit" : "text-loss"}`}>{formatSignedMoney(p.profit)}</td>
                </tr>
              ))}
              <tr className="border-t border-border">
                <td colSpan={5} className="py-2.5 font-sans text-muted">
                  Balance {formatMoney(data.balance)}, with these results
                </td>
                <td className="py-2.5 pl-4 text-right font-semibold">{formatMoney(data.equity)}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </Panel>

      <Panel title="Step by step">
        <ol className="flex flex-col">
          {data.steps.map((step, index) => (
            <li key={`${step.at}-${index}`} className="grid grid-cols-[6.5rem_0.75rem_minmax(0,1fr)] items-start gap-3 py-1.5">
              <span className="font-mono text-xs text-muted tabular-nums">{formatTime(step.at, timeZone, true)}</span>
              <span
                aria-hidden="true"
                className={`mt-1.5 size-2.5 rounded-full ${stepTone(step) === "loss" ? "bg-loss" : stepTone(step) === "warning" ? "bg-warning" : "bg-muted"}`}
              />
              <span className={`text-sm leading-relaxed ${stepTone(step) === "warning" ? "text-warning" : ""}`}>{stepText(step, data.currency)}</span>
            </li>
          ))}
        </ol>
        <p className="text-sm text-muted">
          Every position was closed at the prices that broke the limit. Balance after: {formatMoney(data.balanceAfter)} {data.currency}.
        </p>
      </Panel>

      <ReportFooter />
    </Frame>
  );
}

function ReportFooter() {
  return (
    <footer className="flex flex-wrap items-center justify-between gap-4">
      <p className="flex max-w-xl gap-2 text-sm leading-relaxed text-muted">
        <FileCheckIcon className="mt-0.5 size-4 text-accent" />
        <span>
          Every price and step here comes from the trading platform&apos;s journal, which keeps every price it receives. The same prices played again give
          the same result.
        </span>
      </p>
      <button type="button" onClick={() => window.print()} className={`${secondaryButtonClass} flex items-center gap-1.5 text-sm print:hidden`}>
        <PrintIcon className="size-4" />
        Print or save as PDF
      </button>
    </footer>
  );
}

function formatTime(iso: string, timeZone: string, seconds = false): string {
  return new Date(iso).toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit", second: seconds ? "2-digit" : undefined, timeZone });
}

const chartBox = { width: 960, height: 240, padding: 16 };

// Equity as a line, the limit dashed, the periods without prices shaded and the breach as a dot.
function EquityChart({ report, timeZone }: { report: BreachReport; timeZone: string }) {
  const chart = equityChart(report.equityCurve, report.level, report.gaps, chartBox);
  const gapText = report.gaps.map((g) => `${formatTime(g.from, timeZone, true)} to ${formatTime(g.to, timeZone, true)}`).join(", ");
  return (
    <figure className="m-0 flex flex-col gap-2">
      <div className="relative">
        <svg
          width="100%"
          height={chartBox.height}
          viewBox={`0 0 ${chartBox.width} ${chartBox.height}`}
          preserveAspectRatio="none"
          role="img"
          aria-label={`Equity before the breach, down to ${formatMoney(report.equity)}, below the limit of ${formatMoney(report.level)}${gapText ? `, with no prices ${gapText}` : ""}.`}
          className="block rounded-lg border border-border bg-background"
        >
          {chart.gaps.map((g) => (
            <rect key={g.x} x={g.x} y={0} width={g.width} height={chartBox.height} className="fill-warning/10" />
          ))}
          <line x1={0} x2={chartBox.width} y1={chart.levelY} y2={chart.levelY} className="stroke-loss" strokeWidth="1.5" strokeDasharray="6 5" vectorEffect="non-scaling-stroke" />
          <polyline fill="none" className="stroke-foreground" strokeWidth="2" vectorEffect="non-scaling-stroke" points={chart.line} />
        </svg>
        {chart.breach && <Dot x={chart.breach.x / chartBox.width} y={chart.breach.y / chartBox.height} className="size-3 bg-loss" />}
      </div>
      <figcaption className="flex flex-wrap justify-between gap-2 text-xs text-muted">
        <span>
          {report.equityCurve.length > 0 && `${formatTime(report.equityCurve[0].time, timeZone)} to ${formatTime(report.at, timeZone)} ${timeZoneName(timeZone)}`}
        </span>
        <span className="text-loss">Limit {formatMoney(report.level)}</span>
        {gapText && <span className="w-full text-warning">No prices {gapText}.</span>}
      </figcaption>
    </figure>
  );
}

/** A round mark at a point of a chart that is stretched to its width, at the fraction of its width and height. */
function Dot({ x, y, className }: { x: number; y: number; className: string }) {
  return (
    <span
      aria-hidden="true"
      className={`pointer-events-none absolute -translate-x-1/2 -translate-y-1/2 rounded-full ${className}`}
      style={{ left: `${x * 100}%`, top: `${y * 100}%` }}
    />
  );
}
