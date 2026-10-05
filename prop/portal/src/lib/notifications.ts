/** Who an email goes to: the firm's administrators, or the trader the challenge is for. */
export type NotificationAudience = "team" | "trader";

export type NotificationKind = {
  kind: string;
  audience: NotificationAudience;
  label: string;
  description: string;
};

/** The emails we send for the firm, in the order the admin panel lists them. The kinds are the service's. */
export const notificationKinds: NotificationKind[] = [
  { kind: "firmSale", audience: "team", label: "New sale", description: "A trader bought a challenge in your portal." },
  {
    kind: "firmFundingAwaited",
    audience: "team",
    label: "Waiting for your approval",
    description: "A trader passed every phase, and the funded account waits for you.",
  },
  { kind: "firmPayoutRequested", audience: "team", label: "Payout requested", description: "A funded trader asked for a payout." },
  {
    kind: "firmSupport",
    audience: "team",
    label: "Support ticket",
    description: "A trader opened a support ticket, or wrote in one that waited for the trader or was closed.",
  },
  { kind: "traderStagePassed", audience: "trader", label: "Phase passed", description: "The trader passed a phase, and the next one starts." },
  { kind: "traderPassed", audience: "trader", label: "Challenge passed", description: "Every phase is passed, and you review the funded account." },
  { kind: "traderFunded", audience: "trader", label: "Funded account ready", description: "The funded account is open." },
  { kind: "traderEnded", audience: "trader", label: "Challenge ended", description: "A loss limit was broken, or time ran out." },
  { kind: "traderPayouts", audience: "trader", label: "Payout updates", description: "A payout was approved, paid or rejected." },
  {
    kind: "traderInactivity",
    audience: "trader",
    label: "Trade soon reminder",
    description: "The challenge ends in a few days unless the trader opens a trade.",
  },
  { kind: "traderSupportAnswers", audience: "trader", label: "Support answer", description: "You answered the trader's support ticket. The email has your answer." },
];

/** Whether the firm sends the email. One it never chose is sent. */
export function isOn(settings: Record<string, boolean>, kind: string): boolean {
  return settings[kind] ?? true;
}
