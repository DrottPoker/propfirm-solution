"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";

import { productName } from "@/lib/config";
import { LoginFailedError, useLogin, useServer, useServers } from "@/lib/queries";
import { initialServer, portalLogin, rememberedAccount, rememberedServer, rememberServer } from "@/lib/servers";

import { LogoMark } from "./icons";

const fieldClass = "rounded-md border border-border bg-raised px-3 py-2 outline-none focus:border-accent";

/**
 * Login with the firm's server, email and password. A firm with a portal logs its traders in there instead, since
 * they have no password for the terminal: the page then leads them to the portal, and does so at once when their
 * session in the terminal just ended.
 */
export function LoginForm({ requestedServer, sessionEnded }: { requestedServer: string | null; sessionEnded: boolean }) {
  const router = useRouter();
  const servers = useServers();
  const login = useLogin();
  const [chosenServer, setChosenServer] = useState<string | null>(null);
  const [withPassword, setWithPassword] = useState(false);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  const list = servers.data ?? [];

  // A server that is not listed, for example a firm still in its sandbox, is looked up by its id.
  const known = requestedServer ?? rememberedServer();
  const lookup = useServer(chosenServer === null && known && !list.some((s) => s.id === known) ? known : null);
  const server = chosenServer ?? (lookup.data ? lookup.data.id : initialServer(list, requestedServer, rememberedServer()));
  const serverInfo = list.find((s) => s.id === server) ?? (lookup.data?.id === server ? lookup.data : null);
  const portal = portalLogin(serverInfo, rememberedAccount());

  useEffect(() => {
    if (sessionEnded && portal) {
      window.location.replace(portal);
    }
  }, [sessionEnded, portal]);

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
            {serverInfo && !list.some((s) => s.id === serverInfo.id) && <option value={serverInfo.id}>{serverInfo.name}</option>}
            {list.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </label>

        {portal && serverInfo && !withPassword ? (
          <div className="flex flex-col gap-3 text-sm">
            {sessionEnded && <p className="text-muted">Your session ended. Taking you back to {serverInfo.name}...</p>}
            <a href={portal} className="rounded-md bg-accent py-2 text-center font-medium text-white hover:bg-accent/90">
              Log in through {serverInfo.name}
            </a>
            <p className="text-xs text-muted">You log in on {serverInfo.name}&apos;s portal, which opens the terminal for you.</p>
            <button type="button" onClick={() => setWithPassword(true)} className="self-start text-xs text-muted underline hover:text-foreground">
              I have a password for the terminal
            </button>
          </div>
        ) : (
          <>
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
          </>
        )}
      </form>
    </main>
  );
}
