"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";

import type { Exposure } from "@/lib/api/types";
import { formatCount, formatCountOf, formatLarge, formatLots, formatSignedLots, formatSignedMoney, formatSignedWhole } from "@/lib/format";
import { useExposure, useServers } from "@/lib/queries";

import { LongShortBar } from "./charts";
import { ErrorText, FigureCard, FigureGrid, fieldClass, Loading, PageHeader, Panel, TableBox, tdClass, thClass } from "./ui";

/** What traders hold now, summed per symbol, valued in USD, across every server or one (ADR 0057). */
export function ExposurePage({ server }: { server: string | null }) {
  const router = useRouter();
  const exposure = useExposure(server);
  const servers = useServers("all", "");

  const choose = (id: string) => router.replace(id ? `/exposure?server=${encodeURIComponent(id)}` : "/exposure");

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Exposure"
        text="What traders hold right now, summed per symbol. Long minus short, valued in USD at the feed's prices, every few seconds."
        actions={
          <label className="flex flex-col gap-1.5">
            <span className="text-[13px] text-muted">Servers</span>
            <select value={server ?? ""} onChange={(event) => choose(event.target.value)} className={`${fieldClass} min-w-56`}>
              <option value="">All {servers.data ? `${formatCount(servers.data.counts.all)} servers` : "servers"}</option>
              {servers.data?.servers.map((row) => (
                <option key={row.id} value={row.id}>
                  {row.name} ({row.id})
                </option>
              ))}
            </select>
          </label>
        }
      />

      {exposure.isPending ? (
        <Loading label="Loading the exposure" />
      ) : !exposure.data ? (
        <ErrorText error={exposure.error} />
      ) : (
        <Figures data={exposure.data} />
      )}
    </div>
  );
}

function Figures({ data }: { data: Exposure }) {
  const largest = Math.max(0, ...data.symbols.map((s) => Math.abs(s.netValue)));
  return (
    <>
      <FigureGrid label="Totals" count={5}>
        <FigureCard label="Long" value={formatLarge(data.longValue)} sub={`${data.currency} on ${formatCountOf(data.longPositions, "position")}`} />
        <FigureCard label="Short" value={formatLarge(data.shortValue)} sub={`${data.currency} on ${formatCountOf(data.shortPositions, "position")}`} />
        <FigureCard
          label="Net"
          value={`${formatLarge(Math.abs(data.netValue))} ${data.netValue < 0 ? "short" : data.netValue > 0 ? "long" : ""}`.trim()}
          tone={data.netValue < 0 ? "loss" : data.netValue > 0 ? "profit" : "normal"}
          sub={data.currency}
        />
        <FigureCard
          label="Traders' open profit"
          value={formatSignedWhole(data.openProfit)}
          tone={data.openProfit < 0 ? "loss" : data.openProfit > 0 ? "profit" : "normal"}
          sub={`${data.currency} across ${formatCountOf(data.accounts, "account")}`}
        />
        <FigureCard label="Margin in use" value={formatLarge(data.margin)} sub={data.currency} />
      </FigureGrid>

      {data.unvalued > 0 && (
        <p className="text-sm text-muted">
          {formatCount(data.unvalued)} positions have no rate to {data.currency} yet and are left out of the values.
        </p>
      )}

      <SymbolsPanel data={data} largest={largest} />

      <div className="grid items-start gap-4 lg:grid-cols-[3fr_2fr]">
        <Panel title="Largest positions">
          {data.largest.length === 0 ? (
            <p className="text-muted">Nobody holds a position now.</p>
          ) : (
            <TableBox minWidth={460}>
              <thead>
                <tr className="text-right">
                  <th scope="col" className={`${thClass} text-left`}>
                    Account
                  </th>
                  <th scope="col" className={`${thClass} text-left`}>
                    Symbol
                  </th>
                  <th scope="col" className={thClass}>
                    Lots
                  </th>
                  <th scope="col" className={thClass}>
                    Value in {data.currency}
                  </th>
                  <th scope="col" className={thClass}>
                    Open profit
                  </th>
                </tr>
              </thead>
              <tbody>
                {data.largest.map((position) => (
                  <tr key={`${position.accountId}-${position.positionId}`} className="text-right">
                    <th scope="row" className={`${tdClass} text-left font-normal`}>
                      <Link
                        href={`/servers/${encodeURIComponent(position.serverId)}?account=${encodeURIComponent(position.accountId)}`}
                        className="hover:text-accent"
                      >
                        <span className="font-semibold">#{position.accountId}</span> <span className="font-mono text-xs text-muted">{position.serverId}</span>
                      </Link>
                    </th>
                    <td className={`${tdClass} text-left`}>
                      {position.symbol} <span className={position.side === "Buy" ? "text-profit" : "text-loss"}>{position.side}</span>
                    </td>
                    <td className={tdClass}>{formatLots(position.lots)}</td>
                    <td className={tdClass}>{formatLarge(position.value)}</td>
                    <td className={`${tdClass} ${position.profit < 0 ? "text-loss" : "text-profit"}`}>{formatSignedMoney(position.profit)}</td>
                  </tr>
                ))}
              </tbody>
            </TableBox>
          )}
        </Panel>

        <Panel title="By server">
          {data.servers.length === 0 ? (
            <p className="text-muted">Nobody holds a position now.</p>
          ) : (
            <ul className="flex flex-col">
              {data.servers.map((row) => (
                <li key={row.serverId} className="grid grid-cols-[minmax(0,1fr)_auto_auto] items-baseline gap-3 border-t border-border py-2 first:border-t-0 first:pt-0">
                  <Link href={`/exposure?server=${encodeURIComponent(row.serverId)}`} className="truncate font-mono text-[13px] hover:text-accent">
                    {row.serverId}
                  </Link>
                  <span className="text-muted">{formatCount(row.positions)} positions</span>
                  <span className={`min-w-24 text-right font-semibold ${row.netValue < 0 ? "text-loss" : "text-profit"}`}>
                    {row.netValue < 0 ? "-" : "+"}
                    {formatLarge(Math.abs(row.netValue))}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </Panel>
      </div>
    </>
  );
}

function SymbolsPanel({ data, largest }: { data: Exposure; largest: number }) {
  return (
    <Panel title={<>By symbol <span className="font-normal text-muted">the largest net value first</span></>}>
      {data.symbols.length === 0 ? (
        <p className="text-muted">Nobody holds a position now.</p>
      ) : (
        <TableBox minWidth={900}>
          <thead>
            <tr className="text-right">
              <th scope="col" className={`${thClass} text-left`}>
                Symbol
              </th>
              <th scope="col" className={thClass}>
                Long lots
              </th>
              <th scope="col" className={thClass}>
                Short lots
              </th>
              <th scope="col" className={thClass}>
                Net lots
              </th>
              <th scope="col" className={thClass}>
                Net value in {data.currency}
              </th>
              <th scope="col" className={thClass}>
                Accounts
              </th>
              <th scope="col" className={thClass}>
                Open profit in {data.currency}
              </th>
              <th scope="col" className={`${thClass} w-1/5`}>
                <span className="sr-only">Short or long</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {data.symbols.map((symbol) => (
              <tr key={symbol.symbol} className="text-right">
                <th scope="row" className={`${tdClass} text-left font-semibold`}>
                  {symbol.symbol}
                </th>
                <td className={tdClass}>{formatLots(symbol.longLots)}</td>
                <td className={tdClass}>{formatLots(symbol.shortLots)}</td>
                <td className={`${tdClass} font-semibold ${symbol.netLots < 0 ? "text-loss" : symbol.netLots > 0 ? "text-profit" : ""}`}>{formatSignedLots(symbol.netLots)}</td>
                <td className={tdClass}>{formatLarge(Math.abs(symbol.netValue))}</td>
                <td className={`${tdClass} text-muted`}>{formatCount(symbol.accounts)}</td>
                <td className={`${tdClass} ${symbol.openProfit < 0 ? "text-loss" : "text-profit"}`}>{formatSignedWhole(symbol.openProfit)}</td>
                <td className={tdClass}>
                  <LongShortBar value={symbol.netValue} largest={largest} />
                </td>
              </tr>
            ))}
          </tbody>
        </TableBox>
      )}
    </Panel>
  );
}
