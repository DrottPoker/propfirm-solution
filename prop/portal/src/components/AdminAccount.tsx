"use client";

import Link from "next/link";
import { useState } from "react";

import type { Account } from "@/lib/api/types";
import { canCancel, kindOf, kindsOf } from "@/lib/challenge";
import { formatDateTime } from "@/lib/format";
import { useAccountCommand, useFirmAccount, useHistory, useInvite } from "@/lib/queries";

import { AccountOverview } from "./AccountOverview";
import { buttonClass, ErrorText, fieldClass, Message, Panel, secondaryButtonClass } from "./ui";

/** One of the firm's accounts: its figures, what the firm can do with it and its full history. */
export function AdminAccount({ accountId }: { accountId: string }) {
  const details = useFirmAccount(accountId);

  if (details.isError) {
    return <Message text={details.error.message} />;
  }

  if (!details.data) {
    return <Message text="Loading..." />;
  }

  const account = details.data.account;
  return (
    <main className="mx-auto flex w-full max-w-5xl flex-col gap-6 p-6">
      <Link href="/admin" className="text-sm text-muted hover:text-foreground">
        ← All accounts
      </Link>
      <AccountOverview details={details.data} actions={<span className="text-sm text-muted">{account.email}</span>} />
      <div className="grid gap-6 md:grid-cols-2">
        <TraderAccess account={account} />
        <AccountCommands account={account} />
      </div>
      <History accountId={accountId} />
    </main>
  );
}

/** An invitation for the trader to choose a password, for the firm to send. The link is shown only here. */
function TraderAccess({ account }: { account: Account }) {
  const invite = useInvite(account.id);

  return (
    <Panel title="Portal access">
      <p className="text-sm text-muted">
        Send {account.email} an invitation to choose a password for the portal, or a new password if it is forgotten. It works once,
        within 7 days, and a new one replaces it.
      </p>
      <button type="button" disabled={invite.isPending} onClick={() => invite.mutate()} className={secondaryButtonClass}>
        {invite.isPending ? "Creating..." : "Create invitation link"}
      </button>
      {invite.data && (
        <div className="flex flex-col gap-2 text-sm">
          <label className="flex flex-col gap-1">
            <span className="text-muted">Invitation link, valid until {formatDateTime(invite.data.expiresAt)}</span>
            <input readOnly value={invite.data.url} onFocus={(e) => e.target.select()} className={`${fieldClass} font-mono text-xs`} />
          </label>
          <button type="button" onClick={() => void navigator.clipboard.writeText(invite.data.url)} className={secondaryButtonClass}>
            Copy link
          </button>
        </div>
      )}
      <ErrorText error={invite.error} />
    </Panel>
  );
}

function AccountCommands({ account }: { account: Account }) {
  const command = useAccountCommand(account.id);
  const [reason, setReason] = useState("");

  const cancel = (event: React.FormEvent) => {
    event.preventDefault();
    if (window.confirm(`Cancel account #${account.number}? Its trading account is closed and this cannot be undone.`)) {
      command.mutate({ kind: "cancel", reason });
    }
  };

  return (
    <Panel title="Decisions">
      {account.status === "AwaitingFunding" && (
        <div className="flex flex-col gap-2">
          <p className="text-sm text-muted">The trader passed every evaluation stage. Approve when your checks, such as KYC, are done.</p>
          <button type="button" disabled={command.isPending} onClick={() => command.mutate({ kind: "approve-funding" })} className={buttonClass}>
            Approve funded account
          </button>
        </div>
      )}
      {canCancel(account) ? (
        <form onSubmit={cancel} className="flex flex-col gap-2">
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-muted">Reason for cancelling (optional)</span>
            <input value={reason} onChange={(e) => setReason(e.target.value)} className={fieldClass} />
          </label>
          <button type="submit" disabled={command.isPending} className={`${secondaryButtonClass} text-loss`}>
            Cancel account
          </button>
        </form>
      ) : (
        <p className="text-sm text-muted">The account has ended. Nothing more can be decided.</p>
      )}
      <ErrorText error={command.error} />
    </Panel>
  );
}

/** Every input and what the rule engine decided: the audit trail, including the evidence of a breach. */
function History({ accountId }: { accountId: string }) {
  const history = useHistory(accountId);

  return (
    <Panel title="History">
      <ErrorText error={history.error} />
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="text-left text-muted">
            <tr>
              <th className="py-2 font-normal">Step</th>
              <th className="py-2 font-normal">Time</th>
              <th className="py-2 font-normal">Input</th>
              <th className="py-2 font-normal">Decision</th>
              <th className="py-2 font-normal">Evidence</th>
            </tr>
          </thead>
          <tbody>
            {(history.data ?? []).map((step) => (
              <tr key={step.step} className="border-t border-border align-top">
                <td className="py-2 font-mono">{step.step}</td>
                <td className="py-2 text-muted">{formatDateTime(step.recordedAt)}</td>
                <td className="py-2">{kindOf(step.input)}</td>
                <td className="py-2">{kindsOf(step.outputs).join(", ") || "-"}</td>
                <td className="py-2 text-muted">{step.sourceEvent ? kindOf(step.sourceEvent) : "-"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Panel>
  );
}
