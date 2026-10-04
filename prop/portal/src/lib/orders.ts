import type { BuyerOrder, Order, OrderStatus, PaymentProvider } from "./api/types";

export const orderStatusLabels: Record<OrderStatus, string> = {
  Pending: "Waiting for payment",
  Paid: "Paid",
  Expired: "Expired",
};

export const providerLabels: Record<PaymentProvider, string> = {
  Test: "Test payment",
  Stripe: "Stripe",
  External: "Your own checkout",
};

/** Currencies a price can be in. Prop.Api checks the same list. */
export const priceCurrencies = ["USD", "EUR", "GBP", "CHF", "CAD", "AUD", "NZD", "SEK", "NOK", "DKK", "PLN", "CZK", "SGD", "HKD", "AED"] as const;

/**
 * Only orders from the firm's own checkout are marked as paid by hand. Stripe reports its own payments, and a
 * test order is paid on its test page. A late payment of an expired order still counts.
 */
export function canMarkPaid(order: Order): boolean {
  return order.provider === "External" && order.status !== "Paid";
}

/** Stripe reports its own refunds. Other paid orders are marked by hand once the money is back with the buyer. */
export function canMarkRefunded(order: Order): boolean {
  return order.provider !== "Stripe" && order.status === "Paid" && order.refundedAt === null;
}

/** What else the firm should know about the order, such as a refund or a dispute. */
export function orderNote(order: Order): string {
  const notes = [
    order.problem,
    order.disputedAt && "The buyer disputed the payment.",
    order.refundedAt && "Refunded.",
    order.paymentReference && `Payment ${order.paymentReference}.`,
  ];
  return notes.filter((note): note is string => Boolean(note)).join(" ");
}

/** Where the buyer's order is, which decides what the order's page says. */
export type BuyerStage =
  | { kind: "waiting" }
  | { kind: "expired" }
  | { kind: "problem"; problem: string }
  | { kind: "log-in" }
  | { kind: "choose-password" }
  | { kind: "invited"; email: string }
  | { kind: "get-invite"; email: string };

export function buyerStage(order: BuyerOrder): BuyerStage {
  switch (order.status) {
    case "Pending":
      return { kind: "waiting" };
    case "Expired":
      return { kind: "expired" };
    case "Paid":
      if (order.accountId === null) {
        return { kind: "problem", problem: order.problem ?? "The challenge could not be started." };
      }

      if (order.canLogIn) {
        return { kind: "log-in" };
      }

      if (order.canChoosePassword) {
        return { kind: "choose-password" };
      }

      return order.inviteSentAt === null ? { kind: "get-invite", email: order.email } : { kind: "invited", email: order.email };
  }
}
