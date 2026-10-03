"use client";

import Link from "next/link";

import type { Account } from "@/lib/api/types";
import { canOpenTerminal, statusLabels } from "@/lib/challenge";
import { useMyAccount, useMyAccounts, useTerminalLink } from "@/lib/queries";

import { AccountOverview } from "./AccountOverview";
import { buttonClass, ErrorText, Message } from "./ui";

/** The trader's challenge accounts, one at a time, with the way into the trading terminal. */
export function Dashboard({ requestedAccountId }: { requestedAccountId: string | null }) {
  const accounts = useMyAccounts();

  if (accounts.isError) {
    return <Message text="Your accounts cannot be loaded right now. Try again shortly." />;
  }

  if (!accounts.data) {
    return <Message text="Loading..." />;
  }

  const list = accounts.data;
  const selected = list.find((a) => a.id === requestedAccountId) ?? list.find((a) => a.status === "Active") ?? list.at(-1);
  if (!selected) {
    return <Message text="You have no challenge yet. Your firm opens one for you when you buy it." />;
  }

  return (
    <main className="mx-auto flex w-full max-w-5xl flex-col gap-6 p-6">
      {list.length > 1 && <AccountPicker accounts={list} selectedId={selected.id} />}
      <AccountView accountId={selected.id} />
    </main>
  );
}

function AccountPicker({ accounts, selectedId }: { accounts: Account[]; selectedId: string }) {
  return (
    <nav aria-label="Your accounts" className="flex flex-wrap gap-2">
      {accounts.map((account) => (
        <Link
          key={account.id}
          href={`/?account=${account.id}`}
          aria-current={account.id === selectedId ? "page" : undefined}
          className={`rounded border px-3 py-2 text-sm ${account.id === selectedId ? "border-accent" : "border-border hover:border-muted"}`}
        >
          #{account.number} · {account.challengeId} · {statusLabels[account.status]}
        </Link>
      ))}
    </nav>
  );
}

function AccountView({ accountId }: { accountId: string }) {
  const details = useMyAccount(accountId);

  if (details.isError) {
    return <ErrorText error={details.error} />;
  }

  if (!details.data) {
    return <p className="text-muted">Loading...</p>;
  }

  return <AccountOverview details={details.data} actions={<OpenTerminalButton account={details.data.account} />} />;
}

/** Logs the trader in to the trading terminal with a one-time link. The trading password is never shown. */
function OpenTerminalButton({ account }: { account: Account }) {
  const link = useTerminalLink();

  return (
    <div className="flex flex-col items-end gap-2">
      <button
        type="button"
        disabled={!canOpenTerminal(account) || link.isPending}
        onClick={() => link.mutate(account.id, { onSuccess: (result) => window.location.assign(result.url) })}
        className={buttonClass}
      >
        {link.isPending ? "Opening..." : "Open terminal"}
      </button>
      <ErrorText error={link.error} />
    </div>
  );
}
