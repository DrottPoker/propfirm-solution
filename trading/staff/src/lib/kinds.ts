import type { StaffTerminal, TerminalKind } from "./api/types";

// The types of business a server can be (ADR 0058), as the staff panel names and explains them. The terminal's words
// and the parts it shows follow from the type.

export const kinds: readonly TerminalKind[] = ["Prop", "Broker", "Practice", "Desk"];

export const kindNames: Record<TerminalKind, string> = { Prop: "Prop firm", Broker: "Broker", Practice: "Practice", Desk: "Trading desk" };

/** What the type means for the firm's traders, in a sentence or two. */
export const kindDescriptions: Record<TerminalKind, string> = {
  Prop: "Challenges with the firm's rules in the terminal, breach reports and payouts. Orders go at the first click.",
  Broker: "Clients' own money. Every order asks first, traders can set limits of their own, and nothing speaks of challenges.",
  Practice: "Demo trading for schools, courses and competitions. No rules or limits, only the trading.",
  Desk: "The firm's own traders, with the desk's limits and breach reports. Orders go at the first click.",
};

const moduleNames: Record<keyof StaffTerminal["modules"], string> = {
  rulebook: "the firm's rules",
  ownLimits: "the trader's own limits",
  riskSizing: "sizing from risk",
  tradeDetails: "trade details",
  breachReports: "breach reports",
};

/** The parts the terminal shows besides the trading itself, for example "The firm's rules, sizing from risk and trade details". */
export function partsShown(modules: StaffTerminal["modules"]): string {
  const shown = (Object.keys(moduleNames) as (keyof typeof moduleNames)[]).filter((m) => modules[m]).map((m) => moduleNames[m]);
  if (shown.length === 0) {
    return "Only the trading";
  }

  const text = shown.length === 1 ? shown[0] : `${shown.slice(0, -1).join(", ")} and ${shown[shown.length - 1]}`;
  return text.charAt(0).toUpperCase() + text.slice(1);
}
