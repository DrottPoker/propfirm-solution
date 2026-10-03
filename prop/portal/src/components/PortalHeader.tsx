"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";

import type { Me } from "@/lib/api/types";
import { useLogout, type Role } from "@/lib/queries";

import { FirmName } from "./FirmName";

/** The firm's name, and who is logged in. */
export function PortalHeader({ me, role }: { me: Me; role: Role }) {
  const logout = useLogout(role);
  const router = useRouter();
  const loginPage = role === "admin" ? "/admin/login" : "/login";

  return (
    <header className="flex flex-wrap items-center gap-x-6 gap-y-2 border-b border-border bg-panel px-6 py-3 text-sm">
      <Link href={role === "admin" ? "/admin" : "/"}>
        <FirmName />
      </Link>
      {role === "admin" && (
        <>
          <span className="rounded bg-accent/20 px-2 py-0.5 text-accent">Admin</span>
          <nav aria-label="Admin" className="flex gap-4">
            <Link href="/admin" className="hover:text-accent">
              Accounts
            </Link>
            <Link href="/admin/payouts" className="hover:text-accent">
              Payouts
            </Link>
          </nav>
        </>
      )}
      <span className="ml-auto flex items-center gap-4 text-muted">
        {me.email}
        <button
          type="button"
          className="hover:text-foreground"
          disabled={logout.isPending}
          onClick={() => logout.mutate(undefined, { onSuccess: () => router.replace(loginPage) })}
        >
          Log out
        </button>
      </span>
    </header>
  );
}
