"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";

import { useBranding } from "@/app/providers";
import type { BuyerOrder } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { buyerStage } from "@/lib/orders";
import { useBuyerOrder, useChooseOrderPassword, useResendInvite } from "@/lib/queries";

import { SuccessMark, useConfetti } from "./Celebrate";
import { FirmName } from "./FirmName";
import { PasswordFields } from "./PasswordReset";
import { buttonClass, ErrorText, Loading, Message, secondaryButtonClass } from "./ui";

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
    return <Loading />;
  }

  const paid = ["log-in", "choose-password", "invited", "get-invite"].includes(buyerStage(order.data).kind);
  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <section className="stagger flex w-full max-w-lg flex-col gap-5 rounded-2xl border border-border bg-panel p-7 shadow-raised">
        <FirmName size="lg" />
        {paid && <Welcome />}
        <dl className="flex flex-col gap-1.5 rounded-xl border border-border bg-background/40 p-4 text-sm">
          <div className="flex justify-between gap-3">
            <dt className="text-muted">Order {order.data.number}</dt>
            <dd>{order.data.challengeName}</dd>
          </div>
          <div className="flex justify-between gap-3">
            <dt className="text-muted">{paid ? "Paid" : "To pay"}</dt>
            <dd className="font-medium">
              {formatMoney(order.data.amount)} {order.data.currency}
            </dd>
          </div>
          {paid && (
            <a href={`/api/portal/orders/${order.data.id}/receipt.pdf?token=${encodeURIComponent(token)}`} className="mt-1 self-start text-accent hover:underline">
              Download the receipt (PDF)
            </a>
          )}
        </dl>
        <Stage order={order.data} token={token} />
        {paid && <NextSteps />}
      </section>
    </main>
  );
}

// The moment the payment went through: a check drawn in a ring, and a short burst of confetti.
function Welcome() {
  useConfetti(true);
  return (
    <div className="flex flex-col items-center gap-3 text-center">
      <SuccessMark />
      <h1 className="font-serif text-4xl tracking-tight">You&apos;re in.</h1>
    </div>
  );
}

/** What to do now that the challenge has started, in three steps. */
function NextSteps() {
  const steps = ["Choose your password, or log in, to open your account.", "Open the terminal from your account. It runs in the browser.", "Place your first trade. Each day you trade counts toward the trading days."];
  return (
    <ol aria-label="What happens next" className="flex flex-col gap-2.5 border-t border-border pt-4 text-sm">
      {steps.map((step, index) => (
        <li key={step} className="flex items-start gap-3">
          <span className="grid size-6 shrink-0 place-items-center rounded-full border border-accent/40 bg-accent/10 text-xs font-semibold text-accent">{index + 1}</span>
          <span className="text-muted">{step}</span>
        </li>
      ))}
    </ol>
  );
}

function Stage({ order, token }: { order: BuyerOrder; token: string }) {
  const { name } = useBranding();
  const stage = buyerStage(order);
  switch (stage.kind) {
    case "waiting":
      return (
        <div role="status" className="flex flex-col gap-3 text-sm">
          <p className="flex items-center gap-2.5">
            <span aria-hidden="true" className="size-2 animate-pulse-dot rounded-full bg-accent text-accent/40" />
            Waiting for the payment to be confirmed. This page updates by itself.
          </p>
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
