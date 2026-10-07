import type { AccountDetails, AccountSnapshot, EngineEvent, FloorSnapshot, ServerInfo } from "./api/types";
import { formatMoney, formatTime, timeZoneName } from "./format";

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

/** Two letters for the trader's avatar, from the email address: anna.berg@example.com becomes AB. */
export function initials(email: string): string {
  const name = email.split("@")[0] ?? "";
  const parts = name.split(/[._+-]+/).filter((p) => p.length > 0);
  const letters = parts.length > 1 ? `${parts[0][0]}${parts[1][0]}` : name.slice(0, 2);
  return letters.toUpperCase() || "?";
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
export function endedText(events: readonly EngineEvent[], timeZone: string): string {
  const breach = events.findLast((e): e is Breach => e.kind === "EquityFloorBreached");
  const disabled = events.findLast((e): e is Disabled => e.kind === "AccountDisabled");
  if (disabled?.reason === "Closed") {
    return "The firm closed the account.";
  }

  if (breach) {
    return `The ${floorLabel(breach.floorId).toLowerCase()} was broken at ${formatTime(breach.timestamp, timeZone)} ${timeZoneName(timeZone)}: equity ${formatMoney(breach.equity)} fell below ${formatMoney(breach.level)}. Every position was closed at those prices.`;
  }

  return "A loss limit was broken, so every position was closed.";
}
