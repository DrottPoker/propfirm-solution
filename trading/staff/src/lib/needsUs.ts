import type { NeedsUsItem } from "./api/types";
import { feedName, formatClock, formatCount, formatDuration, formatMilliseconds, formatWhen } from "./format";

/** Something that needs us, in words: what, the detail, where to look, and how bad it is. */
export type NeedsUsText = {
  title: string;
  detail: string;
  href: string;
  linkText: string;
  tone: "warning" | "loss" | "muted";
};

const since = (at: string | null, now: Date) => (at === null ? null : (now.getTime() - new Date(at).getTime()) / 1000);

// "1 account holds" or "38 accounts hold".
const accounts = (count: number | null, one: string, many: string) => (count === 1 ? `1 account ${one}` : `${formatCount(count ?? 0)} accounts ${many}`);

/** The sentences for something that needs us, as the overview and the phone show it. */
export function needsUsText(item: NeedsUsItem, feed: string, now: Date = new Date()): NeedsUsText {
  const seconds = since(item.since, now);
  switch (item.kind) {
    case "FeedSilent":
      return {
        title: `No prices from ${feedName(feed)} for ${formatDuration(seconds ?? 0)} while markets are open`,
        detail: `${item.since ? `Last price ${formatClock(item.since)}. ` : ""}${accounts(item.count, "has", "have")} open positions, and their orders, closes and stop changes are refused until prices return.`,
        href: "/price-feed",
        linkText: "Open the price feed",
        tone: "loss",
      };
    case "SymbolSilent":
      return {
        title:
          seconds === null
            ? `${item.symbol} has had no price since the service started, while its market is open`
            : `${item.symbol} has had no price for ${formatDuration(seconds)} while its market is open`,
        detail: `${feedName(feed)}${item.since ? `, last price ${formatWhen(item.since, now)}` : ""}. ${
          (item.count ?? 0) === 0
            ? `No account holds ${item.symbol} now.`
            : `${accounts(item.count, "holds", "hold")} ${item.symbol}, and their orders, closes and stop changes on it are refused until prices return.`
        }`,
        href: "/price-feed",
        linkText: "Open the price feed",
        tone: "warning",
      };
    case "EventsNotRead":
      return {
        title: `${item.serverId} has not read its events for ${formatDuration(seconds ?? 0)}`,
        detail: `${(item.count ?? 0) > 1000 ? "More than 1,000" : formatCount(item.count ?? 0)} events are waiting. Trading and the loss limits in the engine go on, but the firm's system learns nothing new until it reads again.`,
        href: `/servers/${encodeURIComponent(item.serverId ?? "")}`,
        linkText: "Open the server",
        tone: "warning",
      };
    case "ChartGapNotFilled": {
      const gap = item.gap;
      return {
        title: gap ? `A chart gap from ${formatWhen(gap.from, now)} to ${formatClock(gap.until)} could not be filled` : "A chart gap could not be filled",
        detail: gap
          ? `${feedName(feed)} could not be asked after ${gap.tries} ${gap.tries === 1 ? "try" : "tries"}${gap.finishedAt ? `, the last at ${formatClock(gap.finishedAt)}` : ""}. The gap stays in the charts until it is tried again or the history is loaded again.`
          : "",
        href: "/price-feed",
        linkText: "See the gaps",
        tone: "muted",
      };
    }
    case "QueueBehind":
      return {
        title: `${formatCount(item.count ?? 0)} inputs are waiting for the engine`,
        detail: "Prices and orders are applied late. Look at how long saving the journal takes, and at the database.",
        href: "/engine",
        linkText: "Open the engine",
        tone: "loss",
      };
    case "SlowSaves":
      return {
        title: `Saving the journal took ${formatMilliseconds(item.count ?? 0)} at worst in the last 5 minutes`,
        detail: "Every order waits for its save, so traders feel it. Look at the database.",
        href: "/engine",
        linkText: "Open the engine",
        tone: "warning",
      };
  }
}
