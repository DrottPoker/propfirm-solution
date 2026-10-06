import type { Billing, Card, Charge, ChargeKind, ChargeStatus, Prices, Quote, Slots, Vat } from "./api/types";
import { formatDate, formatMoney } from "./format";

export const chargeKindLabels: Record<ChargeKind, string> = {
  Activation: "Going live",
  Renewal: "Month",
  Slots: "More slots",
  Deposit: "Deposit",
  IdentityChecks: "KYC checks",
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

/**
 * A month's price for a number of slots, without VAT: the package, and each slot beyond it at its tier's price. The
 * same rule as Prop.Api's BillingRules.MonthlyPrice, for the calculator on the front page.
 */
export function monthlyPriceFor(prices: Pick<Prices, "packagePrice" | "slotPrices">, slots: number): number {
  let total = prices.packagePrice;
  prices.slotPrices.forEach((tier, i) => {
    const last = i + 1 < prices.slotPrices.length ? prices.slotPrices[i + 1].from - 1 : Number.MAX_SAFE_INTEGER;
    if (slots >= tier.from) {
      total += (Math.min(slots, last) - tier.from + 1) * tier.price;
    }
  });
  return total;
}

/** The monthly prices: the package first, then each tier beyond it, for example "Slots 26 to 100: 5.00 USD each per month". */
export function monthlyPrices(prices: Prices): string[] {
  const tiers = prices.slotPrices.map((tier, i) => {
    const next = prices.slotPrices[i + 1];
    const range = next ? `Slots ${tier.from} to ${next.from - 1}` : `Slot ${tier.from} and up`;
    return `${range}: ${formatMoney(tier.price)} ${prices.currency} each per month`;
  });
  return [`Package with ${prices.packageSlots} ${plural(prices.packageSlots, "slot")}: ${formatMoney(prices.packagePrice)} ${prices.currency} per month`, ...tiers];
}

/** What one round of automatic expansion adds, for example "Adds 10 slots for 50.00 USD a month, and 43.55 USD for the rest of October if it happens today." */
export function expansionText(expansion: NonNullable<Quote["expansion"]>, currency: string): string {
  return `Adds ${expansion.slots} ${plural(expansion.slots, "slot")} for ${formatMoney(expansion.monthlyPrice)} ${currency} a month, and ${formatMoney(expansion.restOfMonth)} ${currency} for the rest of ${monthName(expansion.month).split(" ")[0]} if it happens today. Without VAT.`;
}

/** How VAT applies to the firm's charges, in a sentence. Our prices are without VAT. */
export function vatText(vat: Vat): string {
  switch (vat.treatment) {
    case "Charged":
      return `Prices are without VAT. ${vat.percent}% VAT is added for your company.`;
    case "ReverseCharge":
      return "Prices are without VAT, and none is added: your company is in another EU country with a VAT number, so it accounts for the VAT itself (reverse charge).";
    case "OutsideEu":
      return "Prices are without VAT, and none is added, since your company is outside the EU.";
    default:
      return "Prices are without VAT.";
  }
}

/** The VAT on an amount without VAT, in whole cents. The service's own figure is what is charged. */
/**
 * A monthly payment's amount, saying whether VAT is in it: "625.00 USD incl. VAT (500.00 + 125.00 VAT)", or the amount
 * alone without VAT. Prices elsewhere are without VAT, so an amount with it says so.
 */
export function chargeAmountText(charge: { amount: number; netAmount: number; vatAmount: number }, currency: string): string {
  return charge.vatAmount > 0
    ? `${formatMoney(charge.amount)} ${currency} incl. VAT (${formatMoney(charge.netAmount)} + ${formatMoney(charge.vatAmount)} VAT)`
    : `${formatMoney(charge.amount)} ${currency}`;
}

export function vatOn(net: number, vat: Vat): number {
  return Math.round(net * vat.percent) / 100;
}

/** Where the invoice of a paid charge is, as a PDF. */
export function invoiceUrl(charge: Charge): string {
  return `/api/portal/admin/billing/charges/${charge.id}/invoice`;
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
    case "IdentityChecks":
      return `KYC checks for ${monthName(charge.month)}`;
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
