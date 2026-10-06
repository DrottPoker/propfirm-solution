"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";

import { formatMoney } from "@/lib/format";
import { useBuyerOrder, useTestPayment } from "@/lib/queries";

import { FirmName } from "./FirmName";
import { buttonClass, ErrorText, Loading, Message, secondaryButtonClass } from "./ui";

/** The payment page for firms that try the platform: paying takes no money, but the order and its account work as for real. */
export function TestCheckout({ orderId, token }: { orderId: string | null; token: string | null }) {
  const router = useRouter();
  const order = useBuyerOrder(orderId ?? "", token ?? "");
  const pay = useTestPayment(orderId ?? "", token ?? "");

  if (!orderId || !token || order.isError) {
    return <Message text="This payment cannot be found." />;
  }

  if (!order.data) {
    return <Loading />;
  }

  const back = `/orders/${orderId}?token=${encodeURIComponent(token)}`;
  const amount = `${formatMoney(order.data.amount)} ${order.data.currency}`;
  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <section className="flex w-full max-w-md animate-pop flex-col gap-4 rounded-2xl border border-border bg-panel p-6 shadow-raised">
        <FirmName size="lg" />
        <div className="flex flex-col gap-1">
          <h1 className="text-lg font-semibold">Test payment</h1>
          <p className="text-sm text-muted">No money is taken. The order and its challenge work as they would after a real payment.</p>
        </div>
        <p className="flex flex-wrap justify-between gap-2 rounded-xl border border-border bg-background/40 p-4 text-sm">
          <span>{order.data.challengeName}</span>
          <span className="font-medium">{amount}</span>
        </p>
        <p className="text-sm text-muted">For {order.data.email}</p>
        <ErrorText error={pay.error} />
        <div className="flex gap-3">
          <button
            type="button"
            disabled={pay.isPending || order.data.status !== "Pending"}
            onClick={() => pay.mutate(undefined, { onSuccess: () => router.replace(back) })}
            className={buttonClass}
          >
            {pay.isPending ? "Paying..." : `Pay ${amount}`}
          </button>
          <Link href="/buy" className={secondaryButtonClass}>
            Cancel
          </Link>
        </div>
        {order.data.status !== "Pending" && (
          <Link href={back} className="text-sm text-accent hover:underline">
            See the order
          </Link>
        )}
      </section>
    </main>
  );
}
