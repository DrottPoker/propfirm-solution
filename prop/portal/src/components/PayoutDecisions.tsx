"use client";

import { useId, useState } from "react";

import type { Payout } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { canApprove, canMarkPaid, canReject } from "@/lib/payouts";
import { usePayoutDecision } from "@/lib/queries";

import { ConfirmDialog, Modal } from "./Dialog";
import { AlertIcon } from "./icons";
import { PayTo } from "./PayTo";
import { buttonClass, dangerButtonClass, ErrorText, fieldClass, secondaryButtonClass } from "./ui";

/**
 * The firm's decisions about a payout: approve it after its own checks of the trader, mark it as paid once the money is
 * sent, or reject it with a reason the trader sees. Marking as paid and rejecting ask first, in a dialog, and so does
 * approving when the firm has not ticked its checks of the trader. <code>traderChecked</code> is undefined when it is not known.
 */
export function PayoutDecisions({ payout, traderChecked }: { payout: Payout; traderChecked?: boolean }) {
  const decision = usePayoutDecision();
  const [dialog, setDialog] = useState<"approve" | "paid" | "reject" | null>(null);
  const approve = () => decision.mutate({ kind: "approve", payoutId: payout.id }, { onSuccess: () => setDialog(null) });

  if (!canApprove(payout) && !canMarkPaid(payout) && !canReject(payout)) {
    return null;
  }

  return (
    <div className="flex flex-col items-end gap-2">
      <div className="flex flex-wrap justify-end gap-2">
        {canApprove(payout) && (
          <button type="button" disabled={decision.isPending} onClick={() => (traderChecked === false ? setDialog("approve") : approve())} className={`${buttonClass} text-sm`}>
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
      <ConfirmDialog
        open={dialog === "approve"}
        onClose={() => setDialog(null)}
        onConfirm={approve}
        title="Approve without your checks of the trader?"
        description={`Account #${payout.accountNumber} of ${payout.email}. You have not ticked that you checked the trader's ID and address. Tick them on the account's trader card once you have.`}
        confirmLabel="Approve anyway"
        pendingLabel="Approving..."
        pending={decision.isPending}
      >
        <ErrorText error={decision.error} />
      </ConfirmDialog>
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
      description={`Account #${payout.accountNumber} of ${payout.email}. Do this once you have sent the money. The trader sees it as paid.`}
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
        <div className="flex flex-col gap-1.5 rounded border border-border p-3 text-sm">
          <span className="font-medium">Sent to</span>
          <PayTo method={payout.payTo} />
        </div>
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">
            Reference <span className="font-normal text-muted">(optional, the trader sees it)</span>
          </span>
          <input value={reference} onChange={(e) => setReference(e.target.value)} placeholder="For example a bank transfer id" className={fieldClass} />
        </label>
        <ErrorText error={decision.error} />
      </form>
    </Modal>
  );
}

// The firm chooses what happens to the profit: forfeited, for a broken rule, or back on the account, for example while
// the trader finishes the firm's checks.
function RejectDialog({ payout, onClose }: { payout: Payout; onClose: () => void }) {
  const decision = usePayoutDecision();
  const formId = useId();
  const [reason, setReason] = useState("");
  const [returnProfit, setReturnProfit] = useState<boolean | null>(null);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (returnProfit !== null) {
      decision.mutate({ kind: "reject", payoutId: payout.id, reason: reason.trim(), returnProfit }, { onSuccess: onClose });
    }
  };
  const profit = `${formatMoney(payout.profit)} ${payout.currency}`;

  return (
    <Modal
      open
      onClose={onClose}
      title={`Reject the payout of ${formatMoney(payout.amount)} ${payout.currency}?`}
      description={`Account #${payout.accountNumber} of ${payout.email}`}
      footer={
        <>
          <button type="button" onClick={onClose} className={secondaryButtonClass}>
            Keep it
          </button>
          <button type="submit" form={formId} disabled={decision.isPending || returnProfit === null} className={dangerButtonClass}>
            {decision.isPending ? "Rejecting..." : "Reject payout"}
          </button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} className="flex flex-col gap-4">
        <fieldset className="flex flex-col gap-2 text-sm">
          <legend className="mb-2 flex gap-2.5">
            <AlertIcon className="mt-0.5 size-4 shrink-0 text-warning" />
            <span>The profit of {profit} was taken off the trading account when the trader asked. What happens to it?</span>
          </legend>
          <label className="flex items-start gap-2.5 rounded-lg border border-border p-3 has-checked:border-accent">
            <input type="radio" name="profit" required checked={returnProfit === false} onChange={() => setReturnProfit(false)} className="mt-1" />
            <span className="flex flex-col gap-0.5">
              <span className="font-medium">Reject, the profit is forfeited</span>
              <span className="text-xs text-muted">For example for a broken rule. The account goes on from where it is now.</span>
            </span>
          </label>
          <label className="flex items-start gap-2.5 rounded-lg border border-border p-3 has-checked:border-accent">
            <input type="radio" name="profit" checked={returnProfit === true} onChange={() => setReturnProfit(true)} className="mt-1" />
            <span className="flex flex-col gap-0.5">
              <span className="font-medium">Reject, put the profit back</span>
              <span className="text-xs text-muted">
                {profit} goes back on the trading account, for example while the trader finishes your checks. The trader can ask again later.
              </span>
            </span>
          </label>
        </fieldset>
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">
            Reason <span className="font-normal text-muted">(the trader sees it)</span>
          </span>
          <textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={4} className={`${fieldClass} resize-y`} />
        </label>
        <ErrorText error={decision.error} />
      </form>
    </Modal>
  );
}
