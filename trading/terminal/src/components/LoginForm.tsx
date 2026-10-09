"use client";

import Image from "next/image";
import { useRouter } from "next/navigation";
import { useEffect, useId, useState } from "react";

import type { ServerInfo } from "@/lib/api/types";
import { productName } from "@/lib/config";
import { ChooseServerError, LoginFailedError, useLogin, useServer, useServerSearch } from "@/lib/queries";
import { portalLogin, rememberedAccount, rememberedServer, rememberServer } from "@/lib/servers";

import { ArrowLeftIcon, EyeIcon, EyeSlashIcon, SearchIcon } from "./icons";
import { KronantWordmark } from "./KronantMark";

const fieldClass =
  "rounded-lg border border-border bg-background/60 px-3 py-2 outline-none transition-[border-color,box-shadow] duration-150 focus:border-accent focus:ring-3 focus:ring-accent/20";

// Dark text on brass, which reads far better than white on it.
const buttonClass =
  "rounded-lg bg-accent py-2 text-center font-medium text-accent-foreground shadow-[inset_0_1px_0_rgb(255_255_255/0.25),0_1px_2px_rgb(0_0_0/0.3)] transition duration-150 ease-out-soft hover:brightness-110 active:translate-y-px disabled:pointer-events-none disabled:opacity-50";

const linkClass = "text-muted underline-offset-2 transition-colors duration-150 hover:text-foreground hover:underline";

/**
 * The terminal's login (ADR 0058). On a firm's own login page, /login?server=..., the firm's logo and name come first:
 * a firm that logs its traders in on its own website sends them there, and one with passwords takes the email and the
 * password, with its page for a forgotten password. Without a firm, the email and the password find it, and a trader
 * whose login fits at several firms chooses one. No list of every firm is shown; a listed firm can be found by name.
 * The firm's risk warning and pages stand under the form.
 */
export function LoginForm({ requestedServer, sessionEnded }: { requestedServer: string | null; sessionEnded: boolean }) {
  // The firm in the link, or the one last logged in to on this device. Cleared to log in without one.
  const [serverId, setServerId] = useState<string | null>(() => requestedServer ?? rememberedServer());
  const lookup = useServer(serverId);
  const server = lookup.data ?? null;
  const unknown = serverId !== null && lookup.data === null;
  const portal = portalLogin(server, rememberedAccount());

  useEffect(() => {
    if (sessionEnded && portal && server && !server.profile.passwordLogin) {
      window.location.replace(portal);
    }
  }, [sessionEnded, portal, server]);

  const forget = () => setServerId(null);

  return (
    <main className="flex flex-1 flex-col items-center justify-center gap-6 overflow-y-auto bg-[radial-gradient(900px_420px_at_50%_-180px,color-mix(in_oklab,var(--accent)_8%,transparent),transparent_70%)] p-6">
      <div className="flex w-full max-w-sm animate-enter flex-col gap-5 rounded-xl border border-border bg-panel p-6 shadow-raised">
        {serverId !== null && lookup.isPending ? (
          <p className="py-8 text-center text-sm text-muted">Loading...</p>
        ) : server ? (
          <FirmLogin server={server} portal={portal} sessionEnded={sessionEnded} onOtherFirm={forget} />
        ) : (
          <AnyLogin unknown={unknown && requestedServer !== null} onChoose={(chosen) => setServerId(chosen.id)} />
        )}
      </div>

      {server && <FirmFooter server={server} />}
      {server && (
        <p className="flex items-center gap-1.5 text-xs text-muted">
          Trading terminal by <KronantWordmark name={productName} small />
        </p>
      )}
    </main>
  );
}

/** The firm's own login: through its website, or with a password at the firm. */
function FirmLogin({
  server,
  portal,
  sessionEnded,
  onOtherFirm,
}: {
  server: ServerInfo;
  portal: string | null;
  sessionEnded: boolean;
  onOtherFirm: () => void;
}) {
  const passwordLogin = server.profile.passwordLogin;
  return (
    <>
      <FirmMark server={server} />
      {!passwordLogin ? (
        <div className="flex flex-col gap-3 text-sm">
          {sessionEnded && <p className="text-muted">Your session ended. Taking you back to {server.name}...</p>}
          <p className="text-muted">{server.name} opens the terminal from its own website, where you log in.</p>
          {portal && (
            <a href={portal} className={buttonClass}>
              Log in through {server.name}
            </a>
          )}
        </div>
      ) : (
        <PasswordForm server={server} />
      )}
      <div className="flex flex-col gap-1.5 border-t border-border pt-4 text-xs">
        {passwordLogin && portal && (
          <a href={portal} className={linkClass}>
            Log in on {server.name}&apos;s website instead
          </a>
        )}
        <button type="button" onClick={onOtherFirm} className={`${linkClass} flex w-fit items-center gap-1`}>
          <ArrowLeftIcon className="size-3" />
          Not with {server.name}? Log in elsewhere
        </button>
      </div>
    </>
  );
}

/** A login that names no firm: the email and the password find it, and a firm can be looked up by name. */
function AnyLogin({ unknown, onChoose }: { unknown: boolean; onChoose: (server: ServerInfo) => void }) {
  const [finding, setFinding] = useState(false);
  return (
    <>
      <h1>
        <KronantWordmark name={productName} />
      </h1>
      {unknown && (
        <p role="alert" className="-mt-2 text-sm text-warning">
          That firm&apos;s login was not found. Log in with your email, or find your firm below.
        </p>
      )}
      <PasswordForm server={null} onChoose={onChoose} />
      <div className="flex flex-col gap-2 border-t border-border pt-4 text-xs">
        <p className="text-muted">Trading through a firm&apos;s website? Open the terminal from there, or find the firm&apos;s login.</p>
        {finding ? (
          <FirmSearch onChoose={onChoose} />
        ) : (
          <button type="button" onClick={() => setFinding(true)} className={`${linkClass} w-fit`}>
            Find your firm
          </button>
        )}
      </div>
    </>
  );
}

function PasswordForm({ server, onChoose }: { server: ServerInfo | null; onChoose?: (server: ServerInfo) => void }) {
  const router = useRouter();
  const login = useLogin();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  // The characters can be shown, to check them before logging in.
  const [showPassword, setShowPassword] = useState(false);
  const passwordId = useId();
  const choices = login.error instanceof ChooseServerError ? login.error.servers : null;

  const submit = (serverId: string | null) =>
    login.mutate(
      { server: serverId, email, password },
      {
        onSuccess: (me) => {
          rememberServer(me.server.id);
          router.replace("/");
        },
      },
    );

  const error =
    login.error instanceof LoginFailedError ? login.error.message : login.error && !choices ? "Could not reach the trading service." : null;
  const reset = server?.profile.links.passwordReset;

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        submit(server?.id ?? null);
      }}
      className="flex flex-col gap-4"
    >
      <label className="flex flex-col gap-1 text-sm">
        <span className="text-muted">Email</span>
        <input type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} className={fieldClass} />
      </label>

      <label className="flex flex-col gap-1 text-sm">
        <span className="flex items-baseline justify-between">
          <span className="text-muted">Password</span>
          {reset && (
            <a href={reset} className={`text-xs ${linkClass}`}>
              Forgot your password?
            </a>
          )}
        </span>
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

      {choices ? (
        <div role="group" aria-label="Choose a firm" className="flex flex-col gap-2 text-sm">
          <p className="text-muted">You have accounts at more than one firm. Which one?</p>
          {choices.map((choice) => (
            <button
              key={choice.id}
              type="button"
              disabled={login.isPending}
              onClick={() => {
                onChoose?.(choice);
                submit(choice.id);
              }}
              className="flex items-center justify-between rounded-lg border border-border bg-raised px-3 py-2 text-left font-medium transition-colors duration-150 hover:border-accent"
            >
              {choice.name}
              <span className="text-xs text-muted">Log in</span>
            </button>
          ))}
        </div>
      ) : (
        <button type="submit" disabled={login.isPending} className={buttonClass}>
          {login.isPending ? "Logging in..." : "Log in"}
        </button>
      )}
    </form>
  );
}

/** Finds a listed firm by its name, to open its own login. */
function FirmSearch({ onChoose }: { onChoose: (server: ServerInfo) => void }) {
  const [search, setSearch] = useState("");
  const found = useServerSearch(search);
  const results = found.data ?? [];
  return (
    <div className="flex flex-col gap-2">
      <label className="flex items-center gap-2 rounded-lg border border-border bg-background/60 px-2.5 py-1.5 text-sm focus-within:border-accent">
        <SearchIcon className="size-4 shrink-0 text-muted" />
        <input
          autoFocus
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="The firm's name"
          aria-label="The firm's name"
          className="w-full min-w-0 bg-transparent outline-none placeholder:text-muted"
        />
      </label>
      {search.trim().length >= 2 && !found.isPending && results.length === 0 && (
        <p className="text-muted">No firm by that name. Ask your firm for its login page.</p>
      )}
      {results.map((result) => (
        <button
          key={result.id}
          type="button"
          onClick={() => onChoose(result)}
          className="flex items-center gap-2 rounded-lg border border-border bg-raised px-3 py-2 text-left text-sm transition-colors duration-150 hover:border-accent"
        >
          <span className="min-w-0 flex-1 truncate font-medium">{result.name}</span>
          <span className="text-xs text-muted">Open its login</span>
        </button>
      ))}
    </div>
  );
}

/** The firm's logo, or its name when it has none, with what the page is for. */
function FirmMark({ server }: { server: ServerInfo }) {
  return (
    <div className="flex flex-col gap-1.5">
      {server.logoUrl ? (
        <Image src={server.logoUrl} alt={server.name} width={160} height={40} unoptimized loading="eager" className="h-8 w-auto max-w-48 object-contain object-left" />
      ) : (
        <span className="text-lg font-semibold tracking-tight">{server.name}</span>
      )}
      <h1 className="text-sm text-muted">Log in to the trading terminal</h1>
    </div>
  );
}

/** The firm's warning about risk and its own pages, under the login. */
function FirmFooter({ server }: { server: ServerInfo }) {
  const { links, riskWarning } = server.profile;
  const pages = [
    { href: links.help, label: "Help" },
    { href: links.support, label: "Support" },
    { href: links.terms, label: "Terms" },
    { href: links.privacy, label: "Privacy" },
  ].filter((page): page is { href: string; label: string } => page.href !== null);
  if (!riskWarning && pages.length === 0) {
    return null;
  }

  return (
    <div className="flex w-full max-w-sm flex-col gap-3 text-xs">
      {riskWarning && <p className="rounded-lg border border-border bg-panel/60 px-3 py-2 leading-relaxed text-muted">{riskWarning}</p>}
      {pages.length > 0 && (
        <nav aria-label={`${server.name}'s pages`} className="flex flex-wrap justify-center gap-x-4 gap-y-1">
          {pages.map((page) => (
            <a key={page.label} href={page.href} target="_blank" rel="noreferrer" className={linkClass}>
              {page.label}
            </a>
          ))}
        </nav>
      )}
    </div>
  );
}
