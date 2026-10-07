import type { FirmIncident, IncidentAccount, IncidentDecision, IncidentKind, IncidentStatus, ServicePart, StatusIncident } from "./api/types";
import { formatMoney } from "./format";

// Incidents (ADR 0053): outages of the trading platform that our staff publish, what they did to a firm's accounts and
// what the firm did about it, and the firm's status page.

export const kindLabels: Record<IncidentKind, string> = {
  PriceFeedOutage: "Price feed outage",
  PlatformDown: "Trading platform down",
  SlowPrices: "Slow prices",
  Other: "Something else",
};

export const incidentKinds = Object.keys(kindLabels) as IncidentKind[];

export const statusLabels: Record<IncidentStatus, string> = { Draft: "Draft", Open: "Ongoing", Resolved: "Resolved" };

/** The tone a badge for the status has. */
export const statusTones: Record<IncidentStatus, "muted" | "warning" | "profit"> = { Draft: "muted", Open: "warning", Resolved: "profit" };

/** The parts of the service a status page shows, and what each is. */
export const partLabels: Record<ServicePart, { name: string; about: string }> = {
  Trading: { name: "Trading", about: "Orders, closes, stops and loss limits" },
  Prices: { name: "Prices", about: "Prices for every symbol" },
  Terminal: { name: "Terminal", about: "The trading terminal in the browser" },
  Portal: { name: "Portal", about: "Accounts, payouts and support" },
};

/** How long something lasted, in words, for example "73 minutes", "2 hours 5 minutes" or "under a minute". */
export function durationText(fromIso: string, toIso: string): string {
  const minutes = Math.floor((Date.parse(toIso) - Date.parse(fromIso)) / 60_000);
  if (minutes < 1) {
    return "under a minute";
  }

  const plural = (n: number, unit: string) => `${n} ${unit}${n === 1 ? "" : "s"}`;
  if (minutes < 120) {
    return plural(minutes, "minute");
  }

  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest === 0 ? plural(hours, "hour") : `${plural(hours, "hour")} ${plural(rest, "minute")}`;
}

function dayAndTime(iso: string, timeZone: string | undefined, seconds: boolean): { day: string; time: string } {
  const date = new Date(iso);
  return {
    day: date.toLocaleDateString("en-GB", { weekday: "short", day: "numeric", month: "short", timeZone }),
    time: date.toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit", second: seconds ? "2-digit" : undefined, timeZone }),
  };
}

/**
 * When an incident was, for example "Tue 6 Oct, 15:12 to 16:25", "Tue 6 Oct, 23:40 to Wed 7 Oct, 00:10" or
 * "Since Tue 6 Oct, 15:12" while it goes on.
 */
export function periodText(startedAt: string, endedAt: string | null, timeZone?: string, seconds = false): string {
  const start = dayAndTime(startedAt, timeZone, seconds);
  if (endedAt === null) {
    return `Since ${start.day}, ${start.time}`;
  }

  const end = dayAndTime(endedAt, timeZone, seconds);
  return end.day === start.day ? `${start.day}, ${start.time} to ${end.time}` : `${start.day}, ${start.time} to ${end.day}, ${end.time}`;
}

/** A moment as a plain date in the time zone, for example "2026-10-06". */
export function dateIn(moment: Date, timeZone?: string): string {
  return moment.toLocaleDateString("en-CA", { year: "numeric", month: "2-digit", day: "2-digit", timeZone });
}

export type StatusDay = { date: string; incidents: string[] };

/**
 * The days up to today, oldest first, each with the titles of the incidents that reached the part on it. An incident
 * that goes on reaches every day up to today.
 */
export function statusDays(incidents: readonly StatusIncident[], part: ServicePart, now: Date, timeZone?: string, count = 30): StatusDay[] {
  const days: StatusDay[] = [];
  for (let back = count - 1; back >= 0; back--) {
    days.push({ date: dateIn(new Date(now.getTime() - back * 86_400_000), timeZone), incidents: [] });
  }

  for (const incident of incidents.filter((i) => i.parts.includes(part))) {
    const first = dateIn(new Date(incident.startedAt), timeZone);
    const last = dateIn(incident.endedAt === null ? now : new Date(incident.endedAt), timeZone);
    for (const day of days) {
      if (day.date >= first && day.date <= last) {
        day.incidents.push(incident.title);
      }
    }
  }

  return days;
}

export type Tone = "loss" | "warning" | "default" | "muted";

/** What the incident did to an account, in words, with how serious it was. */
export function whatHappened(account: IncidentAccount): { text: string; tone: Tone } {
  const refused = refusalText(account);
  if (account.breach) {
    const limit = account.breach.floorId === "daily" ? "daily loss limit" : account.breach.floorId === "max-loss" ? "max loss limit" : "a loss limit";
    return { text: `Broke the ${limit} at equity ${formatMoney(account.breach.equity)}.${refused ? ` ${refused}` : ""}`, tone: "loss" };
  }

  if (refused) {
    return { text: refused, tone: "warning" };
  }

  return { text: account.openPositions > 0 ? "Had positions open. Nothing was refused." : "Nothing was refused.", tone: "muted" };
}

function refusalText(account: IncidentAccount): string | null {
  const parts = [
    account.ordersRefused > 0 ? `${account.ordersRefused} ${account.ordersRefused === 1 ? "order" : "orders"}` : null,
    account.closesRefused > 0 ? `${account.closesRefused} ${account.closesRefused === 1 ? "close" : "closes"}` : null,
    account.changesRefused > 0 ? `${account.changesRefused} stop ${account.changesRefused === 1 ? "change" : "changes"}` : null,
  ].filter((p): p is string => p !== null);
  if (parts.length === 0) {
    return null;
  }

  const list = parts.length === 1 ? parts[0] : `${parts.slice(0, -1).join(", ")} and ${parts[parts.length - 1]}`;
  return `${list} refused while prices were missing.`;
}

/** Whether the firm still has a decision to make about the account: a stage to reinstate, or refused requests to look at. */
export function needsDecision(account: IncidentAccount, decisions: readonly IncidentDecision[]): boolean {
  if (decisions.some((d) => d.accountId === account.accountId)) {
    return false;
  }

  return account.canReinstate || (account.canCredit && account.ordersRefused + account.closesRefused + account.changesRefused > 0);
}

export type AccountFilter = "needs" | "refused" | "all";

/** The accounts the filter shows. */
export function filterAccounts(incident: FirmIncident, filter: AccountFilter): IncidentAccount[] {
  switch (filter) {
    case "needs":
      return incident.accounts.filter((a) => needsDecision(a, incident.decisions));
    case "refused":
      return incident.accounts.filter((a) => a.ordersRefused + a.closesRefused + a.changesRefused > 0);
    default:
      return incident.accounts;
  }
}

/** The figures over the accounts: how many had positions open, broke a limit, had requests refused, and were reinstated. */
export function incidentSummary(incident: FirmIncident) {
  const accounts = incident.accounts;
  const breached = new Set(accounts.filter((a) => a.breach !== null).map((a) => a.accountId));
  return {
    withPositions: accounts.filter((a) => a.openPositions > 0).length,
    breached: breached.size,
    withRefusals: accounts.filter((a) => a.ordersRefused + a.closesRefused + a.changesRefused > 0).length,
    reinstated: new Set(incident.decisions.filter((d) => d.kind === "Reinstated").map((d) => d.accountId)).size,
  };
}

export type BalanceChoice = { key: "equity" | "balance"; label: string; amount: number; note: string };

/**
 * What a reinstated stage can start with: the equity when the incident began, as if the open positions had closed at the
 * last prices before it, and the balance then, as if they had never been opened. Without a known equity, only the
 * balance.
 */
export function balanceChoices(account: IncidentAccount): BalanceChoice[] {
  const choices: BalanceChoice[] = [];
  if (account.equityAtStart !== null) {
    choices.push({
      key: "equity",
      label: "Equity when the incident began",
      amount: account.equityAtStart,
      note: "As if the open positions had closed at the last prices before it.",
    });
  }

  choices.push({
    key: "balance",
    label: "Balance when the incident began",
    amount: account.balanceAtStart,
    note: account.openPositions > 0 ? "As if the positions open then had never been opened." : "What the account had before it.",
  });
  return choices;
}

/** What the firm did for an account, for example "Reinstated #1042 with 98,212.50" or "Credited #1088 with 120.00". */
export function decisionText(decision: IncidentDecision): string {
  const verb = decision.kind === "Reinstated" ? "Reinstated" : "Credited";
  return `${verb} #${decision.number} with ${formatMoney(decision.amount)}`;
}

/** A moment as a datetime-local field shows it, in the browser's time zone, to the second, for example "2026-10-06T15:12:04". */
export function toLocalInput(iso: string): string {
  const date = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

/** What a datetime-local field holds, in the browser's time zone, as a moment, or null when it is empty or not a time. */
export function fromLocalInput(value: string): string | null {
  if (value.trim() === "") {
    return null;
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** How long ago a moment was, roughly, for example "4 min ago" or "just now". */
export function agoText(iso: string, now: number): string {
  const seconds = Math.max(0, Math.round((now - Date.parse(iso)) / 1000));
  if (seconds < 10) {
    return "just now";
  }

  if (seconds < 60) {
    return `${seconds} s ago`;
  }

  const minutes = Math.round(seconds / 60);
  return minutes < 120 ? `${minutes} min ago` : `${Math.round(minutes / 60)} h ago`;
}
