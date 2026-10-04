"use client";

import type { PayoutMethod } from "@/lib/api/types";
import { payoutMethodKindLabels, payoutMethodLines } from "@/lib/payoutMethods";

import { CopyButton } from "./CopyButton";

/** Where the trader asked for the payout to be sent, with a copy button for what goes into the payment. */
export function PayTo({ method }: { method: PayoutMethod | null }) {
  if (!method) {
    return <span className="text-muted">Not given</span>;
  }

  return (
    <dl className="flex flex-col gap-1">
      <dt className="sr-only">Method</dt>
      <dd className="text-xs text-muted">{payoutMethodKindLabels[method.kind]}</dd>
      {payoutMethodLines(method).map((line) => (
        <div key={line.label} className="flex flex-col">
          <dt className="sr-only">{line.label}</dt>
          <dd className="flex items-center gap-1.5">
            <span className={`break-all ${line.copy ? "font-mono text-xs" : ""}`} title={line.label}>
              {line.value}
            </span>
            {line.copy && <CopyButton value={line.value} label={line.label} />}
          </dd>
        </div>
      ))}
    </dl>
  );
}
