"use client";

import { useId, useState } from "react";

import type { Payout } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { canApprove, canMarkPaid, canReject } from "@/lib/payouts";
import { usePayoutDecision } from "@/lib/queries";

import { Modal } from "./Dialog";
import { AlertIcon } from "./icons";
import { buttonClass, dangerButtonClass, ErrorText, fieldClass, secondaryButtonClass } from "./ui";

/**
 * The firm's decisions about a payout: approve it after its own checks, such as KYC, mark it as paid once the money is
 * sent, or reject it with a reason the trader sees. Marking as paid and rejecting ask first, in a dialog.
 */
export function PayoutDecisions({ payout }: { payout: Payout }) {
  const decision = usePayoutDecision();
  const [dialog, setDialog] = useState<"paid" | "reject" | null>(null);

  if (!canApprove(payout) && !canMarkPaid(payout) && !canReject(payout)) {
    return null;
  }

  return (
    <div className="flex flex-col items-end gap-2">
      <div className="flex flex-wrap justify-end gap-2">
        {canApprove(payout) && (
          <button type="button" disabled={decision.isPending} onClick={() => decision.mutate({ kind: "approve", payoutId: payout.id })} className={`${buttonClass} text-sm`}>
            Approve
          </button>
        )}
        {canMarkPaid(payout) && (
          <button type="button" disabled={decision.isPending} onClick={() => setDialog("paid")} className={`${buttonClass} text-sm`}>
            Mark as paid
          </button>
        )}
        {canReject(payout) && (
          <button type="button" disabled={decision.isPending} onClick={() => setDialog("reject")} className={`${secondaryButtonClass} text-sm text-loss`}>
            Reject
          </button>
        )}
      </div>
      {dialog === null && <ErrorText error={decision.error} />}
      {dialog === "paid" && <MarkPaidDialog payout={payout} onClose={() => setDialog(null)} />}
      {dialog === "reject" && <RejectDialog payout={payout} onClose={() => setDialog(null)} />}
    </div>
  );
}

function MarkPaidDialog({ payout, onClose }: { payout: Payout; onClose: () => void }) {
  const decision = usePayoutDecision();
  const formId = useId();
  const [reference, setReference] = useState("");

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    decision.mutate({ kind: "mark-paid", payoutId: payout.id, reference: reference.trim() }, { onSuccess: onClose });
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={`Mark the payout of ${formatMoney(payout.amount)} ${payout.currency} as paid?`}
      description={`${payout.email} · #${payout.accountNumber}. Do this once you have sent the money. The trader sees it as paid.`}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondaryButtonClass}>
            Cancel
          </button>
          <button type="submit" form={formId} disabled={decision.isPending} className={buttonClass}>
            {decision.isPending ? "Saving..." : "Mark as paid"}
          </button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} className="flex flex-col gap-3">
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">
            Your reference for the payment <span className="font-normal text-muted">(optional)</span>
          </span>
          <input value={reference} onChange={(e) => setReference(e.target.value)} placeholder="For example a bank transfer id" className={fieldClass} />
        </label>
        <ErrorText error={decision.error} />
      </form>
    </Modal>
  );
}

function RejectDialog({ payout, onClose }: { payout: Payout; onClose: () => void }) {
  const decision = usePayoutDecision();
  const formId = useId();
  const [reason, setReason] = useState("");

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    decision.mutate({ kind: "reject", payoutId: payout.id, reason: reason.trim() }, { onSuccess: onClose });
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={`Reject the payout of ${formatMoney(payout.amount)} ${payout.currency}?`}
      description={`${payout.email} · #${payout.accountNumber}`}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondaryButtonClass}>
            Keep it
          </button>
          <button type="submit" form={formId} disabled={decision.isPending} className={dangerButtonClass}>
            {decision.isPending ? "Rejecting..." : "Reject payout"}
          </button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} className="flex flex-col gap-4">
        <p className="flex gap-2.5 rounded-lg border border-warning/40 bg-warning/10 px-3.5 py-3 text-sm">
          <AlertIcon className="mt-0.5 size-4 shrink-0 text-warning" />
          <span>
            The profit of {formatMoney(payout.profit)} {payout.currency} was taken off the trading account when the trader asked. Rejecting does not put it back.
          </span>
        </p>
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">
            Reason <span className="font-normal text-muted">· the trader sees this</span>
          </span>
          <textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={4} className={`${fieldClass} resize-y`} />
        </label>
        <ErrorText error={decision.error} />
      </form>
    </Modal>
  );
}
