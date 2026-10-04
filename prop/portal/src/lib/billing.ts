import type { Billing, Card, Charge, ChargeKind, ChargeStatus, Prices, Slots } from "./api/types";
import { formatDate, formatMoney } from "./format";

export const chargeKindLabels: Record<ChargeKind, string> = {
  Activation: "Going live",
  Renewal: "Month",
  Slots: "More slots",
  Deposit: "Deposit",
};

export const chargeStatusLabels: Record<ChargeStatus, string> = {
  Pending: "Not paid yet",
  Paid: "Paid",
  Failed: "Not paid",
  Void: "Cancelled",
};

/** How much of the limit is taken, from 0 to 100. Null when there is no limit. */
export function slotsTaken(slots: Slots): number | null {
  if (slots.slots === null) {
    return null;
  }

  return slots.slots === 0 ? 100 : Math.min(100, ((slots.used + slots.reserved) / slots.slots) * 100);
}

/** The slots in a sentence, for example "12 of 50 slots taken, 2 of them by orders waiting for payment. 38 free." */
export function slotsSummary(slots: Slots): string {
  const taken = slots.used + slots.reserved;
  const byOrders = slots.reserved > 0 ? `, ${slots.reserved} of them by ${plural(slots.reserved, "order")} waiting for payment` : "";
  if (slots.slots === null) {
    return `${taken} open ${plural(taken, "challenge")}${byOrders}. No limit.`;
  }

  return `${taken} of ${slots.slots} slots taken${byOrders}. ${slots.free} free.`;
}

/** Each tier's price, for example "Slots 101 to 500: 4.50 USD each per month". */
export function priceTiers(prices: Prices): string[] {
  return prices.slotPrices.map((tier, i) => {
    const next = prices.slotPrices[i + 1];
    const range = next ? `Slots ${tier.from} to ${next.from - 1}` : `Slot ${tier.from} and up`;
    return `${range}: ${formatMoney(tier.price)} ${prices.currency} each per month`;
  });
}

/** The saved card, for example "visa ending 4242, expires 12/2030". */
export function cardLabel(card: Card): string {
  return `${card.brand} ending ${card.last4}, expires ${String(card.expMonth).padStart(2, "0")}/${card.expYear}`;
}

/** What a charge was for, for example "Slots for November 2026". */
export function chargeLabel(charge: Charge): string {
  switch (charge.kind) {
    case "Activation":
      return "Startup fee and the first month";
    case "Renewal":
      return `Slots for ${monthName(charge.month)}`;
    case "Deposit":
      return "Deposit for the review, taken off the startup fee";
    default:
      return `More slots, ${monthName(charge.month)}`;
  }
}

/** The month of a date, for example "November 2026". */
export function monthName(isoDate: string): string {
  return new Date(`${isoDate}T00:00:00Z`).toLocaleDateString("en-GB", { month: "long", year: "numeric", timeZone: "UTC" });
}

/** Monthly charges that were declined, which the firm can pay now, oldest first. One that waits for its first try is not shown. */
export function unpaidCharges(billing: Billing): Charge[] {
  return billing.charges.filter((c) => c.canPay && (c.failure !== null || c.status === "Failed")).reverse();
}

/** billingLink is false when the billing page cannot help. */
export type BillingNotice = { tone: "loss" | "warning"; text: string; billingLink?: false };

/** What the firm's administrators must know about the billing on every page, or null when all is well. */
export function billingNotice(billing: Billing): BillingNotice | null {
  if (billing.suspension !== null) {
    return {
      tone: "loss",
      text: `Your firm is suspended: ${billing.suspension.reason} No new challenges can start, and your traders' accounts are paused until we lift it. Reply to our email to talk to us.`,
      billingLink: false,
    };
  }

  if (billing.unpaidSince !== null) {
    return {
      tone: "loss",
      text: "This month is not paid. No new challenges can start, and your traders' accounts are paused until it is.",
    };
  }

  const unpaid = unpaidCharges(billing)[0];
  if (unpaid) {
    const when = unpaid.nextAttemptAt ? ` We try again on ${formatDate(unpaid.nextAttemptAt)}.` : "";
    return { tone: "warning", text: `${chargeLabel(unpaid)}: the payment was declined.${when} Pay it to keep your challenges running.` };
  }

  if (billing.status === "Live" && billing.slots.free === 0) {
    return { tone: "warning", text: "Every slot is taken, so no new challenges can start and your shop does not sell. Buy more slots." };
  }

  if (billing.slots.warning) {
    return { tone: "warning", text: slotsSummary(billing.slots) };
  }

  return null;
}

function plural(count: number, word: string): string {
  return count === 1 ? word : `${word}s`;
}
