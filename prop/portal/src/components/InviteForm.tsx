"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

import { useLinkCheck } from "@/lib/passwordQueries";
import { useAcceptAdminInvite, useAcceptInvite, useConfirmInvite, type Role } from "@/lib/queries";

import { AuthCard, LinkProblem, passwordPaths, PasswordFields } from "./PasswordReset";
import { buttonClass, ErrorText, Message } from "./ui";

/**
 * A trader, or a new administrator, opens the invitation and chooses a password for the portal. The link is checked first,
 * so a used or expired one says what to do instead.
 */
export function InviteForm({ token, role = "trader" }: { token: string | null; role?: Role }) {
  const check = useLinkCheck("invite", role, token);
  const paths = passwordPaths[role];
  const title = role === "admin" ? "Choose a password to administer the firm" : "Choose a password for your account";

  if (!token || check.data === null) {
    return (
      <AuthCard audience={role} title={title}>
        <LinkProblem
          text="This link does not work. Check that it is the whole link from the email, or log in if you already chose a password."
          action={{ href: paths.login, label: "Log in" }}
          more={{ href: paths.forgot, label: "Forgot your password?" }}
        />
      </AuthCard>
    );
  }

  if (check.isError) {
    return <Message text="The link cannot be checked right now. Try again shortly." />;
  }

  if (!check.data) {
    return <Message text="Loading..." />;
  }

  const { status, email, hasPassword } = check.data;
  if (status === "Valid" && hasPassword && role === "trader") {
    return (
      <AuthCard audience={role} title="Confirm your email">
        <ConfirmEmail token={token} email={email} />
      </AuthCard>
    );
  }

  return (
    <AuthCard audience={role} title={title}>
      {status === "Valid" ? (
        <NewPassword token={token} role={role} email={email} />
      ) : hasPassword ? (
        <LinkProblem
          text={`${status === "Used" ? "This invitation was already used" : "This invitation has expired"}, and ${email} has a password. Log in with it.`}
          action={{ href: paths.login, label: "Log in" }}
          more={{ href: paths.forgot, label: "Forgot your password?" }}
        />
      ) : (
        <LinkProblem
          text={
            role === "admin"
              ? "This invitation has expired. Ask an administrator of the firm to invite you again."
              : `This invitation has expired. Ask for a link to choose your password for ${email}, which works for 1 hour.`
          }
          action={role === "admin" ? { href: paths.login, label: "Go to log in" } : { href: paths.forgot, label: "Ask for a link" }}
        />
      )}
    </AuthCard>
  );
}

/** The trader chose a password on the order's page, so the link only confirms the email and logs the trader in. */
function ConfirmEmail({ token, email }: { token: string; email: string }) {
  const router = useRouter();
  const confirm = useConfirmInvite();
  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted">
        You have a password for <span className="text-foreground">{email}</span>. Confirm that the email is yours, and you are logged in.
      </p>
      <ErrorText error={confirm.error} />
      <button
        type="button"
        disabled={confirm.isPending || confirm.isSuccess}
        onClick={() => confirm.mutate(token, { onSuccess: () => router.replace(passwordPaths.trader.home) })}
        className={buttonClass}
      >
        {confirm.isPending || confirm.isSuccess ? "Confirming..." : "Confirm my email"}
      </button>
    </div>
  );
}

function NewPassword({ token, role, email }: { token: string; role: Role; email: string }) {
  const router = useRouter();
  const acceptAsTrader = useAcceptInvite();
  const acceptAsAdmin = useAcceptAdminInvite();
  const accept = role === "admin" ? acceptAsAdmin : acceptAsTrader;
  const [password, setPassword] = useState("");
  const [repeated, setRepeated] = useState("");
  const [mismatch, setMismatch] = useState(false);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    setMismatch(password !== repeated);
    if (password === repeated) {
      accept.mutate({ token, password }, { onSuccess: () => router.replace(passwordPaths[role].home) });
    }
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-4">
      <p className="text-sm text-muted">
        For <span className="text-foreground">{email}</span>. You log in with this email and the password.
      </p>
      <PasswordFields password={password} repeated={repeated} onPassword={setPassword} onRepeated={setRepeated} />
      <ErrorText error={mismatch ? new Error("The passwords are not the same.") : accept.error} />
      <button type="submit" disabled={accept.isPending} className={buttonClass}>
        {accept.isPending ? "Saving..." : "Save password and continue"}
      </button>
    </form>
  );
}
