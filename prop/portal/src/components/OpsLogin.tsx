"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { useOps } from "@/app/providers";
import { useOpsLogin } from "@/lib/opsQueries";

import { PasswordInput } from "./PasswordInput";
import { passwordPaths } from "./PasswordReset";
import { buttonClass, ErrorText, fieldClass } from "./ui";

/** Login for our own staff. */
export function OpsLogin() {
  const ops = useOps();
  const router = useRouter();
  const login = useOpsLogin();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const passwordId = useId();

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

        <div className="flex flex-col gap-1 text-sm">
          <span className="flex items-center justify-between">
            <label htmlFor={passwordId} className="text-muted">
              Password
            </label>
            <Link href={passwordPaths.staff.forgot} className="text-xs text-accent hover:underline">
              Forgot password?
            </Link>
          </span>
          <PasswordInput id={passwordId} autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
        </div>

        <ErrorText error={login.error} />

        <button type="submit" disabled={login.isPending} className={buttonClass}>
          {login.isPending ? "Logging in..." : "Log in"}
        </button>
      </form>
    </main>
  );
}
