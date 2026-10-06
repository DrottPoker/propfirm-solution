"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";

import { useOps } from "@/app/providers";
import { useConfirmPasswordReset, useLinkCheck, useRequestPasswordReset, type PasswordAudience } from "@/lib/passwordQueries";

import { FirmName } from "./FirmName";
import { PasswordInput } from "./PasswordInput";
import { buttonClass, ErrorText, fieldClass, Loading, Message } from "./ui";

/** Where each audience logs in, asks for a link and lands after choosing a password. */
export const passwordPaths: Record<PasswordAudience, { login: string; forgot: string; home: string }> = {
  trader: { login: "/login", forgot: "/forgot-password", home: "/" },
  admin: { login: "/admin/login", forgot: "/admin/forgot-password", home: "/admin" },
  staff: { login: "/ops/login", forgot: "/ops/forgot-password", home: "/ops" },
};

const subtitles: Record<PasswordAudience, string> = { trader: "Trader login", admin: "Admin login", staff: "Staff login" };

/** The person forgot the password: an email with a link that chooses a new one. */
export function ForgotPasswordForm({ audience }: { audience: PasswordAudience }) {
  const request = useRequestPasswordReset(audience);
  const [email, setEmail] = useState("");

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    request.mutate(email.trim());
  };

  return (
    <AuthCard audience={audience} title="Forgot your password?">
      {request.isSuccess ? (
        <div role="status" className="flex flex-col gap-3 text-sm">
          <p>
            If <span className="font-medium">{email.trim()}</span> can log in here, we have sent it a link to choose a new password. The link works for 1
            hour.
          </p>
          <p className="text-xs text-muted">No email? Check the spam folder, or ask for a new link.</p>
          <Link href={passwordPaths[audience].login} className="text-accent hover:underline">
            Back to log in
          </Link>
        </div>
      ) : (
        <form onSubmit={submit} className="flex flex-col gap-4">
          <p className="text-sm text-muted">Write the email you log in with, and we send you a link to choose a new password.</p>
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-muted">Email</span>
            <input type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} className={fieldClass} />
          </label>
          <ErrorText error={request.error} />
          <button type="submit" disabled={request.isPending} className={buttonClass}>
            {request.isPending ? "Sending..." : "Send me a link"}
          </button>
          <Link href={passwordPaths[audience].login} className="text-xs text-muted hover:text-foreground">
            Back to log in
          </Link>
        </form>
      )}
    </AuthCard>
  );
}

/** Opened from the email: the link is checked at once, then the new password is chosen and the person is logged in. */
export function ResetPasswordForm({ audience, token }: { audience: PasswordAudience; token: string | null }) {
  const check = useLinkCheck("reset", audience, token);
  const paths = passwordPaths[audience];

  if (!token || check.data === null) {
    return (
      <AuthCard audience={audience} title="Choose a new password">
        <LinkProblem text="This link does not work. Check that it is the whole link from the email, or ask for a new one." action={{ href: paths.forgot, label: "Ask for a new link" }} />
      </AuthCard>
    );
  }

  if (check.isError) {
    return <Message text="The link cannot be checked right now. Try again shortly." />;
  }

  if (!check.data) {
    return <Loading />;
  }

  return (
    <AuthCard audience={audience} title="Choose a new password">
      {check.data.status === "Valid" ? (
        <NewPassword audience={audience} token={token} email={check.data.email} />
      ) : check.data.status === "Used" ? (
        <LinkProblem text="This link was already used. Log in with your new password, or ask for a new link." action={{ href: paths.login, label: "Log in" }} more={{ href: paths.forgot, label: "Ask for a new link" }} />
      ) : (
        <LinkProblem text="This link has expired. Ask for a new one, which works for 1 hour." action={{ href: paths.forgot, label: "Ask for a new link" }} />
      )}
    </AuthCard>
  );
}

function NewPassword({ audience, token, email }: { audience: PasswordAudience; token: string; email: string }) {
  const router = useRouter();
  const confirm = useConfirmPasswordReset(audience);
  const [password, setPassword] = useState("");
  const [repeated, setRepeated] = useState("");
  const [mismatch, setMismatch] = useState(false);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    setMismatch(password !== repeated);
    if (password === repeated) {
      confirm.mutate({ token, password }, { onSuccess: () => router.replace(passwordPaths[audience].home) });
    }
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-4">
      <p className="text-sm text-muted">
        For <span className="text-foreground">{email}</span>. Other places you are logged in are logged out.
      </p>
      <PasswordFields password={password} repeated={repeated} onPassword={setPassword} onRepeated={setRepeated} />
      <ErrorText error={mismatch ? new Error("The passwords are not the same.") : confirm.error} />
      <button type="submit" disabled={confirm.isPending} className={buttonClass}>
        {confirm.isPending ? "Saving..." : "Save password and log in"}
      </button>
    </form>
  );
}

/** A new password, twice, so a typo does not lock the person out. */
export function PasswordFields({
  password,
  repeated,
  onPassword,
  onRepeated,
}: {
  password: string;
  repeated: string;
  onPassword: (value: string) => void;
  onRepeated: (value: string) => void;
}) {
  return (
    <>
      <label className="flex flex-col gap-1 text-sm">
        <span className="text-muted">Password</span>
        <PasswordInput autoComplete="new-password" required value={password} onChange={(e) => onPassword(e.target.value)} />
      </label>
      <label className="flex flex-col gap-1 text-sm">
        <span className="text-muted">Repeat password</span>
        <PasswordInput autoComplete="new-password" required value={repeated} onChange={(e) => onRepeated(e.target.value)} />
      </label>
    </>
  );
}

type LinkAction = { href: string; label: string };

/** A link from an email that cannot be used, what to do instead, and maybe another way. */
export function LinkProblem({ text, action, more }: { text: string; action: LinkAction; more?: LinkAction }) {
  return (
    <div role="status" className="flex flex-col gap-3 text-sm">
      <p>{text}</p>
      <Link href={action.href} className={`${buttonClass} self-start`}>
        {action.label}
      </Link>
      {more && (
        <Link href={more.href} className="text-xs text-muted hover:text-foreground">
          {more.label}
        </Link>
      )}
    </div>
  );
}

/** The card the login pages have, with the firm's name or ours on top. */
export function AuthCard({ audience, title, children }: { audience: PasswordAudience; title: string; children: React.ReactNode }) {
  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <section className="flex w-full max-w-sm flex-col gap-4 rounded-lg border border-border bg-panel p-6">
        <div className="flex flex-col gap-1">
          {audience === "staff" ? <OpsName /> : <FirmName size="lg" />}
          <span className="text-sm text-muted">{subtitles[audience]}</span>
          <h1 className="text-lg font-semibold">{title}</h1>
        </div>
        {children}
      </section>
    </main>
  );
}

function OpsName() {
  const ops = useOps();
  return <span className="text-xl font-semibold">{ops.name}</span>;
}
