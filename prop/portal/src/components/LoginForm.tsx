"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";

import { useLogin, useShop, type Role } from "@/lib/queries";

import { FirmName } from "./FirmName";
import { buttonClass, ErrorText, fieldClass } from "./ui";

/** Login for the firm's traders, or for its administrators. */
export function LoginForm({ role }: { role: Role }) {
  const router = useRouter();
  const login = useLogin(role);
  const shop = useShop(role === "trader");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    login.mutate({ email, password }, { onSuccess: () => router.replace(role === "admin" ? "/admin" : "/") });
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

        {role === "trader" &&
          (shop.data?.open ? (
            <p className="text-xs text-muted">
              New here?{" "}
              <Link href="/buy" className="text-accent hover:underline">
                Buy a challenge
              </Link>
              , and you get an email to choose your password.
            </p>
          ) : (
            <p className="text-xs text-muted">New here? Your firm emails you an invitation to choose a password.</p>
          ))}
        <Link href={role === "admin" ? "/login" : "/admin/login"} className="text-xs text-muted hover:text-foreground">
          {role === "admin" ? "Trader login" : "Admin login"}
        </Link>
      </form>
    </main>
  );
}
