"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";

import { environmentName, productName } from "@/lib/config";
import { useLogin, useMe } from "@/lib/queries";

import { KronantMark } from "./icons";
import { buttonClass, ErrorText, fieldClass } from "./ui";

/** Our staff log in with their own email and password, which reach only the staff panel. */
export function LoginForm() {
  const router = useRouter();
  const me = useMe();
  const login = useLogin();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  // Someone who is already logged in goes straight to the overview.
  useEffect(() => {
    if (me.data) {
      router.replace("/");
    }
  }, [me.data, router]);

  return (
    <main className="flex flex-1 items-center justify-center px-4 py-12">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          login.mutate({ email, password }, { onSuccess: () => router.replace("/") });
        }}
        className="flex w-full max-w-sm flex-col gap-5 rounded-2xl border border-border bg-panel p-7 shadow-float"
      >
        <div className="flex items-center gap-3">
          <KronantMark className="size-9" />
          <div className="flex flex-col leading-tight">
            <span className="text-lg font-semibold">{productName}</span>
            <span className="text-sm text-accent">Staff, {environmentName}</span>
          </div>
        </div>
        <h1 className="font-serif text-3xl">Log in</h1>
        <label className="flex flex-col gap-1.5">
          <span className="font-medium">Email</span>
          <input type="email" autoComplete="username" required value={email} onChange={(event) => setEmail(event.target.value)} className={fieldClass} />
        </label>
        <label className="flex flex-col gap-1.5">
          <span className="font-medium">Password</span>
          <input
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            className={fieldClass}
          />
        </label>
        <ErrorText error={login.error} />
        <button type="submit" disabled={login.isPending} className={buttonClass}>
          {login.isPending ? "Logging in..." : "Log in"}
        </button>
        <p className="text-sm text-muted">For Ludware&apos;s staff. Firms and traders log in elsewhere.</p>
      </form>
    </main>
  );
}
