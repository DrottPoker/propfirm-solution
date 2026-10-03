"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef } from "react";

import { useWelcome } from "@/lib/queries";

import { FirmName } from "./FirmName";
import { ErrorText } from "./ui";

/** Right after signing up: the one-time link logs the administrator in on the firm's own address. */
export function AdminWelcome({ token }: { token: string | null }) {
  const router = useRouter();
  const welcome = useWelcome();
  const { mutate } = welcome;

  // The link works once, so it is used once even when React runs the effect twice in development.
  const used = useRef(false);
  useEffect(() => {
    if (token && !used.current) {
      used.current = true;
      mutate(token, { onSuccess: () => router.replace("/admin") });
    }
  }, [token, mutate, router]);

  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <section className="flex w-full max-w-sm flex-col gap-4 rounded-lg border border-border bg-panel p-6">
        <FirmName size="lg" />
        {!token || welcome.isError ? (
          <>
            <ErrorText error={welcome.error ?? new Error("This link has no login in it.")} />
            <Link href="/admin/login" className="text-sm text-accent hover:underline">
              Log in with your email and password
            </Link>
          </>
        ) : (
          <p className="text-sm text-muted">Opening your admin panel...</p>
        )}
      </section>
    </main>
  );
}
