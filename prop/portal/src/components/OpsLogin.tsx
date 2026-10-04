"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import { useOps } from "@/app/providers";
import { useOpsLogin } from "@/lib/opsQueries";

import { buttonClass, ErrorText, fieldClass } from "./ui";

/** Login for our own staff. */
export function OpsLogin() {
  const ops = useOps();
  const router = useRouter();
  const login = useOpsLogin();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    login.mutate({ email, password }, { onSuccess: () => router.replace("/ops") });
  };

  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <form onSubmit={submit} className="flex w-full max-w-sm flex-col gap-4 rounded-lg border border-border bg-panel p-6">
        <div className="flex flex-col gap-1">
          <span className="text-sm text-muted">{ops.name}</span>
          <h1 className="text-xl font-semibold">Staff login</h1>
        </div>

        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Email</span>
          <input type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} className={fieldClass} />
        </label>

        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Password</span>
          <input
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className={fieldClass}
          />
        </label>

        <ErrorText error={login.error} />

        <button type="submit" disabled={login.isPending} className={buttonClass}>
          {login.isPending ? "Logging in..." : "Log in"}
        </button>
      </form>
    </main>
  );
}
