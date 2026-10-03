"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef } from "react";

import { LinkLoginFailedError, LoginFailedError, useLinkLogin } from "@/lib/queries";

/** Uses the link once, then opens the terminal without the token in the address. */
export function LinkLogin({ token, accountId }: { token: string | null; accountId: string | null }) {
  const router = useRouter();
  const login = useLinkLogin();
  const used = useRef(false);

  useEffect(() => {
    // React runs effects twice in development, and a link only works once.
    if (used.current || !token) {
      return;
    }

    used.current = true;
    login.mutate(token, {
      onSuccess: () => router.replace(accountId ? `/?account=${encodeURIComponent(accountId)}` : "/"),
    });
  }, [token, accountId, login, router]);

  const error = !token
    ? new LinkLoginFailedError().message
    : login.error instanceof LinkLoginFailedError || login.error instanceof LoginFailedError
      ? login.error.message
      : login.error
        ? "Could not reach the trading service."
        : null;

  return (
    <main className="flex flex-1 items-center justify-center p-6">
      {error ? (
        <div className="flex max-w-sm flex-col gap-3 rounded-xl border border-border bg-panel p-6 text-sm">
          <p role="alert">{error}</p>
          <Link href="/login" className="text-accent">
            Log in
          </Link>
        </div>
      ) : (
        <p className="text-muted">Logging in...</p>
      )}
    </main>
  );
}
