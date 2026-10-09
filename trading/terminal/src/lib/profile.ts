import type { ServerInfo } from "./api/types";

// How a server's terminal works for its traders (ADR 0058): the kind of business, the parts shown, how orders and
// tickets start, password login, the firm's pages and a risk warning. Set by the firm in the trading service; the
// terminal only shows what the profile turns on, and uses the kind's words.

export type TerminalProfile = ServerInfo["profile"];
export type TerminalKind = TerminalProfile["kind"];
export type Modules = TerminalProfile["modules"];

/** The profile a server has until its firm sets one, the same as the trading service's. */
export const standardProfile: TerminalProfile = {
  kind: "Prop",
  modules: { rulebook: true, ownLimits: true, riskSizing: true, tradeDetails: true, breachReports: true },
  confirmOrders: false,
  startingSize: { kind: "Smallest", value: null },
  passwordLogin: true,
  links: { help: null, support: null, terms: null, privacy: null, passwordReset: null },
  riskWarning: null,
};

/** The words that differ between kinds of business. Everything else is said the same way for all. */
export interface KindWords {
  /** The button and the panel with the account's rules and limits. */
  rules: string;
  /** What carries on after the trader's own lock, for example "The challenge goes on". */
  carriesOn: string;
  /** Money taken out of the account, in the history. */
  withdrawal: string;
}

const words: Record<TerminalKind, KindWords> = {
  Prop: { rules: "Rules", carriesOn: "The challenge goes on.", withdrawal: "Payout" },
  Broker: { rules: "Limits", carriesOn: "The account stays open.", withdrawal: "Withdrawal" },
  Practice: { rules: "Limits", carriesOn: "The account stays open.", withdrawal: "Withdrawal" },
  Desk: { rules: "Limits", carriesOn: "The account stays open.", withdrawal: "Withdrawal" },
};

export function wordsFor(kind: TerminalKind): KindWords {
  return words[kind];
}

/** Whether the panel with the rules and limits has anything to show: the firm's rules or the trader's own limits. */
export function hasRulesPanel(modules: Modules): boolean {
  return modules.rulebook || modules.ownLimits;
}
