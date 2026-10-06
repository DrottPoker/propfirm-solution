import type { Account } from "./api/types";

/**
 * The guide's steps for a new firm, in order: the look, the first challenge's price, how traders pay, and trying it.
 * The name is the short one under each part of the guide's bar.
 */
export const guideSteps = [
  { key: "look", name: "Logo and color", title: "Make it yours", text: "Your logo and brand color, on every page of your portal." },
  { key: "price", name: "Price", title: "Price your first challenge", text: "Your sandbox has a two-step challenge. Give it a price and put it up for sale." },
  { key: "payments", name: "Payments", title: "Choose how traders pay", text: "Test payments are enough to try. Before you go live, connect Stripe or your own checkout." },
  { key: "try", name: "Try as a trader", title: "Try it as a trader", text: "Buy the challenge in your own shop with a test payment, then open the terminal and trade." },
] as const;

export type GuideStep = (typeof guideSteps)[number]["key"];

/** The step in the address, or the first one. */
export function stepOf(value: string | string[] | undefined): GuideStep {
  return guideSteps.find((s) => s.key === value)?.key ?? guideSteps[0].key;
}

/** The step after or before, staying within the guide. */
export function stepAfter(step: GuideStep, by: 1 | -1): GuideStep {
  const index = guideSteps.findIndex((s) => s.key === step);
  return guideSteps[Math.min(guideSteps.length - 1, Math.max(0, index + by))].key;
}

/** One thing to do when trying the portal as a trader, ticked off by itself once it has happened. */
export type TryItem = { key: string; label: string; done: boolean };

/** Trying it as a trader, from the firm's newest accounts: a challenge bought, its trading account opened, and a first trade. */
export function tryChecklist(accounts: Account[]): TryItem[] {
  const started = accounts.filter((a) => a.status !== "Cancelled");
  return [
    { key: "bought", label: "Buy your challenge in your shop, with a test payment", done: started.length > 0 },
    { key: "opened", label: "The account's trading account opens by itself", done: started.some((a) => a.tradingAccountId !== null) },
    { key: "traded", label: "Open the terminal from the account and place a trade", done: started.some((a) => a.tradingDays > 0) },
  ];
}
