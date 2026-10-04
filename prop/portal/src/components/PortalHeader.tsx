"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";

import { useBranding } from "@/app/providers";

import type { Me } from "@/lib/api/types";
import { useLogout, useShop, type Role } from "@/lib/queries";

import { FirmName } from "./FirmName";

/** The firm's name, and who is logged in. */
export function PortalHeader({ me, role }: { me: Me; role: Role }) {
  const logout = useLogout(role);
  const shop = useShop(role === "trader");
  const branding = useBranding();
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
            <Link href="/admin/orders" className="hover:text-accent">
              Orders
            </Link>
            <Link href="/admin/challenges" className="hover:text-accent">
              Challenges
            </Link>
            <Link href="/admin/team" className="hover:text-accent">
              Team
            </Link>
            <Link href="/admin/billing" className="hover:text-accent">
              Billing
            </Link>
            {branding.status !== "Live" && (
              <Link href="/admin/verification" className="hover:text-accent">
                Verification
              </Link>
            )}
            <Link href="/admin/settings" className="hover:text-accent">
              Settings
            </Link>
          </nav>
        </>
      )}
      {role === "trader" && shop.data?.open && (
        <Link href="/buy" className="text-accent hover:underline">
          Buy a challenge
        </Link>
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
