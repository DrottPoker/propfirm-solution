"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";

import { useBranding } from "@/app/providers";
import type { BuyerOrder } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { buyerStage } from "@/lib/orders";
import { useBuyerOrder, useChooseOrderPassword, useResendInvite } from "@/lib/queries";

import { FirmName } from "./FirmName";
import { PasswordFields } from "./PasswordReset";
import { buttonClass, ErrorText, Message, secondaryButtonClass } from "./ui";

/**
 * The buyer's order, where the payment provider sends the buyer back. It waits for the provider to confirm the
 * payment, then lets a new buyer choose a password right here, or tells the buyer how to get into the portal.
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
          <p className="text-profit">{startedText(order)}</p>
          <Link href={order.accountId ? `/accounts/${order.accountId}` : "/"} className={`${buttonClass} self-start`}>
            Go to your account
          </Link>
        </div>
      );
    case "choose-password":
      return <ChoosePassword order={order} token={token} />;
    case "invited":
    case "get-invite":
      return (
        <div role="status" className="flex flex-col gap-3 text-sm">
          <p className="text-profit">{startedText(order)}</p>
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

/** Once the order has its account, the challenge has started; until then it is on its way. */
function startedText(order: BuyerOrder): string {
  return order.accountId ? "Payment received. Your challenge has started." : "Payment received. Your challenge is starting.";
}

/** A new buyer chooses the password here and goes straight to the account. The email is confirmed afterwards, with the link we sent. */
function ChoosePassword({ order, token }: { order: BuyerOrder; token: string }) {
  const router = useRouter();
  const choose = useChooseOrderPassword(order.id, token);
  const [password, setPassword] = useState("");
  const [repeated, setRepeated] = useState("");
  const [mismatch, setMismatch] = useState(false);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    setMismatch(password !== repeated);
    if (password === repeated) {
      choose.mutate(password, { onSuccess: () => router.replace(order.accountId ? `/accounts/${order.accountId}` : "/") });
    }
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-3 text-sm">
      <p className="text-profit">{startedText(order)}</p>
      <p>
        Choose a password for <span className="font-medium">{order.email}</span>, and open your account. You log in with this email and the password from
        now on.
      </p>
      <PasswordFields password={password} repeated={repeated} onPassword={setPassword} onRepeated={setRepeated} />
      <ErrorText error={mismatch ? new Error("The passwords are not the same.") : choose.error} />
      <button type="submit" disabled={choose.isPending || choose.isSuccess} className={`${buttonClass} self-start`}>
        {choose.isPending || choose.isSuccess ? "Opening..." : "Open my account"}
      </button>
      {order.inviteSentAt && <p className="text-xs text-muted">We have also emailed you a link. Open it later to confirm your email, which payouts need.</p>}
    </form>
  );
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
