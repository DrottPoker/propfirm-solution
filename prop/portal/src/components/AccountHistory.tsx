"use client";

import { useState } from "react";

import type { AccountDetails, DayResult, Trade, TradeStatistics } from "@/lib/api/types";
import { isTrading, resultTone, toneText } from "@/lib/dashboard";
import { formatDay, formatLots, formatMoney, formatPrice, formatRatio, formatShortDateTime, formatSignedMoney } from "@/lib/format";
import { usePerformance, useTrades } from "@/lib/queries";

import { BalanceChart } from "./BalanceChart";
import { ErrorText, Panel, SegmentedControl, secondaryButtonClass } from "./ui";

/** Close reasons from the trading platform, in words. An unknown one is shown as it is. */
const closeReasons: Record<string, string> = {
  Manual: "Closed",
  StopLoss: "Stop loss",
  TakeProfit: "Take profit",
  StopOut: "Stop out",
  EquityFloor: "Loss limit",
  AccountClosed: "Account closed",
};

/**
 * How a stage has gone: its balance, its days, statistics and closed trades. The stage is chosen in one row above
 * everything it changes, and the current stage is shown first. What is shown stays while another stage loads.
 * `now` is when the account's figures were last fetched, where equity now is drawn.
 */
export function AccountHistory({ details, now }: { details: AccountDetails; now: number }) {
  const started = details.stages.filter((s) => s.startedAt !== null);
  const [chosen, setChosen] = useState<number | null>(null);
  const stage = chosen ?? started.at(-1)?.stage ?? null;
  const performance = usePerformance(details.account.id, stage, details.historyVersion);

  const data = performance.data;
  const tradingThisStage = isTrading(details) && data?.stage === details.account.stage;
  const loading = performance.isPlaceholderData || (performance.isFetching && !data);

  return (
    <section aria-labelledby="history-heading" className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="history-heading" className="text-lg font-semibold">
          History{data ? ` · ${data.stageName}` : ""}
        </h2>
        {started.length > 1 && (
          <SegmentedControl
            label="Stage"
            options={started.map((s) => ({ value: s.stage, label: s.progress === "Current" ? `${s.name} · now` : s.name }))}
            value={stage ?? 0}
            onChange={setChosen}
          />
        )}
      </div>

      <ErrorText error={performance.error} />
      {!data ? (
        <p className="text-muted">Loading...</p>
      ) : (
        <div className={`flex flex-col gap-5 transition-opacity ${loading ? "opacity-60" : ""}`}>
          <Panel title="Balance">
            <BalanceChart
              performance={data}
              equity={tradingThisStage ? details.results.equity : null}
              floating={tradingThisStage ? details.results.floating : null}
              endTime={tradingThisStage ? now : data.balance.length > 0 ? Date.parse(data.balance[data.balance.length - 1].time) : now}
            />
          </Panel>
          <div className="grid grid-cols-1 gap-5 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
            <Panel title="Day by day">
              <Days days={data.days} />
            </Panel>
            <Panel title="Statistics">
              <Statistics statistics={data.statistics} />
            </Panel>
          </div>
          <ClosedTrades details={details} stage={data.stage} />
        </div>
      )}
    </section>
  );
}

function Days({ days }: { days: DayResult[] }) {
  if (days.length === 0) {
    return <p className="text-sm text-muted">No trading days yet.</p>;
  }

  return (
    <div className="max-h-96 overflow-auto">
      <table className="w-full min-w-[28rem] text-sm">
        <thead className="sticky top-0 bg-panel text-left text-muted">
          <tr>
            <th scope="col" className="py-2 font-normal">
              Day
            </th>
            <th scope="col" className="py-2 pl-4 text-right font-normal">
              Trades
            </th>
            <th scope="col" className="py-2 pl-4 text-right font-normal">
              Lots
            </th>
            <th scope="col" className="py-2 pl-4 text-right font-normal">
              Result
            </th>
            <th scope="col" className="py-2 pl-4 font-normal">
              Trading day
            </th>
          </tr>
        </thead>
        <tbody>
          {days.map((day) => (
            <tr key={day.day} className="border-t border-border">
              <td className="py-2">{formatDay(day.day)}</td>
              <td className="py-2 pl-4 text-right font-mono tabular-nums">{day.trades}</td>
              <td className="py-2 pl-4 text-right font-mono tabular-nums">{day.trades > 0 ? formatLots(day.lots) : "-"}</td>
              <td className={`py-2 pl-4 text-right font-mono tabular-nums ${toneText[resultTone(day.result)]}`}>{day.trades > 0 ? formatSignedMoney(day.result) : "-"}</td>
              <td className={`py-2 pl-4 ${day.counted ? "text-profit" : "text-muted"}`}>{day.counted ? "Counted" : "No new trade"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Statistics({ statistics: s }: { statistics: TradeStatistics }) {
  if (s.trades === 0) {
    return <p className="text-sm text-muted">Statistics appear when the first trade is closed.</p>;
  }

  const figures: { label: string; value: string; tone?: string }[] = [
    { label: "Closed trades", value: `${s.trades}` },
    { label: "Winning trades", value: s.winRatePercent == null ? `${s.wins}` : `${s.wins} · ${s.winRatePercent}%` },
    { label: "Average win", value: formatSignedMoney(s.averageWin), tone: s.averageWin == null ? undefined : "text-profit" },
    { label: "Average loss", value: formatSignedMoney(s.averageLoss), tone: s.averageLoss == null ? undefined : "text-loss" },
    { label: "Best trade", value: formatSignedMoney(s.bestTrade) },
    { label: "Worst trade", value: formatSignedMoney(s.worstTrade) },
    { label: "Profit factor", value: s.profitFactor == null ? (s.losses === 0 ? "No losses" : "-") : formatRatio(s.profitFactor) },
    { label: "Lots traded", value: formatLots(s.lots) },
    { label: "Result", value: formatSignedMoney(s.result), tone: toneText[resultTone(s.result)] },
    { label: "Commission", value: formatMoney(s.commission) },
  ];
  return (
    <dl className="grid grid-cols-2 gap-x-4 gap-y-3.5 text-xs">
      {figures.map((f) => (
        <div key={f.label} className="flex flex-col gap-0.5">
          <dt className="text-muted">{f.label}</dt>
          <dd className={`font-mono text-base ${f.tone ?? ""}`}>{f.value}</dd>
        </div>
      ))}
    </dl>
  );
}

function ClosedTrades({ details, stage }: { details: AccountDetails; stage: number }) {
  const trades = useTrades(details.account.id, stage, details.historyVersion);
  const rows: Trade[] = trades.data?.pages.flatMap((p) => p.trades) ?? [];

  return (
    <Panel
      title="Closed trades"
      actions={
        rows.length > 0 && (
          <a href={`/api/portal/accounts/${details.account.id}/trades.csv?stage=${stage}`} download className="text-sm text-accent hover:underline">
            Download CSV
          </a>
        )
      }
    >
      <ErrorText error={trades.error} />
      {rows.length === 0 ? (
        <p className="text-sm text-muted">{trades.isPending ? "Loading..." : "No closed trades yet."}</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[52rem] text-sm">
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
                <th scope="col" className="py-2 pl-4 font-normal">
                  Opened
                </th>
                <th scope="col" className="py-2 pl-4 text-right font-normal">
                  Open price
                </th>
                <th scope="col" className="py-2 pl-4 font-normal">
                  Closed
                </th>
                <th scope="col" className="py-2 pl-4 text-right font-normal">
                  Close price
                </th>
                <th scope="col" className="py-2 pl-4 text-right font-normal">
                  Result
                </th>
              </tr>
            </thead>
            <tbody>
              {rows.map((trade) => (
                <tr key={trade.positionId} className="border-t border-border">
                  <td className="py-2 font-medium">{trade.symbol}</td>
                  <td className={`py-2 pl-4 ${trade.side === "Buy" ? "text-profit" : "text-loss"}`}>{trade.side}</td>
                  <td className="py-2 pl-4 text-right font-mono tabular-nums">{formatLots(trade.volume)}</td>
                  <td className="whitespace-nowrap py-2 pl-4 text-muted">{trade.openedAt ? formatShortDateTime(trade.openedAt) : "-"}</td>
                  <td className="py-2 pl-4 text-right font-mono tabular-nums">{formatPrice(trade.openPrice)}</td>
                  <td className="whitespace-nowrap py-2 pl-4 text-muted">
                    {formatShortDateTime(trade.closedAt)}
                    {trade.closeReason !== "Manual" && <span className="block text-xs">{closeReasons[trade.closeReason] ?? trade.closeReason}</span>}
                  </td>
                  <td className="py-2 pl-4 text-right font-mono tabular-nums">{formatPrice(trade.closePrice)}</td>
                  <td className="py-2 pl-4 text-right font-mono tabular-nums">
                    <span className={toneText[resultTone(trade.result)]}>{formatSignedMoney(trade.result)}</span>
                    {trade.commission > 0 && <span className="block text-xs text-muted">after {formatMoney(trade.commission)} commission</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {trades.hasNextPage && (
        <button type="button" onClick={() => trades.fetchNextPage()} disabled={trades.isFetchingNextPage} className={`${secondaryButtonClass} self-start text-sm`}>
          {trades.isFetchingNextPage ? "Loading..." : "Show more"}
        </button>
      )}
    </Panel>
  );
}
