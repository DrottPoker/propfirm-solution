"use client";

import { toast } from "sonner";

import type { ChartGap, PriceFeed, SymbolFeed } from "@/lib/api/types";
import { feedInText, feedName, formatAgo, formatClock, formatCount, formatDate, formatPrice, formatRate, formatWhen } from "@/lib/format";
import { usePriceFeed, useReloadHistory, useRetryGap } from "@/lib/queries";
import { useNow } from "@/lib/useNow";

import { Bars } from "./charts";
import { RetryIcon } from "./icons";
import { feedStatus } from "./OverviewPage";
import { ErrorText, Facts, Loading, PageHeader, Panel, secondaryButtonClass, Status, TableBox, tdClass, thClass } from "./ui";

/** Every symbol's raw price, the gaps in the charts and the charts' history (ADR 0057). */
export function PriceFeedPage() {
  const feed = usePriceFeed();
  const now = useNow();

  if (feed.isPending) {
    return <Loading label="Loading the price feed" />;
  }

  if (!feed.data) {
    return <ErrorText error={feed.error} />;
  }

  const data = feed.data;
  const silent = data.symbols.filter((s) => s.state === "Silent").length;
  const status = feedStatus({ lastPriceAt: data.lastPriceAt, silent, silentSince: data.silentSince });
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Price feed"
        text={`Where every price on the platform comes from. The engine refuses orders, closes and stop changes on a symbol whose price is older than ${formatCount(data.maxPriceAgeSeconds)} seconds.`}
      />

      <section aria-label="The feed now" className="grid grid-cols-2 gap-4 rounded-2xl sm:grid-cols-3 xl:grid-cols-6 border border-border bg-panel px-5 py-4 shadow-card">
        <Figure label="Provider" value={feedName(data.feed)} />
        <Figure label="Now" value={<Status tone={status.tone}>{status.text}</Status>} />
        <Figure label="Last price" value={data.lastPriceAt ? formatAgo(data.lastPriceAt, now) : "None yet"} />
        <Figure label="Prices per second" value={formatRate(data.pricesPerSecond)} />
        <Figure label="Silent while open" value={silent === 0 ? "None" : <span className="text-warning">{silent === 1 ? "1 symbol" : `${formatCount(silent)} symbols`}</span>} />
        <Figure label={data.silentSince ? "Silent since" : "Prices since"} value={data.silentSince ? formatWhen(data.silentSince, now) : data.liveSince ? formatWhen(data.liveSince, now) : "-"} />
      </section>

      {!data.followsTradingHours && (
        <p className="rounded-xl border border-border bg-panel px-4 py-3 leading-relaxed text-muted">
          With {feedInText(data.feed)}, every market is always open, whatever the exchanges&apos; hours say.
        </p>
      )}

      <Panel title="Prices per minute" aside="The last hour">
        <Bars values={data.pricesPerMinute} label="Prices per minute in the last hour" height={60} />
      </Panel>

      <Panel title="Every symbol" aside="Raw prices from the feed, before any firm's markup">
        <TableBox minWidth={900}>
          <thead>
            <tr className="text-right">
              <th scope="col" className={`${thClass} text-left`}>
                Symbol
              </th>
              <th scope="col" className={thClass}>
                Bid
              </th>
              <th scope="col" className={thClass}>
                Ask
              </th>
              <th scope="col" className={thClass}>
                Last price
              </th>
              <th scope="col" className={thClass}>
                Prices last minute
              </th>
              <th scope="col" className={`${thClass} text-left`}>
                Market
              </th>
              <th scope="col" className={`${thClass} text-left`}>
                State
              </th>
            </tr>
          </thead>
          <tbody>
            {data.symbols.map((symbol) => (
              <SymbolRow key={symbol.symbol} symbol={symbol} now={now} />
            ))}
          </tbody>
        </TableBox>
      </Panel>

      <div className="grid items-start gap-4 lg:grid-cols-[3fr_2fr]">
        <GapsPanel feed={data} now={now} />
        <HistoryPanel feed={data} now={now} />
      </div>
    </div>
  );
}

function Figure({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className="text-[13px] text-muted">{label}</span>
      <span className="text-lg font-semibold">{value}</span>
    </div>
  );
}

const stateText: Record<SymbolFeed["state"], { text: string; tone: "profit" | "warning" | "muted" }> = {
  Live: { text: "Live", tone: "profit" },
  Silent: { text: "Silent", tone: "warning" },
  Closed: { text: "Closed", tone: "muted" },
  Waiting: { text: "Waiting", tone: "muted" },
};

function SymbolRow({ symbol, now }: { symbol: SymbolFeed; now: Date }) {
  const state = stateText[symbol.state];
  const market = symbol.alwaysOpen
    ? "Always open"
    : symbol.marketOpen
      ? symbol.nextChange
        ? `Open, closes ${formatWhen(symbol.nextChange, now)}`
        : "Open"
      : symbol.nextChange
        ? `Closed, opens ${formatWhen(symbol.nextChange, now)}`
        : "Closed for now";
  return (
    <tr className={`text-right ${symbol.state === "Silent" ? "bg-warning/8" : ""}`}>
      <th scope="row" className={`${tdClass} text-left font-normal`}>
        <span className="font-semibold">{symbol.symbol}</span> <span className="text-xs text-muted">{symbol.category}</span>
      </th>
      <td className={`${tdClass} font-mono text-[13px]`}>{formatPrice(symbol.bid, symbol.digits)}</td>
      <td className={`${tdClass} font-mono text-[13px]`}>{formatPrice(symbol.ask, symbol.digits)}</td>
      <td className={`${tdClass} ${symbol.state === "Silent" ? "text-warning" : ""}`}>{symbol.lastPriceAt ? formatAgo(symbol.lastPriceAt, now) : "-"}</td>
      <td className={`${tdClass} text-muted`}>{formatCount(symbol.pricesLastMinute)}</td>
      <td className={`${tdClass} text-left text-muted`}>{market}</td>
      <td className={`${tdClass} text-left`}>
        <Status tone={state.tone}>{state.text}</Status>
      </td>
    </tr>
  );
}

const gapState: Record<ChartGap["state"], { text: string; tone: "profit" | "warning" | "muted" | "accent" }> = {
  Filling: { text: "Filling", tone: "accent" },
  Filled: { text: "Filled", tone: "profit" },
  Empty: { text: "No prices in it", tone: "muted" },
  NotFilled: { text: "Not filled", tone: "warning" },
};

function GapsPanel({ feed, now }: { feed: PriceFeed; now: Date }) {
  const retry = useRetryGap();
  return (
    <Panel title="Gaps in the charts">
      <p className="leading-relaxed text-muted">
        A gap is a whole minute without any price, after a stop or a silent feed. It is filled from the feed&apos;s history in the background while trading goes on.
      </p>
      {!feed.hasHistory ? (
        <p className="text-muted">Made-up prices have no history, so their gaps stay.</p>
      ) : feed.gaps.length === 0 ? (
        <p className="text-muted">No gaps yet.</p>
      ) : (
        <ul className="flex flex-col">
          {feed.gaps.map((gap) => {
            const state = gapState[gap.state];
            const canRetry = gap.state === "NotFilled" || gap.state === "Empty";
            return (
              <li key={gap.id} className="flex flex-wrap items-start gap-x-4 gap-y-2 border-t border-border py-3 first:border-t-0">
                <div className="flex min-w-0 flex-[1_1_20rem] flex-col gap-1">
                  <strong className="font-semibold">
                    {formatWhen(gap.from, now)} to {formatClock(gap.until)} <Status tone={state.tone}>{state.text}</Status>
                  </strong>
                  <span className="leading-relaxed text-muted">{gapDetail(gap, feed.feed, now)}</span>
                </div>
                {canRetry && (
                  <button
                    type="button"
                    disabled={retry.isPending}
                    onClick={() =>
                      retry.mutate(gap.id, {
                        onSuccess: () => toast("Asked the feed again", { description: "The charts show the gap's bars once they come." }),
                        onError: (error) => toast.error("Could not try again", { description: error.message }),
                      })
                    }
                    className={secondaryButtonClass}
                  >
                    <RetryIcon />
                    Try again
                  </button>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </Panel>
  );
}

function gapDetail(gap: ChartGap, feed: string, now: Date): string {
  const tries = `${gap.tries} ${gap.tries === 1 ? "try" : "tries"}`;
  switch (gap.state) {
    case "Filling":
      return `Asking ${feedName(feed)} for it, ${tries} so far.${gap.problem ? ` Last answer: ${gap.problem}` : ""}`;
    case "Filled":
      return `Filled with ${formatCount(gap.bars)} bars${gap.finishedAt ? ` at ${formatWhen(gap.finishedAt, now)}` : ""}, after ${tries}.`;
    case "Empty":
      return `${feedName(feed)} had no prices for it, for example since the markets were closed.`;
    case "NotFilled":
      return `${feedName(feed)} could not be asked after ${tries}${gap.finishedAt ? `, the last at ${formatClock(gap.finishedAt)}` : ""}: ${gap.problem ?? "no answer"}. The gap stays in the charts.`;
  }
}

function HistoryPanel({ feed, now }: { feed: PriceFeed; now: Date }) {
  const reload = useReloadHistory();
  const history = feed.history;
  return (
    <Panel title="Chart history" aside={history.reloading ? <Status tone="accent">Loading again</Status> : undefined}>
      <Facts
        items={[
          { label: "Loaded", value: history.loadedAt ? formatWhen(history.loadedAt, now) : "Not loaded" },
          { label: "Reaches back", value: history.reach ? `To ${formatDate(history.reach)}, ${formatCount(history.dayDays)} days in day bars` : `${formatCount(history.dayDays)} days` },
          {
            label: "In more detail",
            value: `${formatCount(history.historyDays)} days in hours, ${formatCount(history.quarterHourDays)} days in 15 minutes, ${formatCount(history.minuteDays)} days in minutes`,
          },
        ]}
      />
      {feed.hasHistory && (
        <>
          <p className="text-sm leading-relaxed text-muted">
            Loading it again replaces this feed&apos;s bars with the provider&apos;s, which also closes gaps that could not be filled. Charts in open terminals reload when it is done.
          </p>
          <button
            type="button"
            disabled={reload.isPending || history.reloading}
            onClick={() =>
              reload.mutate(undefined, {
                onSuccess: () => toast("Loading the history again", { description: "This takes about a minute." }),
                onError: (error) => toast.error("Could not load the history again", { description: error.message }),
              })
            }
            className={`${secondaryButtonClass} self-start`}
          >
            <RetryIcon />
            Load the history again
          </button>
        </>
      )}
    </Panel>
  );
}
