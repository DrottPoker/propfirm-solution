/** The guide's steps for a new firm, in order: the look, the first challenge's price, how traders pay, and trying it. */
export const guideSteps = [
  { key: "look", title: "Make it yours", text: "Your logo and brand color, on every page of your portal." },
  { key: "price", title: "Price your first challenge", text: "Your sandbox has a two-step challenge. Give it a price and put it up for sale." },
  { key: "payments", title: "Choose how traders pay", text: "Test payments are enough to try. Before you go live, connect Stripe or your own checkout." },
  { key: "try", title: "Try it as a trader", text: "Buy the challenge in your own shop with a test payment, then open the terminal and trade." },
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
