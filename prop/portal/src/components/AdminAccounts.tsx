"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";

import type { ChallengeStatus } from "@/lib/api/types";
import { statusLabels } from "@/lib/challenge";
import { formatDateTime, formatMoney } from "@/lib/format";
import { useChallenges, useFirmAccounts, useFirmSettings, useStartAccount, type AccountFilter } from "@/lib/queries";

import { StatusBadge } from "./AccountOverview";
import { buttonClass, ErrorText, fieldClass, Panel, secondaryButtonClass } from "./ui";

const statuses = Object.keys(statusLabels) as ChallengeStatus[];

/** The firm's admin panel: start challenges for traders and find accounts. */
export function AdminAccounts() {
  return (
    <main className="mx-auto flex w-full max-w-6xl flex-col gap-6 p-6">
      <NewAccount />
      <Accounts />
    </main>
  );
}

function NewAccount() {
  const router = useRouter();
  const settings = useFirmSettings();
  const challenges = useChallenges(settings.data !== undefined && settings.data.status !== "Provisioning");
  const start = useStartAccount();
  const [email, setEmail] = useState("");
  const [chosenChallenge, setChosenChallenge] = useState("");
  const [reference, setReference] = useState("");

  const list = challenges.data ?? [];
  const challengeId = chosenChallenge || list[0]?.id || "";

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    start.mutate(
      { email, challengeId, reference: reference.trim() || null },
      { onSuccess: (account) => router.push(`/admin/accounts/${account.id}`) },
    );
  };

  // A firm that just signed up waits for its trading server, and for its first challenge with it.
  if (settings.data?.status === "Provisioning") {
    return (
      <Panel title="Start a challenge">
        <p role="status" className="text-sm text-muted">
          Your trading server is being set up. This takes a few seconds.
        </p>
      </Panel>
    );
  }

  return (
    <Panel
      title="Start a challenge"
      actions={
        <Link href="/admin/challenges" className="text-sm text-accent hover:underline">
          Manage challenges
        </Link>
      }
    >
      <form onSubmit={submit} className="flex flex-wrap items-end gap-3">
        <label className="flex min-w-60 flex-1 flex-col gap-1 text-sm">
          <span className="text-muted">Trader email</span>
          <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} className={fieldClass} />
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Challenge</span>
          <select required value={challengeId} onChange={(e) => setChosenChallenge(e.target.value)} className={fieldClass}>
            {list.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name} ({c.id})
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Order reference (optional)</span>
          <input value={reference} onChange={(e) => setReference(e.target.value)} className={fieldClass} />
        </label>
        <button type="submit" disabled={start.isPending || !challengeId} className={buttonClass}>
          {start.isPending ? "Starting..." : "Start challenge"}
        </button>
      </form>
      <ErrorText error={start.error ?? challenges.error} />
    </Panel>
  );
}

function Accounts() {
  const [filter, setFilter] = useState<AccountFilter>({ email: "", status: "" });
  const [email, setEmail] = useState("");
  const accounts = useFirmAccounts(filter);

  const search = (event: React.FormEvent) => {
    event.preventDefault();
    setFilter((f) => ({ ...f, email: email.trim() }));
  };

  return (
    <Panel title="Accounts">
      <form onSubmit={search} className="flex flex-wrap items-end gap-3">
        <label className="flex min-w-60 flex-1 flex-col gap-1 text-sm">
          <span className="text-muted">Search by email</span>
          <input type="search" value={email} onChange={(e) => setEmail(e.target.value)} className={fieldClass} />
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Status</span>
          <select
            value={filter.status}
            onChange={(e) => setFilter((f) => ({ ...f, status: e.target.value as ChallengeStatus | "" }))}
            className={fieldClass}
          >
            <option value="">All</option>
            {statuses.map((status) => (
              <option key={status} value={status}>
                {statusLabels[status]}
              </option>
            ))}
          </select>
        </label>
        <button type="submit" className={secondaryButtonClass}>
          Search
        </button>
      </form>

      <ErrorText error={accounts.error} />
      {accounts.data && accounts.data.length === 0 && <p className="text-sm text-muted">No accounts found.</p>}
      {accounts.data && accounts.data.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="text-left text-muted">
              <tr>
                <th className="py-2 font-normal">Account</th>
                <th className="py-2 font-normal">Trader</th>
                <th className="py-2 font-normal">Challenge</th>
                <th className="py-2 font-normal">Stage</th>
                <th className="py-2 font-normal">Status</th>
                <th className="py-2 text-right font-normal">Balance</th>
                <th className="py-2 text-right font-normal">Started</th>
              </tr>
            </thead>
            <tbody>
              {accounts.data.map((account) => (
                <tr key={account.id} className="border-t border-border">
                  <td className="py-2">
                    <Link href={`/admin/accounts/${account.id}`} className="text-accent hover:underline">
                      #{account.number}
                    </Link>
                  </td>
                  <td className="py-2">{account.email}</td>
                  <td className="py-2">{account.challengeId}</td>
                  <td className="py-2">{account.stageName}</td>
                  <td className="py-2">
                    <StatusBadge status={account.status} />
                    {account.paused && account.status !== "Failed" && account.status !== "Cancelled" && (
                      <span className="ml-2 rounded bg-warning/20 px-2 py-0.5 text-sm text-warning">Paused</span>
                    )}
                  </td>
                  <td className="py-2 text-right font-mono tabular-nums">{formatMoney(account.balance)}</td>
                  <td className="py-2 text-right text-muted">{formatDateTime(account.createdAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Panel>
  );
}
