"use client";

import Link from "next/link";

import { useBranding } from "@/app/providers";
import type { BuyerOrder } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { buyerStage } from "@/lib/orders";
import { useBuyerOrder, useResendInvite } from "@/lib/queries";

import { FirmName } from "./FirmName";
import { buttonClass, ErrorText, Message, secondaryButtonClass } from "./ui";

/**
 * The buyer's order, where the payment provider sends the buyer back. It waits for the provider to confirm the
 * payment, then tells the buyer how to get into the portal.
 */
export function BuyerOrderView({ orderId, token }: { orderId: string; token: string | null }) {
  const order = useBuyerOrder(orderId, token ?? "");

  if (!token || order.isError) {
    return <Message text="This order cannot be found. Use the link from your payment." />;
  }

  if (!order.data) {
    return <Message text="Loading..." />;
  }

  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <section className="flex w-full max-w-md flex-col gap-4 rounded-lg border border-border bg-panel p-6">
        <div className="flex flex-col gap-1">
          <FirmName size="lg" />
          <h1 className="text-sm text-muted">Order #{order.data.number}</h1>
        </div>
        <p className="flex flex-wrap justify-between gap-2 text-sm">
          <span>{order.data.challengeName}</span>
          <span className="font-mono tabular-nums">
            {formatMoney(order.data.amount)} {order.data.currency}
          </span>
        </p>
        <Stage order={order.data} token={token} />
      </section>
    </main>
  );
}

function Stage({ order, token }: { order: BuyerOrder; token: string }) {
  const { name } = useBranding();
  const stage = buyerStage(order);
  switch (stage.kind) {
    case "waiting":
      return (
        <div role="status" className="flex flex-col gap-3 text-sm">
          <p>Waiting for the payment to be confirmed. This page updates by itself.</p>
          {order.checkoutUrl && (
            <a href={order.checkoutUrl} className="text-accent hover:underline">
              Not paid yet? Go to the payment.
            </a>
          )}
        </div>
      );
    case "expired":
      return (
        <div role="status" className="flex flex-col gap-3 text-sm">
          <p>This order expired without a payment.</p>
          <Link href="/buy" className={`${buttonClass} self-start`}>
            Buy again
          </Link>
        </div>
      );
    case "problem":
      return (
        <p role="status" className="text-sm text-warning">
          Your payment is received, but the challenge could not be started: {stage.problem} Contact {name}.
        </p>
      );
    case "log-in":
      return (
        <div role="status" className="flex flex-col gap-3 text-sm">
          <p className="text-profit">Payment received. Your challenge is starting.</p>
          <Link href={order.accountId ? `/accounts/${order.accountId}` : "/"} className={`${buttonClass} self-start`}>
            Go to your account
          </Link>
        </div>
      );
    case "invited":
    case "get-invite":
      return (
        <div role="status" className="flex flex-col gap-3 text-sm">
          <p className="text-profit">Payment received. Your challenge is starting.</p>
          <p>
            {stage.kind === "invited"
              ? `We have emailed ${stage.email} a link to choose your password for the portal.`
              : `Get a link at ${stage.email} to choose your password for the portal.`}
          </p>
          <SendInvite order={order} token={token} again={stage.kind === "invited"} />
        </div>
      );
  }
}

function SendInvite({ order, token, again }: { order: BuyerOrder; token: string; again: boolean }) {
  const resend = useResendInvite(order.id, token);
  return (
    <div className="flex flex-col gap-2">
      <button
        type="button"
        disabled={resend.isPending}
        onClick={() => resend.mutate()}
        className={`${again ? secondaryButtonClass : buttonClass} self-start`}
      >
        {again ? "Send the link again" : "Email me the link"}
      </button>
      {resend.isSuccess && <p className="text-profit">Sent.</p>}
      <ErrorText error={resend.error} />
    </div>
  );
}
