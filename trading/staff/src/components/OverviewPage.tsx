"use client";

import Link from "next/link";

import type { EngineSummary, FeedSummary, NeedsUsItem, PlatformLogEntry, SymbolExposure } from "@/lib/api/types";
import {
  feedName,
  formatAgo,
  formatClock,
  formatCount,
  formatCountOf,
  formatDay,
  formatLarge,
  formatMilliseconds,
  formatRate,
  formatSignedLots,
  formatWhen,
} from "@/lib/format";
import { needsUsText } from "@/lib/needsUs";
import { isWarning, logText } from "@/lib/platformLog";
import { useOverview } from "@/lib/queries";
import { useNow } from "@/lib/useNow";

import { Bars, LongShortBar } from "./charts";
import { ErrorIcon, LateIcon, SuccessIcon, WarningIcon } from "./icons";
import { ErrorText, Facts, FigureCard, FigureGrid, Loading, PageHeader, Panel, Status, TableBox, tdClass, thClass } from "./ui";

/** What needs us first, then the platform in figures, the feed, the engine, what traders hold and what happened (ADR 0057). */
export function OverviewPage() {
  const overview = useOverview();
  const now = useNow();

  if (overview.isPending) {
    return <Loading label="Loading the overview" />;
  }

  if (!overview.data) {
    return <ErrorText error={overview.error} />;
  }

  const data = overview.data;
  const figures = data.figures;
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title="Overview" text={`${formatDay(now)}, ${formatClock(now)}`} />
      <NeedsUs items={data.needsUs} feed={data.feed.feed} now={now} />

      <FigureGrid label="Key figures" count={6}>
        <FigureCard label="Servers" value={formatCount(data.servers)} sub={`${formatCount(data.listedServers)} listed, ${formatCount(data.servers - data.listedServers)} not yet`} />
        <FigureCard label="Traders" value={formatCount(figures.traders)} sub={`${formatCount(figures.newTraders)} new this week`} />
        <FigureCard
          label="Accounts trading"
          value={formatCount(figures.accountsTrading)}
          sub={`${formatCount(figures.accountsPaused)} paused, ${formatCount(figures.accountsClosed)} closed`}
        />
        <FigureCard label="Open positions" value={formatCount(figures.openPositions)} sub={`On ${formatCountOf(figures.accountsWithPositions, "account")}`} />
        <FigureCard
          label="Positions opened today"
          value={formatCount(figures.positionsOpenedToday)}
          sub={`${formatCountOf(figures.refusedToday, "request")} refused, ${formatCount(figures.refusedForOldPricesToday)} for old prices`}
        />
        <FigureCard label="Terminals open" value={formatCount(figures.terminalsOpen)} sub={`Watching ${formatCountOf(figures.accountsWatched, "account")}`} />
      </FigureGrid>

      <div className="grid gap-4 lg:grid-cols-[3fr_2fr]">
        <FeedPanel feed={data.feed} now={now} />
        <EnginePanel engine={data.engine} now={now} />
      </div>

      <div className="grid gap-4 lg:grid-cols-[3fr_2fr]">
        <ExposurePanel symbols={data.topExposure} />
        <LatestPanel entries={data.latest} now={now} />
      </div>
    </div>
  );
}

/** The list of what needs us, or a calm line when nothing does. */
export function NeedsUs({ items, feed, now }: { items: NeedsUsItem[]; feed: string; now: Date }) {
  return (
    <Panel title={<>Needs us {items.length > 0 && <span className="font-normal text-muted">{items.length}</span>}</>}>
      {items.length === 0 ? (
        <p className="flex items-center gap-2.5 text-muted">
          <SuccessIcon className="size-5 text-profit" />
          Nothing needs us now.
        </p>
      ) : (
        <ul className="flex flex-col">
          {items.map((item, index) => {
            const text = needsUsText(item, feed, now);
            const Icon = text.tone === "loss" ? ErrorIcon : item.kind === "EventsNotRead" ? LateIcon : WarningIcon;
            return (
              <li key={index} className="flex flex-wrap items-start gap-x-3.5 gap-y-2 border-t border-border py-3 first:border-t-0 first:pt-0 last:pb-0">
                <Icon className={`mt-0.5 size-5 ${text.tone === "loss" ? "text-loss" : text.tone === "warning" ? "text-warning" : "text-muted"}`} />
                <div className="flex min-w-0 flex-[1_1_24rem] flex-col gap-1">
                  <strong className="font-semibold">{text.title}</strong>
                  <span className="leading-relaxed text-muted">{text.detail}</span>
                </div>
                <Link href={text.href} className="self-center font-medium text-accent hover:underline">
                  {text.linkText}
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </Panel>
  );
}

export function feedStatus(feed: Pick<FeedSummary, "lastPriceAt" | "silent" | "silentSince">): { text: string; tone: "profit" | "warning" | "loss" | "muted" } {
  if (feed.silentSince) {
    return { text: "Silent", tone: "loss" };
  }

  if (!feed.lastPriceAt) {
    return { text: "No prices yet", tone: "muted" };
  }

  return feed.silent > 0 ? { text: "Prices arriving, some silent", tone: "warning" } : { text: "Prices arriving", tone: "profit" };
}

function FeedPanel({ feed, now }: { feed: FeedSummary; now: Date }) {
  const status = feedStatus(feed);
  const dip = Math.max(...feed.pricesPerMinute) * 0.85;
  return (
    <Panel
      title={
        <>
          Price feed <span className="font-normal text-muted">{feedName(feed.feed)}</span>
        </>
      }
      aside={<Status tone={status.tone}>{status.text}</Status>}
    >
      <dl className="grid grid-cols-3 gap-3">
        <div className="flex flex-col gap-0.5">
          <dt className="text-[13px] text-muted">Last price</dt>
          <dd className="text-lg font-semibold">{feed.lastPriceAt ? formatAgo(feed.lastPriceAt, now) : "None yet"}</dd>
        </div>
        <div className="flex flex-col gap-0.5">
          <dt className="text-[13px] text-muted">Symbols</dt>
          <dd className="text-lg font-semibold">
            {formatCount(feed.live)} live{" "}
            {feed.closed > 0 && <span className="text-sm font-normal text-muted">{formatCount(feed.closed)} closed </span>}
            {feed.silent > 0 && <span className="text-sm font-normal text-warning">{formatCount(feed.silent)} silent</span>}
          </dd>
        </div>
        <div className="flex flex-col gap-0.5">
          <dt className="text-[13px] text-muted">Prices per second</dt>
          <dd className="text-lg font-semibold">{formatRate(feed.pricesPerSecond)}</dd>
        </div>
      </dl>
      <figure className="flex flex-col gap-1.5">
        <Bars
          values={feed.pricesPerMinute}
          label="Prices per minute in the last hour"
          notice={(index) => index < feed.pricesPerMinute.length - 1 && feed.pricesPerMinute[index] < dip}
        />
        <figcaption className="flex justify-between text-xs text-muted">
          <span>{formatClock(new Date(now.getTime() - 59 * 60_000))}</span>
          <span>Prices per minute, last hour</span>
          <span>{formatClock(now)}</span>
        </figcaption>
      </figure>
      <Link href="/price-feed" className="text-sm text-accent hover:underline">
        Open the price feed
      </Link>
    </Panel>
  );
}

function EnginePanel({ engine, now }: { engine: EngineSummary; now: Date }) {
  return (
    <Panel title="Engine" aside={<Status tone={engine.healthy ? "profit" : "loss"}>{engine.healthy ? "Healthy" : "Not healthy"}</Status>}>
      <Facts
        items={[
          {
            label: "Waiting in the queue",
            value: (
              <>
                {formatCount(engine.queueLength)} <span className="text-muted">({formatCount(engine.maxQueueThisHour)} at most this hour)</span>
              </>
            ),
          },
          {
            label: "Time to save",
            value: (
              <>
                {formatMilliseconds(engine.averageSaveMilliseconds)} <span className="text-muted">({formatMilliseconds(engine.slowestSaveMilliseconds)} at worst)</span>
              </>
            ),
          },
          { label: "Last input", value: <span className="font-mono text-[13px]">{formatCount(engine.lastInput)}</span> },
          {
            label: "Last snapshot",
            value: engine.lastSnapshot?.createdAt
              ? formatAgo(engine.lastSnapshot.createdAt, now)
              : engine.lastSnapshot
                ? `After input ${formatCount(engine.lastSnapshot.inputSequence)}`
                : "None",
          },
          { label: "Running since", value: engine.startedAt ? formatWhen(engine.startedAt, now) : "Starting" },
        ]}
      />
      <Link href="/engine" className="mt-auto text-sm text-accent hover:underline">
        Open the engine
      </Link>
    </Panel>
  );
}

function ExposurePanel({ symbols }: { symbols: SymbolExposure[] }) {
  const largest = Math.max(0, ...symbols.map((s) => Math.abs(s.netValue)));
  return (
    <Panel
      title="Largest net exposure"
      aside={
        <Link href="/exposure" className="text-accent hover:underline">
          All symbols
        </Link>
      }
    >
      {symbols.length === 0 ? (
        <p className="text-muted">Nobody holds a position now.</p>
      ) : (
        <TableBox minWidth={480}>
          <thead>
            <tr className="text-right">
              <th scope="col" className={`${thClass} text-left`}>
                Symbol
              </th>
              <th scope="col" className={thClass}>
                Net lots
              </th>
              <th scope="col" className={thClass}>
                Value in USD
              </th>
              <th scope="col" className={thClass}>
                Accounts
              </th>
              <th scope="col" className={`${thClass} w-1/3`}>
                <span className="sr-only">Long or short</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {symbols.map((symbol) => (
              <tr key={symbol.symbol} className="text-right">
                <th scope="row" className={`${tdClass} text-left font-semibold`}>
                  {symbol.symbol}
                </th>
                <td className={`${tdClass} ${symbol.netLots < 0 ? "text-loss" : "text-profit"}`}>{formatSignedLots(symbol.netLots)}</td>
                <td className={tdClass}>{formatLarge(Math.abs(symbol.netValue))}</td>
                <td className={`${tdClass} text-muted`}>{formatCount(symbol.accounts)}</td>
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

/** What happened on the platform lately, newest first. */
export function LatestPanel({ entries, now, title = "Latest on the platform" }: { entries: PlatformLogEntry[]; now: Date; title?: string }) {
  return (
    <Panel title={title}>
      {entries.length === 0 ? (
        <p className="text-muted">Nothing yet.</p>
      ) : (
        <ul className="flex flex-col">
          {entries.map((entry) => (
            <li key={entry.id} className="grid grid-cols-[4.5rem_minmax(0,1fr)] gap-3 border-t border-border py-2 first:border-t-0 first:pt-0">
              <span className="pt-0.5 font-mono text-xs text-muted">{formatWhen(entry.at, now)}</span>
              <span className={`leading-snug ${isWarning(entry) ? "text-warning" : ""}`}>{logText(entry, now)}</span>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}
