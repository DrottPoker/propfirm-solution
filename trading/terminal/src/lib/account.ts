import type { AccountDetails, AccountSnapshot, EngineEvent, FloorSnapshot, ServerInfo } from "./api/types";
import { formatAt, formatMoney, timeZoneName } from "./format";

// Floors the prop platform sets, with the names its portal uses for them.
const floorNames: Record<string, string> = { daily: "Daily loss limit", "max-loss": "Max loss limit" };

/** A floor's name for the trader, as in the firm's portal: "daily" is the "Daily loss limit", and "min_equity" the "Min equity limit". */
export function floorLabel(floorId: string): string {
  const known = floorNames[floorId];
  if (known) {
    return known;
  }

  const words = floorId.replace(/[-_]+/g, " ").trim();
  return words.length > 0 ? `${words[0].toUpperCase()}${words.slice(1)} limit` : "Loss limit";
}

export type FloorRisk = "ok" | "warning" | "danger";

/**
 * How close equity is to the floor, measured against the floor's distance. Floors at a fixed level have no
 * distance and are never marked, until equity has reached them.
 */
export function floorRisk(floor: FloorSnapshot): FloorRisk {
  if (floor.headroom <= 0) {
    return "danger";
  }

  const distance = "distance" in floor.rule ? floor.rule.distance : null;
  if (!distance || distance <= 0) {
    return "ok";
  }

  const left = floor.headroom / distance;
  return left < 0.1 ? "danger" : left < 0.25 ? "warning" : "ok";
}

/** What is left before the floor, never a negative amount: a floor equity has reached is broken. */
export function floorLeftText(floor: FloorSnapshot): string {
  return floor.headroom > 0 ? `${formatMoney(floor.headroom)} left` : "Broken";
}

/**
 * Two letters for the trader's avatar: from the name the firm told when there is one, "Anna Berg" becomes AB, and
 * otherwise from the email address, anna.berg@example.com becomes AB too.
 */
export function initials(email: string, name: string | null = null): string {
  const words = name?.trim().split(/\s+/).filter((w) => w.length > 0) ?? [];
  if (words.length > 0) {
    const letters = words.length > 1 ? `${words[0][0]}${words.at(-1)?.[0] ?? ""}` : words[0].slice(0, 2);
    return letters.toUpperCase();
  }

  const local = email.split("@")[0] ?? "";
  const parts = local.split(/[._+-]+/).filter((p) => p.length > 0);
  const letters = parts.length > 1 ? `${parts[0][0]}${parts[1][0]}` : local.slice(0, 2);
  return letters.toUpperCase() || "?";
}

/**
 * What the account has made or lost today: equity now against the balance the trading day started with, so open
 * positions count, and as a share of that balance.
 */
export function todayResult(account: Pick<AccountSnapshot, "equity" | "ownLimits">): { amount: number; percent: number | null } {
  const start = account.ownLimits.dayStartBalance;
  const amount = Math.round((account.equity - start) * 100) / 100;
  return { amount, percent: start > 0 ? (amount / start) * 100 : null };
}

/** The limit the account is nearest to breaking, the firm's or the trader's own, and how much room is left to it. */
export interface Room {
  /** For example "Left today" or "Left to max loss". */
  name: string;
  /** What the limit is, for the explanation. */
  limit: string;
  left: number;
  /** The share of the room left, from 1 at the start to 0 at the limit, or null when the limit has no set distance. */
  share: number | null;
  risk: FloorRisk;
  own: boolean;
}

export function nearestRoom(account: Pick<AccountSnapshot, "floors" | "ownLimits" | "equity">): Room | null {
  const rooms: Room[] = account.floors
    .filter((f) => f.headroom > 0)
    .map((f) => {
      const distance = "distance" in f.rule ? f.rule.distance : null;
      return {
        name: f.floorId === "daily" ? "Left today" : `Left to ${floorLabel(f.floorId).toLowerCase().replace(/ limit$/, "")}`,
        limit: floorLabel(f.floorId),
        left: f.headroom,
        share: distance && distance > 0 ? Math.min(1, f.headroom / distance) : null,
        risk: floorRisk(f),
        own: false,
      };
    });

  const own = account.ownLimits;
  if (own.limits.dailyLoss !== null && own.lossLevel !== null) {
    const left = Math.max(0, account.equity - own.lossLevel);
    const share = Math.min(1, left / own.limits.dailyLoss);
    if (left > 0) {
      rooms.push({ name: "Left today", limit: "Your own daily loss limit", left, share, risk: share < 0.1 ? "danger" : share < 0.25 ? "warning" : "ok", own: true });
    }
  }

  return rooms.reduce<Room | null>((nearest, room) => (!nearest || room.left < nearest.left ? room : nearest), null);
}

/** The account's state in a word for the account menu: trading, locked for the day by the trader's own limit, paused or ended. */
export function statusWord(status: AccountSnapshot["status"], locked: boolean): { word: string; tone: "muted" | "accent" | "warning" | "loss" } {
  if (status === "Disabled") {
    return { word: "Ended", tone: "loss" };
  }

  if (status === "Suspended") {
    return { word: "Paused", tone: "warning" };
  }

  return locked ? { word: "Locked today", tone: "accent" } : { word: "Active", tone: "muted" };
}

/** What a suspended account can and cannot do, for the trader. */
export const suspendedHelp =
  "Trading is paused on this account, so new orders are not taken. You can still close positions and change their stops.";

/** The account's name for the trader, as the firm's portal names it, or its id when the firm gave none. */
export function accountName(accountId: string, details: AccountDetails | undefined): string {
  return details?.label ?? accountId;
}

/**
 * Where the profit target stands. The target is reached on the balance once no position is open, so the amount to
 * go is measured on the balance too.
 */
export function targetText(target: number, account: Pick<AccountSnapshot, "balance" | "positions">): string {
  if (account.balance < target) {
    return `${formatMoney(target - account.balance)} to go`;
  }

  return account.positions.length > 0 ? "Reached, close positions" : "Reached";
}

/** Where "Back to the firm" goes: the account in the firm's portal, or the portal itself. */
export function backLink(details: AccountDetails | undefined, server: ServerInfo): string | null {
  return details?.detailsUrl ?? server.loginUrl ?? null;
}

type Breach = Extract<EngineEvent, { kind?: "EquityFloorBreached" }>;
type Disabled = Extract<EngineEvent, { kind?: "AccountDisabled" }>;

/** Why trading on a disabled account ended, in a sentence, from its latest events. */
export function endedText(events: readonly EngineEvent[], timeZone: string, now: Date): string {
  const breach = events.findLast((e): e is Breach => e.kind === "EquityFloorBreached");
  const disabled = events.findLast((e): e is Disabled => e.kind === "AccountDisabled");
  if (disabled?.reason === "Closed") {
    return "The firm closed the account.";
  }

  if (breach) {
    return `The ${floorLabel(breach.floorId).toLowerCase()} was broken ${formatAt(breach.timestamp, timeZone, now)} ${timeZoneName(timeZone)}: equity ${formatMoney(breach.equity)} fell below ${formatMoney(breach.level)}. Every position was closed at those prices.`;
  }

  return "A loss limit was broken, so every position was closed.";
}
