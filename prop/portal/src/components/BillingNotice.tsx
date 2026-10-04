"use client";

import Link from "next/link";

import { billingNotice } from "@/lib/billing";
import { useBilling } from "@/lib/queries";

const tones = {
  loss: "border-loss/40 bg-loss/10 text-loss",
  warning: "border-warning/40 bg-warning/10 text-warning",
};

/** Tells the firm's administrators on every admin page when the firm is suspended, a month is unpaid or the slots run out. */
export function BillingNotice() {
  const billing = useBilling();
  const notice = billing.data ? billingNotice(billing.data) : null;
  if (!notice) {
    return null;
  }

  return (
    <div role="status" className={`border-b px-6 py-2 text-center text-sm ${tones[notice.tone]}`}>
      {notice.text}{" "}
      {notice.billingLink !== false && (
        <Link href="/admin/billing" className="font-medium underline">
          Billing
        </Link>
      )}
    </div>
  );
}
