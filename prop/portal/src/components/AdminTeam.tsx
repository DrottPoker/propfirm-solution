"use client";

import { useState } from "react";

import { formatDateTime } from "@/lib/format";
import { useAdmins, useInviteAdmin, useRemoveAdmin, useWithdrawAdminInvite } from "@/lib/queries";

import { ConfirmDialog } from "./Dialog";
import { AdminPage, buttonClass, ErrorText, fieldClass, PageHeader, Panel } from "./ui";

/** The firm's administrators: who they are, invitations that wait, sent again or taken back, and removal. */
export function AdminTeam() {
  const admins = useAdmins();
  const remove = useRemoveAdmin();
  const resend = useInviteAdmin();
  const withdraw = useWithdrawAdminInvite();
  const [removing, setRemoving] = useState<{ id: string; email: string } | null>(null);

  return (
    <AdminPage narrow>
      <PageHeader title="Team" description="The people who run your firm in this admin panel. Each one logs in with their own email and password." />
      <InviteAdmin />

      <Panel title="Administrators">
        <ErrorText error={admins.error ?? resend.error ?? withdraw.error} />
        <ul className="flex flex-col">
          {(admins.data?.admins ?? []).map((admin) => (
            <li key={admin.id} className="flex flex-wrap items-center gap-3 border-t border-border py-2 text-sm first:border-t-0">
              <span className="font-medium">{admin.email}</span>
              {admin.isYou && <span className="rounded bg-accent/20 px-2 py-0.5 text-xs text-accent">You</span>}
              <span className="text-muted">since {formatDateTime(admin.createdAt)}</span>
              {!admin.isYou && (
                <button type="button" onClick={() => setRemoving({ id: admin.id, email: admin.email })} className="ml-auto text-muted hover:text-loss">
                  Remove
                </button>
              )}
            </li>
          ))}
        </ul>

        {admins.data && admins.data.invites.length > 0 && (
          <div className="flex flex-col gap-2">
            <h3 className="text-sm text-muted">Invited, waiting for a password</h3>
            <ul className="flex flex-col text-sm">
              {admins.data.invites.map((invite) => (
                <li key={invite.email} className="flex flex-wrap items-center gap-3 border-t border-border py-2 first:border-t-0">
                  <span>
                    {invite.email} <span className="text-muted">(the link works until {formatDateTime(invite.expiresAt)})</span>
                  </span>
                  <span className="ml-auto flex gap-3">
                    <button
                      type="button"
                      disabled={resend.isPending}
                      onClick={() => resend.mutate(invite.email)}
                      className="text-accent hover:underline disabled:opacity-50"
                    >
                      Send again
                    </button>
                    <button
                      type="button"
                      disabled={withdraw.isPending}
                      onClick={() => withdraw.mutate(invite.email)}
                      className="text-muted hover:text-loss disabled:opacity-50"
                    >
                      Take back
                    </button>
                  </span>
                </li>
              ))}
            </ul>
            {resend.isSuccess && (
              <p role="status" className="text-xs text-profit">
                Sent again to {resend.data.email}. The old link no longer works.
              </p>
            )}
          </div>
        )}
      </Panel>

      <ConfirmDialog
        open={removing !== null}
        onClose={() => setRemoving(null)}
        onConfirm={() => removing && remove.mutate(removing.id, { onSuccess: () => setRemoving(null) })}
        title={`Remove ${removing?.email ?? ""}?`}
        description="They are logged out at once and can no longer log in."
        confirmLabel="Remove"
        pendingLabel="Removing..."
        pending={remove.isPending}
        danger
      >
        <ErrorText error={remove.error} />
      </ConfirmDialog>
    </AdminPage>
  );
}

function InviteAdmin() {
  const invite = useInviteAdmin();
  const [email, setEmail] = useState("");

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    invite.mutate(email.trim(), { onSuccess: () => setEmail("") });
  };

  return (
    <Panel title="Invite an administrator">
      <form onSubmit={submit} className="flex flex-wrap items-end gap-3">
        <label className="flex min-w-60 flex-1 flex-col gap-1 text-sm">
          <span className="text-muted">Email</span>
          <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} className={fieldClass} />
        </label>
        <button type="submit" disabled={invite.isPending} className={buttonClass}>
          {invite.isPending ? "Sending..." : "Send invitation"}
        </button>
      </form>
      <ErrorText error={invite.error} />
      {invite.isSuccess && (
        <p role="status" className="text-sm text-profit">
          Invitation sent to {invite.data.email}. The link works for 7 days.
        </p>
      )}
      <p className="text-xs text-muted">Administrators see all the firm&apos;s accounts and payouts, and can change its settings.</p>
    </Panel>
  );
}
