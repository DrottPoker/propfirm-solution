"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";

import { useMe } from "@/lib/queries";

import { StaffShell } from "./StaffShell";
import { ErrorText, Loading } from "./ui";

/** Every page of the panel needs a staff member logged in. Anyone else is sent to log in. */
export function StaffFrame({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const me = useMe();

  useEffect(() => {
    if (me.data === null) {
      router.replace("/login");
    }
  }, [me.data, router]);

  if (me.isError) {
    return (
      <main className="flex flex-1 items-center justify-center p-8">
        <ErrorText error={me.error} />
      </main>
    );
  }

  if (!me.data) {
    return (
      <main className="mx-auto w-full max-w-6xl px-4 py-8 sm:px-8">
        <Loading label="Loading the staff panel" />
      </main>
    );
  }

  return <StaffShell me={me.data}>{children}</StaffShell>;
}
