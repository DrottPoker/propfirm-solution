"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";

import { useMe, type Role } from "@/lib/queries";

import { PortalHeader } from "./PortalHeader";
import { Message } from "./ui";

const loginOf: Record<Role, string> = { trader: "/login", admin: "/admin/login" };

/** Shows the page to someone logged in with the role, and sends others to the role's login. */
export function RequireRole({ role, children }: { role: Role; children: React.ReactNode }) {
  const router = useRouter();
  const me = useMe(role);

  useEffect(() => {
    if (me.data === null) {
      router.replace(loginOf[role]);
    }
  }, [me.data, role, router]);

  if (me.isError) {
    return <Message text="The portal cannot be reached right now. Try again shortly." />;
  }

  if (!me.data) {
    return <Message text="Loading..." />;
  }

  return (
    <>
      <PortalHeader me={me.data} role={role} />
      {children}
    </>
  );
}
