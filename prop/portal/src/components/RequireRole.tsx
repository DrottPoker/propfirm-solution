"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";

import { loginWithNext } from "@/lib/next";
import { useMe, type Role } from "@/lib/queries";

import { AdminShell } from "./AdminShell";
import { passwordPaths } from "./PasswordReset";
import { PortalHeader } from "./PortalHeader";
import { Loading, Message } from "./ui";

/**
 * Shows the page to someone logged in with the role, and sends others to the role's login, which returns to the page.
 * Traders get the portal's header above the page, and administrators the admin panel's menu beside it.
 */
export function RequireRole({ role, children }: { role: Role; children: React.ReactNode }) {
  const router = useRouter();
  const me = useMe(role);

  useEffect(() => {
    if (me.data === null) {
      const paths = passwordPaths[role];
      router.replace(loginWithNext(paths.login, paths.home, window.location.pathname + window.location.search));
    }
  }, [me.data, role, router]);

  if (me.isError) {
    return <Message text="The portal cannot be reached right now. Try again shortly." />;
  }

  if (!me.data) {
    return <Loading />;
  }

  return role === "admin" ? (
    <AdminShell me={me.data}>{children}</AdminShell>
  ) : (
    <>
      <PortalHeader me={me.data} />
      {children}
    </>
  );
}
