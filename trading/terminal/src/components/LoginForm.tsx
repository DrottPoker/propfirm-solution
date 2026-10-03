"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import { productName } from "@/lib/config";
import { LoginFailedError, useLogin, useServers } from "@/lib/queries";
import { initialServer, rememberedServer, rememberServer } from "@/lib/servers";

import { LogoMark } from "./icons";

const fieldClass = "rounded-md border border-border bg-raised px-3 py-2 outline-none focus:border-accent";

/** Login with the firm's server, email and password. Traders get them from their firm's portal. */
export function LoginForm({ requestedServer }: { requestedServer: string | null }) {
  const router = useRouter();
  const servers = useServers();
  const login = useLogin();
  const [chosenServer, setChosenServer] = useState<string | null>(null);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  const list = servers.data ?? [];
  const server = chosenServer ?? initialServer(list, requestedServer, rememberedServer());

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    login.mutate(
      { server, email, password },
      {
        onSuccess: () => {
          rememberServer(server);
          router.replace("/");
        },
      },
    );
  };

  const failure = login.error ?? servers.error;
  const error = failure
    ? failure instanceof LoginFailedError
      ? failure.message
      : "Could not reach the trading service."
    : null;

  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <form onSubmit={submit} className="flex w-full max-w-sm flex-col gap-4 rounded-xl border border-border bg-panel p-6 shadow-2xl shadow-black/40">
        <h1 className="flex items-center gap-2.5 text-lg font-semibold tracking-tight">
          <LogoMark />
          {productName}
        </h1>

        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Server</span>
          <select required value={server} onChange={(e) => setChosenServer(e.target.value)} className={fieldClass}>
            <option value="" disabled>
              {servers.isPending ? "Loading servers..." : "Choose your firm's server"}
            </option>
            {list.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </label>

        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Email</span>
          <input
            type="email"
            autoComplete="username"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className={fieldClass}
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
            className={fieldClass}
          />
        </label>

        {error && (
          <p role="alert" className="text-sm text-loss">
            {error}
          </p>
        )}

        <button type="submit" disabled={login.isPending} className="rounded-md bg-accent py-2 font-medium text-white hover:bg-accent/90 disabled:opacity-50">
          {login.isPending ? "Logging in..." : "Log in"}
        </button>
      </form>
    </main>
  );
}
