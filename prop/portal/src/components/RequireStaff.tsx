"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";

import { useOpsMe } from "@/lib/opsQueries";

import { OpsShell } from "./OpsShell";
import { Message } from "./ui";

/** Shows our admin view to our staff, inside its menu, and sends others to its login. */
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

  return <OpsShell me={me.data}>{children}</OpsShell>;
}
