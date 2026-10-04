"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { useLogin, useShop, type Role } from "@/lib/queries";

import { FirmName } from "./FirmName";
import { passwordPaths } from "./PasswordReset";
import { buttonClass, ErrorText, fieldClass } from "./ui";

/** Login for the firm's traders, or for its administrators. Goes to the next page afterwards, or to the role's home. */
export function LoginForm({ role, next = null }: { role: Role; next?: string | null }) {
  const router = useRouter();
  const login = useLogin(role);
  const shop = useShop(role === "trader");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const passwordId = useId();

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    login.mutate({ email, password }, { onSuccess: () => router.replace(next ?? passwordPaths[role].home) });
  };

  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <form onSubmit={submit} className="flex w-full max-w-sm flex-col gap-4 rounded-lg border border-border bg-panel p-6">
        <div className="flex flex-col gap-1">
          <FirmName size="lg" />
          <h1 className="text-sm text-muted">{role === "admin" ? "Admin login" : "Trader login"}</h1>
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
            <Link href={passwordPaths[role].forgot} className="text-xs text-accent hover:underline">
              Forgot password?
            </Link>
          </span>
          <input
            id={passwordId}
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className={fieldClass}
          />
        </div>

        <ErrorText error={login.error} />

        <button type="submit" disabled={login.isPending} className={buttonClass}>
          {login.isPending ? "Logging in..." : "Log in"}
        </button>

        {role === "trader" &&
          (shop.data?.open ? (
            <p className="text-xs text-muted">
              New here?{" "}
              <Link href="/buy" className="text-accent hover:underline">
                Buy a challenge
              </Link>
              , and choose your password right after paying.
            </p>
          ) : (
            <p className="text-xs text-muted">New here? Your firm emails you an invitation to choose a password.</p>
          ))}
        {role === "admin" && (
          <Link href="/login" className="text-xs text-muted hover:text-foreground">
            Trader login
          </Link>
        )}
      </form>
    </main>
  );
}
