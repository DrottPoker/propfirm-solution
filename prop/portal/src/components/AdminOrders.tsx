"use client";

import Link from "next/link";
import { useId, useState } from "react";

import type { Order, OrderStatus } from "@/lib/api/types";
import { formatDateTime, formatMoney } from "@/lib/format";
import { canMarkPaid, canMarkRefunded, orderNote, orderStatusLabels, providerLabels } from "@/lib/orders";
import { useChallenges, useFirmOrders, useFirmSettings, useOrderDecision } from "@/lib/queries";

import { Modal } from "./Dialog";
import { BagIcon } from "./icons";
import { AdminPage, Badge, type BadgeTone, buttonClass, EmptyState, ErrorText, fieldClass, FilterTabs, PageHeader, secondaryButtonClass } from "./ui";

const views: { id: string; label: string; status: OrderStatus | null }[] = [
  { id: "all", label: "All", status: null },
  { id: "paid", label: "Paid", status: "Paid" },
  { id: "pending", label: "Waiting for payment", status: "Pending" },
  { id: "expired", label: "Expired", status: "Expired" },
];

const statusTones: Record<OrderStatus, BadgeTone> = {
  Pending: "warning",
  Paid: "profit",
  Expired: "muted",
};

/** The challenges bought in the portal. A paid order has started its account. */
export function AdminOrders() {
  const [viewId, setViewId] = useState("all");
  const view = views.find((v) => v.id === viewId)!;
  const orders = useFirmOrders(view.status);
  const settings = useFirmSettings();
  const challenges = useChallenges();
  const challengeName = (id: string) => challenges.data?.find((c) => c.id === id)?.name ?? id;

  return (
    <AdminPage>
      <PageHeader
        title="Orders"
        description="Challenges bought in your portal. The money goes straight to your payment provider, and a paid order starts its challenge. A refund or a dispute does not cancel the account: cancel it on the account if you want to."
      />
      {settings.data && !settings.data.payments.active && (
        <p className="rounded-lg border border-warning/40 bg-warning/10 px-4 py-3 text-sm">
          Your portal sells nothing right now. Choose how traders pay under{" "}
          <Link href="/admin/checkout" className="underline">
            Checkout
          </Link>{" "}
          and set prices under{" "}
          <Link href="/admin/challenges" className="underline">
            Challenges
          </Link>
          .
        </p>
      )}

      <section aria-label="Orders" className="flex flex-col rounded-lg border border-border bg-panel">
        <div className="border-b border-border px-4 py-3">
          <FilterTabs label="Orders to show" options={views.map((v) => ({ value: v.id, label: v.label }))} value={viewId} onChange={setViewId} />
        </div>
        {orders.error && (
          <div className="px-4 py-3">
            <ErrorText error={orders.error} />
          </div>
        )}
        {orders.isPending ? (
          <p className="px-4 py-6 text-sm text-muted">Loading...</p>
        ) : (orders.data ?? []).length === 0 ? (
          <EmptyState icon={<BagIcon className="size-6" />} title="No orders here" text="Orders come when traders buy a challenge in your portal's shop." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[60rem] text-sm">
              <thead className="text-left text-muted">
                <tr>
                  <th scope="col" className="px-4 py-3 font-normal">
                    Order
                  </th>
                  <th scope="col" className="px-4 py-3 font-normal">
                    Buyer
                  </th>
                  <th scope="col" className="px-4 py-3 font-normal">
                    Challenge
                  </th>
                  <th scope="col" className="px-4 py-3 text-right font-normal">
                    Amount
                  </th>
                  <th scope="col" className="px-4 py-3 font-normal">
                    Paid with
                  </th>
                  <th scope="col" className="px-4 py-3 font-normal">
                    Status
                  </th>
                  <th scope="col" className="px-4 py-3 font-normal">
                    <span className="sr-only">Decision</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {(orders.data ?? []).map((order) => (
                  <tr key={order.id} className="border-t border-border align-top">
                    <td className="whitespace-nowrap px-4 py-3">
                      <span>{order.number}</span>
                      <span className="block text-xs text-muted">{formatDateTime(order.createdAt)}</span>
                    </td>
                    <td className="px-4 py-3">
                      {order.buyerName ?? order.email}
                      {order.buyerName && <span className="block text-xs text-muted">{order.email}</span>}
                    </td>
                    <td className="px-4 py-3">
                      {order.accountId ? (
                        <Link href={`/admin/accounts/${order.accountId}`} className="text-accent hover:underline">
                          {challengeName(order.challengeId)}
                        </Link>
                      ) : (
                        challengeName(order.challengeId)
                      )}
                      <span className="block font-mono text-xs text-muted">{order.challengeId}</span>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-right tabular-nums">
                      {formatMoney(order.amount)} {order.currency}
                      {order.discountCode && order.listAmount != null && (
                        <span className="block font-sans text-xs text-muted">
                          Code {order.discountCode}, was {formatMoney(order.listAmount)}
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3">{providerLabels[order.provider]}</td>
                    <td className="px-4 py-3">
                      <Badge tone={statusTones[order.status]}>{orderStatusLabels[order.status]}</Badge>
                      {orderNote(order) && <span className="mt-1 block max-w-64 text-xs text-muted">{orderNote(order)}</span>}
                    </td>
                    <td className="px-4 py-3">
                      <OrderDecisions order={order} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </AdminPage>
  );
}

/** Orders from the firm's own checkout are marked as paid by hand, and orders not paid through Stripe as refunded. */
function OrderDecisions({ order }: { order: Order }) {
  const [dialog, setDialog] = useState<"mark-paid" | "mark-refunded" | null>(null);
  if (!canMarkPaid(order) && !canMarkRefunded(order)) {
    return null;
  }

  return (
    <div className="flex flex-wrap justify-end gap-2">
      {canMarkPaid(order) && (
        <button type="button" onClick={() => setDialog("mark-paid")} className={`${buttonClass} text-sm`}>
          Mark as paid
        </button>
      )}
      {canMarkRefunded(order) && (
        <button type="button" onClick={() => setDialog("mark-refunded")} className={`${secondaryButtonClass} text-sm`}>
          Mark as refunded
        </button>
      )}
      {dialog && <OrderDialog order={order} kind={dialog} onClose={() => setDialog(null)} />}
    </div>
  );
}

function OrderDialog({ order, kind, onClose }: { order: Order; kind: "mark-paid" | "mark-refunded"; onClose: () => void }) {
  const decision = useOrderDecision();
  const formId = useId();
  const [reference, setReference] = useState("");
  const amount = `${formatMoney(order.amount)} ${order.currency}`;
  const paid = kind === "mark-paid";

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    decision.mutate({ kind, orderId: order.id, reference: reference.trim() }, { onSuccess: onClose });
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={paid ? `Mark order ${order.number} of ${amount} as paid?` : `Mark order ${order.number} as refunded?`}
      description={paid ? `${order.email}. Its challenge starts at once.` : `${order.email}. The account is not cancelled: cancel it on the account if you want to.`}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondaryButtonClass}>
            Cancel
          </button>
          <button type="submit" form={formId} disabled={decision.isPending} className={buttonClass}>
            {decision.isPending ? "Saving..." : paid ? "Mark as paid" : "Mark as refunded"}
          </button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} className="flex flex-col gap-3">
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">
            Your reference for the {paid ? "payment" : "refund"} <span className="font-normal text-muted">(optional)</span>
          </span>
          <input value={reference} onChange={(e) => setReference(e.target.value)} className={fieldClass} />
        </label>
        <ErrorText error={decision.error} />
      </form>
    </Modal>
  );
}
