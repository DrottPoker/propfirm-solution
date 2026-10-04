import type { Billing, ChallengeDefinition, ChallengePrice, FirmSettings } from "./api/types";
import { formatMoney } from "./format";

/** Where a step to live is: done, the next to do, still to do, waiting for us, or not possible yet. */
export type StepStatus = "done" | "current" | "todo" | "waiting" | "locked";

export type GoLiveStep = {
  key: string;
  status: StepStatus;
  title: string;
  detail: string;
  /** Where to do the step. An external address opens in a new tab. */
  action: { label: string; href: string; external?: boolean } | null;
};

/**
 * The steps a firm in the sandbox takes to go live, in order. Most can be done in any order; going live comes last,
 * after we have approved the firm. The first step still to do is the current one.
 */
export function goLiveSteps(input: {
  settings: FirmSettings;
  challenges: ChallengeDefinition[];
  prices: ChallengePrice[];
  billing: Billing;
  accounts: number;
}): GoLiveStep[] {
  const { settings, challenges, prices, billing, accounts } = input;
  const forSale = prices.filter((p) => p.forSale).length;
  const provider = settings.payments.provider;
  const review = billing.review;
  const deposit = billing.prices.reviewDeposit;
  const steps: GoLiveStep[] = [
    {
      key: "server",
      status: settings.status === "Provisioning" ? "waiting" : "done",
      title: "Your trading server is ready",
      detail: settings.status === "Provisioning" ? "It is being set up, which takes a few seconds." : "Set up when you signed up.",
      action: null,
    },
    {
      key: "challenges",
      status: challenges.length > 0 ? "done" : "todo",
      title: "Create your challenges",
      detail:
        challenges.length > 0
          ? `${names(challenges.map((c) => c.name))}. Change the rules or add more whenever you like.`
          : "Choose the account sizes, targets and loss limits you sell.",
      action: { label: "Challenges", href: "/admin/challenges" },
    },
    {
      key: "prices",
      status: forSale > 0 ? "done" : "todo",
      title: "Set a price for each challenge",
      detail:
        forSale > 0
          ? `${forSale === 1 ? "One challenge is" : `${forSale} challenges are`} for sale in your portal's shop.`
          : "Give each challenge you sell a price, and put it up for sale in your portal's shop.",
      action: { label: "Prices", href: "/admin/challenges" },
    },
    {
      key: "checkout",
      status: provider === "Stripe" || provider === "External" ? "done" : "todo",
      title: "Choose how traders pay",
      detail:
        provider === "Stripe"
          ? "Buyers pay with Stripe, to your own Stripe account."
          : provider === "External"
            ? "Buyers pay on your own checkout page."
            : provider === "Test"
              ? "Test payments are on now. Before you go live, connect Stripe with your own keys, or your own checkout page."
              : "Your shop takes no payment yet. Connect Stripe or your own checkout page, or try test payments first.",
      action: { label: "Set up checkout", href: "/admin/checkout" },
    },
    {
      key: "design",
      status: settings.logoUrl !== null || Object.keys(settings.colors).length > 0 ? "done" : "todo",
      title: "Make the portal yours",
      detail: "Add your logo and colors. Traders see them on every page of your portal.",
      action: { label: "Portal design", href: "/admin/design" },
    },
    {
      key: "try",
      status: accounts > 0 ? "done" : "todo",
      title: "Try it as a trader",
      detail: "Buy a challenge in your shop with a test payment, then open the terminal and place a trade.",
      action: { label: "Open your shop", href: "/buy", external: true },
    },
    reviewStep(review, deposit > 0 && billing.depositPaid === 0 ? `${formatMoney(deposit)} ${billing.prices.currency}` : null),
    {
      key: "live",
      status: settings.status === "Live" ? "done" : review === "Approved" ? "todo" : "locked",
      title: "Go live",
      detail:
        review === "Approved"
          ? "Choose your slots, and pay the startup fee less the deposit, and your first month."
          : "After we have approved your firm: choose your slots, and pay the startup fee less the deposit, and your first month.",
      action: review === "Approved" ? { label: "Go live", href: "/admin/billing" } : null,
    },
  ];

  const next = steps.findIndex((s) => s.status === "todo");
  return steps.map((s, i) => (i === next ? { ...s, status: "current" } : s));
}

function reviewStep(review: Billing["review"], deposit: string | null): GoLiveStep {
  const step = { key: "review", title: "Send your company for review" };
  switch (review) {
    case "Submitted":
      return { ...step, status: "waiting", detail: "We are reviewing your firm, usually within a day, and email you when we have.", action: { label: "Your application", href: "/admin/verification" } };
    case "ChangesRequested":
      return { ...step, status: "todo", detail: "We asked for changes. Make them and send the application again, with no new deposit.", action: { label: "Make the changes", href: "/admin/verification" } };
    case "Approved":
      return { ...step, status: "done", detail: "We have approved your firm.", action: null };
    case "Rejected":
      return { ...step, status: "locked", detail: "We could not approve your firm. Our email says why.", action: { label: "Your application", href: "/admin/verification" } };
    default:
      return {
        ...step,
        status: "todo",
        detail: `Company details, owners and links.${deposit ? ` You pay a ${deposit} deposit when you send it, taken off the startup fee.` : ""} We usually answer within a day.`,
        action: { label: review === "Draft" ? "Continue the application" : "Start the application", href: "/admin/verification" },
      };
  }
}

/** Names in a sentence, for example "A, B and C". */
function names(list: string[]): string {
  return list.length <= 1 ? (list[0] ?? "") : `${list.slice(0, -1).join(", ")} and ${list[list.length - 1]}`;
}
