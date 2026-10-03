"use client";

import Image from "next/image";
import { useRouter } from "next/navigation";
import { useState } from "react";

import { LoginFailedError, useLogin } from "@/lib/queries";

import { useBranding } from "../providers";

export default function LoginPage() {
  const branding = useBranding();
  const router = useRouter();
  const login = useLogin();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    login.mutate({ email, password }, { onSuccess: () => router.replace("/") });
  };

  const error = login.error
    ? login.error instanceof LoginFailedError
      ? login.error.message
      : "Could not reach the trading service."
    : null;

  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <form onSubmit={submit} className="flex w-full max-w-sm flex-col gap-4 rounded-lg border border-border bg-panel p-6">
        <div className="flex items-center gap-3">
          {branding?.logoUrl && (
            <Image src={branding.logoUrl} alt="" width={96} height={28} unoptimized className="h-7 w-auto object-contain" />
          )}
          <h1 className="text-lg font-semibold">{branding?.displayName ?? "Trading terminal"}</h1>
        </div>

        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Email</span>
          <input
            type="email"
            autoComplete="username"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className="rounded border border-border bg-background px-3 py-2 outline-none focus:border-accent"
          />
        </label>

        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Password</span>
          <input
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className="rounded border border-border bg-background px-3 py-2 outline-none focus:border-accent"
          />
        </label>

        {error && (
          <p role="alert" className="text-sm text-loss">
            {error}
          </p>
        )}

        <button type="submit" disabled={login.isPending} className="rounded bg-accent py-2 font-medium text-white disabled:opacity-50">
          {login.isPending ? "Logging in..." : "Log in"}
        </button>
      </form>
    </main>
  );
}
