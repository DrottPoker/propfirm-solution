"use client";

import { useState } from "react";

import { formatDateTime } from "@/lib/format";
import { useAdmins, useInviteAdmin, useRemoveAdmin } from "@/lib/queries";

import { AdminPage, buttonClass, ErrorText, fieldClass, PageHeader, Panel } from "./ui";

/** The firm's administrators: who they are, invitations that wait, and removal. */
export function AdminTeam() {
  const admins = useAdmins();
  const remove = useRemoveAdmin();

  const confirmRemove = (adminId: string, email: string) => {
    if (window.confirm(`Remove ${email}? They are logged out at once and can no longer log in.`)) {
      remove.mutate(adminId);
    }
  };

  return (
    <AdminPage narrow>
      <PageHeader title="Team" description="The people who run your firm in this admin panel. Each one logs in with their own email and password." />
      <InviteAdmin />

      <Panel title="Administrators">
        <ErrorText error={admins.error ?? remove.error} />
        <ul className="flex flex-col">
          {(admins.data?.admins ?? []).map((admin) => (
            <li key={admin.id} className="flex flex-wrap items-center gap-3 border-t border-border py-2 text-sm first:border-t-0">
              <span className="font-medium">{admin.email}</span>
              {admin.isYou && <span className="rounded bg-accent/20 px-2 py-0.5 text-xs text-accent">You</span>}
              <span className="text-muted">since {formatDateTime(admin.createdAt)}</span>
              {!admin.isYou && (
                <button
                  type="button"
                  onClick={() => confirmRemove(admin.id, admin.email)}
                  disabled={remove.isPending}
                  className="ml-auto text-muted hover:text-loss"
                >
                  Remove
                </button>
              )}
            </li>
          ))}
        </ul>

        {admins.data && admins.data.invites.length > 0 && (
          <div className="flex flex-col gap-2">
            <h3 className="text-sm text-muted">Invited, waiting for a password</h3>
            <ul className="flex flex-col gap-1 text-sm">
              {admins.data.invites.map((invite) => (
                <li key={invite.email}>
                  {invite.email} <span className="text-muted">(the link works until {formatDateTime(invite.expiresAt)})</span>
                </li>
              ))}
            </ul>
          </div>
        )}
      </Panel>
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
