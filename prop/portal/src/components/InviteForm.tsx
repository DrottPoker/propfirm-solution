"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import { useAcceptAdminInvite, useAcceptInvite, type Role } from "@/lib/queries";

import { FirmName } from "./FirmName";
import { buttonClass, ErrorText, fieldClass } from "./ui";

/** A trader, or a new administrator, opens the invitation and chooses a password for the portal. */
export function InviteForm({ token, role = "trader" }: { token: string | null; role?: Role }) {
  const router = useRouter();
  const acceptAsTrader = useAcceptInvite();
  const acceptAsAdmin = useAcceptAdminInvite();
  const accept = role === "admin" ? acceptAsAdmin : acceptAsTrader;
  const [password, setPassword] = useState("");
  const [repeated, setRepeated] = useState("");
  const [mismatch, setMismatch] = useState(false);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    setMismatch(password !== repeated);
    if (token && password === repeated) {
      accept.mutate({ token, password }, { onSuccess: () => router.replace(role === "admin" ? "/admin" : "/") });
    }
  };

  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <form onSubmit={submit} className="flex w-full max-w-sm flex-col gap-4 rounded-lg border border-border bg-panel p-6">
        <div className="flex flex-col gap-1">
          <FirmName size="lg" />
          <h1 className="text-sm text-muted">{role === "admin" ? "Choose a password to administer the firm" : "Choose a password for your account"}</h1>
        </div>

        {token ? (
          <>
            <label className="flex flex-col gap-1 text-sm">
              <span className="text-muted">Password</span>
              <input
                type="password"
                autoComplete="new-password"
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className={fieldClass}
              />
            </label>

            <label className="flex flex-col gap-1 text-sm">
              <span className="text-muted">Repeat password</span>
              <input
                type="password"
                autoComplete="new-password"
                required
                value={repeated}
                onChange={(e) => setRepeated(e.target.value)}
                className={fieldClass}
              />
            </label>

            <ErrorText error={mismatch ? new Error("The passwords are not the same.") : accept.error} />

            <button type="submit" disabled={accept.isPending} className={buttonClass}>
              {accept.isPending ? "Saving..." : "Save password and continue"}
            </button>
          </>
        ) : (
          <p role="alert" className="text-sm text-loss">
            This link has no invitation in it. Open the link in the email again.
          </p>
        )}
      </form>
    </main>
  );
}
