"use client";

import { useRouter } from "next/navigation";
import { useEffect, useId, useState } from "react";

import { productName } from "@/lib/config";
import { LoginFailedError, useLogin, useServer, useServers } from "@/lib/queries";
import { initialServer, portalLogin, rememberedAccount, rememberedServer, rememberServer } from "@/lib/servers";

import { EyeIcon, EyeSlashIcon } from "./icons";
import { KronantWordmark } from "./KronantMark";

const fieldClass =
  "rounded-lg border border-border bg-background/60 px-3 py-2 outline-none transition-[border-color,box-shadow] duration-150 focus:border-accent focus:ring-3 focus:ring-accent/20";

// Dark text on brass, which reads far better than white on it.
const buttonClass =
  "rounded-lg bg-accent py-2 text-center font-medium text-accent-foreground shadow-[inset_0_1px_0_rgb(255_255_255/0.25),0_1px_2px_rgb(0_0_0/0.3)] transition duration-150 ease-out-soft hover:brightness-110 active:translate-y-px disabled:pointer-events-none disabled:opacity-50";

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
  // The characters can be shown, to check them before logging in.
  const [showPassword, setShowPassword] = useState(false);
  const passwordId = useId();

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
    <main className="flex flex-1 items-center justify-center bg-[radial-gradient(900px_420px_at_50%_-180px,color-mix(in_oklab,var(--accent)_10%,transparent),transparent_70%)] p-6">
      <form onSubmit={submit} className="flex w-full max-w-sm animate-enter flex-col gap-4 rounded-xl border border-border bg-panel p-6 shadow-raised">
        <h1 className="mb-1">
          <KronantWordmark name={productName} />
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
            <a href={portal} className={buttonClass}>
              Log in through {serverInfo.name}
            </a>
            <p className="text-xs text-muted">You log in on {serverInfo.name}&apos;s portal, which opens the terminal for you.</p>
            <button
              type="button"
              onClick={() => setWithPassword(true)}
              className="self-start text-xs text-muted underline underline-offset-2 transition-colors duration-150 hover:text-foreground"
            >
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
              <span className="relative flex">
                <input
                  id={passwordId}
                  type={showPassword ? "text" : "password"}
                  autoComplete="current-password"
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className={`${fieldClass} w-full pr-11`}
                />
                <button
                  type="button"
                  aria-label={showPassword ? "Hide password" : "Show password"}
                  aria-pressed={showPassword}
                  aria-controls={passwordId}
                  onClick={() => setShowPassword(!showPassword)}
                  className="absolute inset-y-0 right-0 grid w-10 place-items-center text-muted transition-colors hover:text-foreground"
                >
                  {showPassword ? <EyeSlashIcon /> : <EyeIcon />}
                </button>
              </span>
            </label>

            {error && (
              <p role="alert" className="text-sm text-loss">
                {error}
              </p>
            )}

            <button type="submit" disabled={login.isPending} className={buttonClass}>
              {login.isPending ? "Logging in..." : "Log in"}
            </button>
          </>
        )}
      </form>
    </main>
  );
}
