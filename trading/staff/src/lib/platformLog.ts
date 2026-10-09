import type { PlatformLogEntry } from "./api/types";
import { feedName, formatClock, formatCount, formatDuration, formatWhen } from "./format";

const by = (entry: PlatformLogEntry) => entry.detail.partner ?? entry.staffEmail ?? "the configuration";

const seconds = (value: string | undefined) => formatDuration(Number(value ?? 0));

const startTime = (milliseconds: number) => (milliseconds < 60_000 ? `${(milliseconds / 1000).toFixed(1)} s` : formatDuration(milliseconds / 1000));

const gap = (entry: PlatformLogEntry, now: Date) =>
  entry.detail.from && entry.detail.until ? `from ${formatWhen(entry.detail.from, now)} to ${formatClock(entry.detail.until)}` : "";

/** One entry of the platform's log as a sentence, for example "Kronant Prop made the server fjord-traders, Fjord Traders in EUR". */
export function logText(entry: PlatformLogEntry, now: Date = new Date()): string {
  const d = entry.detail;
  const server = entry.serverId ?? "a server";
  switch (entry.kind) {
    case "ServiceStarted":
      return `The service started in ${startTime(Number(d.milliseconds ?? 0))}, replaying ${formatCount(Number(d.replayed ?? 0))} inputs after the snapshot`;
    case "ServerCreated":
      return `${by(entry)} made the server ${server}, ${d.name ?? server}${d.currency ? ` in ${d.currency}` : ""}`;
    case "ServerListed":
      return `${server} is listed, by ${by(entry)}`;
    case "ServerUnlisted":
      return `${server} was taken off the list, by ${by(entry)}`;
    case "AdminKeyReplaced":
      return d.partner ? `New admin key for ${server}, asked for by ${d.partner}` : `${by(entry)} stopped the admin key of ${server}${d.reason ? `: ${d.reason}` : ""}`;
    case "SymbolSilent":
      return `${d.symbol} went silent on ${feedName(d.feed ?? "")} while its market is open`;
    case "SymbolBack":
      return `${d.symbol} has prices again after ${seconds(d.silentSeconds)}`;
    case "FeedSilent":
      return `${feedName(d.feed ?? "")} stopped giving prices while markets are open`;
    case "FeedBack":
      return `${feedName(d.feed ?? "")} gives prices again after ${seconds(d.silentSeconds)}`;
    case "ChartGapFilled":
      return `Chart gap ${gap(entry, now)} filled with ${formatCount(Number(d.bars ?? 0))} bars`;
    case "ChartGapNotFilled":
      return `Chart gap ${gap(entry, now)} could not be filled`;
    case "ChartHistoryReloaded":
      return d.problem
        ? `Loading the chart history again failed: ${d.problem}`
        : `${by(entry)} loaded the chart history again, ${formatCount(Number(d.bars ?? 0))} bars`;
  }
}

/** Whether the entry is bad news, to show it in the warning color. */
export function isWarning(entry: PlatformLogEntry): boolean {
  return entry.kind === "SymbolSilent" || entry.kind === "FeedSilent" || entry.kind === "ChartGapNotFilled" || (entry.kind === "ChartHistoryReloaded" && !!entry.detail.problem);
}
