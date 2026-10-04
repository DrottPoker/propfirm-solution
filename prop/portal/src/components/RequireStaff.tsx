"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect } from "react";

import { useOps } from "@/app/providers";
import type { OpsMe } from "@/lib/api/types";
import { useOpsLogout, useOpsMe } from "@/lib/opsQueries";

import { Message } from "./ui";

/** Shows our admin view to our staff, and sends others to its login. */
export function RequireStaff({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const me = useOpsMe();

  useEffect(() => {
    if (me.data === null) {
      router.replace("/ops/login");
    }
  }, [me.data, router]);

  if (me.isError) {
    return <Message text="Our admin view cannot be reached right now. Try again shortly." />;
  }

  if (!me.data) {
    return <Message text="Loading..." />;
  }

  return (
    <>
      <OpsHeader me={me.data} />
      {children}
    </>
  );
}

function OpsHeader({ me }: { me: OpsMe }) {
  const ops = useOps();
  const logout = useOpsLogout();
  const router = useRouter();
  return (
    <header className="flex flex-wrap items-center gap-x-6 gap-y-2 border-b border-border bg-panel px-6 py-3 text-sm">
      <Link href="/ops" className="font-semibold">
        {ops.name}
      </Link>
      <span className="rounded bg-accent/20 px-2 py-0.5 text-accent">Staff</span>
      <nav aria-label="Staff" className="flex gap-4">
        <Link href="/ops" className="hover:text-accent">
          Firms
        </Link>
      </nav>
      <span className="ml-auto flex items-center gap-4 text-muted">
        {me.email}
        <button
          type="button"
          className="hover:text-foreground"
          disabled={logout.isPending}
          onClick={() => logout.mutate(undefined, { onSuccess: () => router.replace("/ops/login") })}
        >
          Log out
        </button>
      </span>
    </header>
  );
}
