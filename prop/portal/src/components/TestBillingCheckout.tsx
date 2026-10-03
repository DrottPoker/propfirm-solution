"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";

import { formatMoney } from "@/lib/format";
import { useCompleteTestBillingCheckout, useTestBillingCheckout } from "@/lib/queries";

import { buttonClass, ErrorText, Message, secondaryButtonClass } from "./ui";

/**
 * The page where a firm pays the platform or saves a card while payments to the platform are test payments. No
 * money is taken. A test card that declines shows what happens when a real card is declined.
 */
export function TestBillingCheckout({ checkoutId }: { checkoutId: string }) {
  const router = useRouter();
  const checkout = useTestBillingCheckout(checkoutId);
  const complete = useCompleteTestBillingCheckout(checkoutId);

  if (checkout.isError) {
    return <Message text="This payment page cannot be found." />;
  }

  if (!checkout.data) {
    return <Message text="Loading..." />;
  }

  const { purpose, status, lines, amount, currency } = checkout.data;
  const payment = purpose === "Payment";
  const done = () => router.replace("/admin/billing?checkout=done");
  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <section className="flex w-full max-w-md flex-col gap-4 rounded-lg border border-warning/40 bg-panel p-6">
        <div className="flex flex-col gap-1">
          <h1 className="text-lg font-semibold">{payment ? "Test payment" : "Test card"}</h1>
          <p className="text-sm text-warning">
            {payment
              ? "No money is taken. Your firm's billing works as it would after a real payment."
              : "No card is saved. Later payments use the test card you choose here."}
          </p>
        </div>
        {payment && (
          <ul className="flex flex-col gap-1 text-sm">
            {lines.map((line) => (
              <li key={line.description} className="flex justify-between gap-3">
                <span>{line.description}</span>
                <span className="font-mono tabular-nums">{formatMoney(line.amount)}</span>
              </li>
            ))}
            <li className="flex justify-between gap-3 border-t border-border pt-1 font-medium">
              <span>Total</span>
              <span className="font-mono tabular-nums">
                {formatMoney(amount)} {currency}
              </span>
            </li>
          </ul>
        )}
        {status !== "Open" && <p className="text-sm text-muted">This page is no longer open.</p>}
        <ErrorText error={complete.error} />
        <div className="flex flex-wrap gap-3">
          <button type="button" disabled={status !== "Open" || complete.isPending} onClick={() => complete.mutate(false, { onSuccess: done })} className={buttonClass}>
            {payment ? `Pay ${formatMoney(amount)} ${currency}` : "Save a test card"}
          </button>
          <button type="button" disabled={status !== "Open" || complete.isPending} onClick={() => complete.mutate(true, { onSuccess: done })} className={secondaryButtonClass}>
            {payment ? "Try a card that declines" : "Save a card that declines"}
          </button>
        </div>
        <Link href="/admin/billing" className="text-sm text-muted hover:text-foreground">
          Back to billing
        </Link>
      </section>
    </main>
  );
}
