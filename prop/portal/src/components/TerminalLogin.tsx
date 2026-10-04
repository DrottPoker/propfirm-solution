"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef } from "react";

import { terminalAccountOf } from "@/lib/challenge";
import { loginWithNext } from "@/lib/next";
import { useMe, useMyAccounts, useTerminalLink } from "@/lib/queries";

import { AuthCard } from "./PasswordReset";
import { buttonClass, ErrorText, Message } from "./ui";

/**
 * The firm's traders log in to the trading terminal here, since their trading password is never shown: logged in to the
 * portal, the terminal opens with a one-time link, otherwise the portal's login comes first and returns here.
 */
export function TerminalLogin({ tradingAccountId }: { tradingAccountId: string | null }) {
  const router = useRouter();
  const me = useMe("trader");

  useEffect(() => {
    if (me.data === null) {
      router.replace(loginWithNext("/login", "/", window.location.pathname + window.location.search));
    }
  }, [me.data, router]);

  if (me.isError) {
    return <Message text="The portal cannot be reached right now. Try again shortly." />;
  }

  return me.data ? <OpenTerminal tradingAccountId={tradingAccountId} /> : <Message text="Loading..." />;
}

function OpenTerminal({ tradingAccountId }: { tradingAccountId: string | null }) {
  const accounts = useMyAccounts();
  const { mutate, error } = useTerminalLink();
  const target = accounts.data ? terminalAccountOf(accounts.data.map((d) => d.account), tradingAccountId) : undefined;
  const opened = useRef(false);

  useEffect(() => {
    if (target && !opened.current) {
      opened.current = true;
      mutate(target.id, { onSuccess: (result) => window.location.replace(result.url) });
    }
  }, [target, mutate]);

  if (accounts.isError || error || target === null) {
    return (
      <AuthCard audience="trader" title="Open the terminal">
        {target === null ? (
          <p className="text-sm">None of your accounts can trade right now. See them, or start a new challenge, in the portal.</p>
        ) : (
          <ErrorText error={error ?? accounts.error} />
        )}
        <Link href="/" className={`${buttonClass} self-start`}>
          Go to your accounts
        </Link>
      </AuthCard>
    );
  }

  return <Message text="Opening the terminal..." />;
}
