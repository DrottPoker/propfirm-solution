"use client";

import Link from "next/link";
import { useState } from "react";

import type { Order, OrderStatus } from "@/lib/api/types";
import { formatDateTime, formatMoney } from "@/lib/format";
import { canMarkPaid, canMarkRefunded, orderNote, orderStatusLabels, providerLabels } from "@/lib/orders";
import { useFirmOrders, useFirmSettings, useOrderDecision } from "@/lib/queries";

import { buttonClass, ErrorText, Panel, secondaryButtonClass } from "./ui";

const views: { label: string; status: OrderStatus | null }[] = [
  { label: "All", status: null },
  { label: "Paid", status: "Paid" },
  { label: "Waiting for payment", status: "Pending" },
  { label: "Expired", status: "Expired" },
];

const statusStyles: Record<OrderStatus, string> = {
  Pending: "bg-warning/20 text-warning",
  Paid: "bg-profit/20 text-profit",
  Expired: "bg-muted/20 text-muted",
};

/** The challenges bought in the portal. A paid order has started its account. */
export function AdminOrders() {
  const [view, setView] = useState(views[0]);
  const orders = useFirmOrders(view.status);
  const settings = useFirmSettings();

  return (
    <main className="mx-auto flex w-full max-w-6xl flex-col gap-6 p-6">
      <Panel
        title="Orders"
        actions={
          <nav aria-label="Orders to show" className="flex gap-2 text-sm">
            {views.map((v) => (
              <button
                key={v.label}
                type="button"
                aria-pressed={v === view}
                onClick={() => setView(v)}
                className={`rounded border px-3 py-1 ${v === view ? "border-accent" : "border-border hover:border-muted"}`}
              >
                {v.label}
              </button>
            ))}
          </nav>
        }
      >
        <p className="text-sm text-muted">
          Challenges bought in your portal. The money goes straight to your payment provider, and a paid order starts its challenge. A refund
          or a dispute does not cancel the account: cancel it on the account if you want to.
        </p>
        {settings.data && !settings.data.payments.active && (
          <p className="text-sm text-warning">
            Your portal sells nothing right now. Choose how you take payment under{" "}
            <Link href="/admin/settings" className="underline">
              Settings
            </Link>{" "}
            and set prices under{" "}
            <Link href="/admin/challenges" className="underline">
              Challenges
            </Link>
            .
          </p>
        )}
        <ErrorText error={orders.error} />
        {orders.data && orders.data.length === 0 && <p className="text-sm text-muted">No orders here.</p>}
        {orders.data && orders.data.length > 0 && <OrderTable orders={orders.data} />}
      </Panel>
    </main>
  );
}

function OrderTable({ orders }: { orders: Order[] }) {
  const cell = "py-2 pl-6";
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="text-left text-muted">
          <tr>
            <th className="py-2 font-normal">Order</th>
            <th className={`${cell} font-normal`}>Buyer</th>
            <th className={`${cell} font-normal`}>Challenge</th>
            <th className={`${cell} text-right font-normal`}>Amount</th>
            <th className={`${cell} font-normal`}>Paid with</th>
            <th className={`${cell} font-normal`}>Status</th>
            <th className={`${cell} font-normal`}>Note</th>
            <th className={`${cell} font-normal`} />
          </tr>
        </thead>
        <tbody>
          {orders.map((order) => (
            <tr key={order.id} className="border-t border-border align-top">
              <td className="whitespace-nowrap py-2">
                #{order.number}
                <span className="block text-xs text-muted">{formatDateTime(order.createdAt)}</span>
              </td>
              <td className={cell}>{order.email}</td>
              <td className={cell}>
                {order.accountId ? (
                  <Link href={`/admin/accounts/${order.accountId}`} className="text-accent hover:underline">
                    {order.challengeId}
                  </Link>
                ) : (
                  order.challengeId
                )}
              </td>
              <td className={`${cell} whitespace-nowrap text-right font-mono tabular-nums`}>
                {formatMoney(order.amount)} {order.currency}
              </td>
              <td className={cell}>{providerLabels[order.provider]}</td>
              <td className={cell}>
                <span className={`whitespace-nowrap rounded px-2 py-0.5 ${statusStyles[order.status]}`}>{orderStatusLabels[order.status]}</span>
              </td>
              <td className={`${cell} text-muted`}>{orderNote(order)}</td>
              <td className={cell}>
                <OrderDecisions order={order} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Orders from the firm's own checkout are marked as paid by hand, and orders not paid through Stripe as refunded. */
function OrderDecisions({ order }: { order: Order }) {
  const decision = useOrderDecision();
  const amount = `${formatMoney(order.amount)} ${order.currency}`;

  const markPaid = () => {
    const reference = window.prompt(`Mark order #${order.number} of ${amount} as paid? Its challenge starts. Your reference for the payment (optional):`);
    if (reference !== null) {
      decision.mutate({ kind: "mark-paid", orderId: order.id, reference: reference.trim() });
    }
  };

  const markRefunded = () => {
    const reference = window.prompt(`Mark order #${order.number} as refunded? The account is not cancelled. Your reference for the refund (optional):`);
    if (reference !== null) {
      decision.mutate({ kind: "mark-refunded", orderId: order.id, reference: reference.trim() });
    }
  };

  if (!canMarkPaid(order) && !canMarkRefunded(order)) {
    return null;
  }

  return (
    <div className="flex flex-col items-end gap-2">
      <div className="flex justify-end gap-2">
        {canMarkPaid(order) && (
          <button type="button" disabled={decision.isPending} onClick={markPaid} className={buttonClass}>
            Mark as paid
          </button>
        )}
        {canMarkRefunded(order) && (
          <button type="button" disabled={decision.isPending} onClick={markRefunded} className={secondaryButtonClass}>
            Mark as refunded
          </button>
        )}
      </div>
      <ErrorText error={decision.error} />
    </div>
  );
}
