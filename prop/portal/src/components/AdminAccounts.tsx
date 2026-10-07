"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import { accountGroups, accountStatus, groupLabels, stageLabel } from "@/lib/admin";
import type { AccountGroup } from "@/lib/api/types";
import { resultTone, toneText } from "@/lib/dashboard";
import { formatDate, formatMoney, formatSignedMoney } from "@/lib/format";
import { useAccountSearch, useChallenges } from "@/lib/queries";
import { useDebounced } from "@/lib/useDebounced";

import { AccountsIcon, SearchIcon } from "./icons";
import { StartChallengeButton } from "./StartChallenge";
import { AdminPage, Badge, EmptyState, ErrorText, FilterTabs, PageHeader, secondaryButtonClass } from "./ui";

/** Every challenge account at the firm, newest first: found by email, number or reference, in a group and a challenge. */
export function AdminAccounts({ initialGroup, initialSearch }: { initialGroup: AccountGroup; initialSearch: string }) {
  const [group, setGroup] = useState(initialGroup);
  const [search, setSearch] = useState(initialSearch);
  const [challengeId, setChallengeId] = useState("");
  const query = useDebounced(search, 300);
  const accounts = useAccountSearch({ search: query, group, challengeId });
  const challenges = useChallenges();

  // The address keeps the group and the search, so going back or sharing it shows the same accounts.
  useEffect(() => {
    const params = new URLSearchParams();
    if (group !== "All") {
      params.set("group", group);
    }

    if (query.trim()) {
      params.set("search", query.trim());
    }

    window.history.replaceState(null, "", `/admin/accounts${params.size > 0 ? `?${params}` : ""}`);
  }, [group, query]);

  const pages = accounts.data?.pages ?? [];
  const rows = pages.flatMap((p) => p.accounts);
  const counts = pages[0]?.counts;
  const challengeName = (id: string) => challenges.data?.find((c) => c.id === id)?.name ?? id;
  const filtered = query.trim() !== "" || challengeId !== "" || group !== "All";

  return (
    <AdminPage>
      <PageHeader title="Accounts" description="Every challenge account at your firm, the newest first." actions={<StartChallengeButton />} />

      <FilterTabs
        label="Accounts to show"
        options={accountGroups.map((g) => ({ value: g, label: groupLabels[g], count: counts?.[countKey[g]], highlight: g === "AwaitingFunding" }))}
        value={group}
        onChange={setGroup}
      />

      <section aria-label="Accounts" className="flex flex-col rounded-lg border border-border bg-panel">
        <div className="flex flex-wrap items-center gap-3 border-b border-border px-4 py-3.5">
          <label className="flex min-w-0 flex-[1_1_18rem] items-center gap-2 rounded border border-border bg-background px-3 text-muted focus-within:border-accent">
            <SearchIcon className="size-4 shrink-0" />
            <span className="sr-only">Search accounts</span>
            <input
              type="search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Email, account number or order reference"
              className="min-w-0 flex-1 bg-transparent py-2 text-foreground outline-none"
            />
          </label>
          <label className="flex items-center gap-2 text-sm text-muted">
            Challenge
            <select value={challengeId} onChange={(e) => setChallengeId(e.target.value)} className="rounded border border-border bg-background px-2.5 py-2 text-foreground">
              <option value="">All challenges</option>
              {(challenges.data ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </label>
        </div>

        {accounts.error && (
          <div className="px-4 py-3">
            <ErrorText error={accounts.error} />
          </div>
        )}
        {accounts.isPending ? (
          <p className="px-4 py-6 text-sm text-muted">Loading...</p>
        ) : rows.length === 0 ? (
          filtered ? (
            <EmptyState icon={<SearchIcon className="size-5" />} title="No accounts found" text="Try another email, account number or group." />
          ) : (
            <EmptyState
              icon={<AccountsIcon className="size-6" />}
              title="No accounts yet"
              text="Start a challenge for a trader, or let traders buy one in your portal's shop."
              actions={
                <>
                  <StartChallengeButton />
                  <a href="/buy" target="_blank" rel="noopener" className={secondaryButtonClass}>
                    Open your shop
                  </a>
                </>
              }
            />
          )
        ) : (
          <>
            {/* On a phone, each account is a card with what the table shows: status, stage and balance. */}
            <ul className={`flex flex-col transition-opacity sm:hidden ${accounts.isPlaceholderData ? "opacity-60" : ""}`}>
              {rows.map((account) => {
                const status = accountStatus(account);
                const result = account.balance === null ? null : account.balance - account.initialBalance;
                return (
                  <li key={account.id} className={`border-t border-border first:border-t-0 ${account.status === "AwaitingFunding" ? "bg-warning/5" : ""}`}>
                    <Link href={`/admin/accounts/${account.id}`} className="flex flex-col gap-1.5 px-4 py-3 text-sm">
                      <span className="flex items-center justify-between gap-3">
                        <span>
                          <span className="text-accent">#{account.number}</span> <span className="text-muted">{challengeName(account.challengeId)}</span>
                        </span>
                        <Badge tone={status.tone}>{status.label}</Badge>
                      </span>
                      <span className="truncate">{account.email}</span>
                      <span className="flex items-baseline justify-between gap-3 text-xs text-muted">
                        <span>
                          {stageLabel(account)},{" "}
                          {account.minTradingDays > 0 ? `${account.tradingDays} of ${account.minTradingDays} days` : `${account.tradingDays} trading days`}
                        </span>
                        <span className="text-sm text-foreground tabular-nums">
                          {formatMoney(account.balance)}
                          {result !== null && <span className={`ml-1.5 text-xs ${toneText[resultTone(result)]}`}>{formatSignedMoney(result)}</span>}
                        </span>
                      </span>
                    </Link>
                  </li>
                );
              })}
            </ul>
            <div className={`hidden overflow-x-auto transition-opacity sm:block ${accounts.isPlaceholderData ? "opacity-60" : ""}`}>
              <table className="w-full min-w-[60rem] text-sm">
                <thead className="text-left text-muted">
                  <tr>
                    <th scope="col" className="px-4 py-3 font-normal">
                      Account
                    </th>
                    <th scope="col" className="px-4 py-3 font-normal">
                      Trader
                    </th>
                    <th scope="col" className="px-4 py-3 font-normal">
                      Stage
                    </th>
                    <th scope="col" className="px-4 py-3 font-normal">
                      Status
                    </th>
                    <th scope="col" className="px-4 py-3 text-right font-normal">
                      Balance
                    </th>
                    <th scope="col" className="px-4 py-3 text-right font-normal">
                      Trading days
                    </th>
                    <th scope="col" className="px-4 py-3 text-right font-normal">
                      Started
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((account) => {
                    const status = accountStatus(account);
                    const result = account.balance === null ? null : account.balance - account.initialBalance;
                    return (
                      <tr key={account.id} className={`border-t border-border ${account.status === "AwaitingFunding" ? "bg-warning/5" : ""}`}>
                        <td className="px-4 py-3">
                          <Link href={`/admin/accounts/${account.id}`} className="flex flex-col" aria-label={`Account #${account.number}, ${challengeName(account.challengeId)}`}>
                            <span className="text-accent">#{account.number}</span>
                            <span className="text-xs text-muted">{challengeName(account.challengeId)}</span>
                          </Link>
                        </td>
                        <td className="px-4 py-3">
                          {account.email}
                          {account.reference && <span className="block text-xs text-muted">Ref. {account.reference}</span>}
                        </td>
                        <td className={`px-4 py-3 ${account.funded && account.status !== "Failed" && account.status !== "Cancelled" ? "text-profit" : ""}`}>{stageLabel(account)}</td>
                        <td className="px-4 py-3">
                          <Badge tone={status.tone}>{status.label}</Badge>
                        </td>
                        <td className="px-4 py-3 text-right tabular-nums">
                          {formatMoney(account.balance)}
                          {result !== null && <span className={`block text-xs ${toneText[resultTone(result)]}`}>{formatSignedMoney(result)}</span>}
                        </td>
                        <td className="px-4 py-3 text-right tabular-nums">
                          {account.tradingDays} of {account.minTradingDays}
                        </td>
                        <td className="px-4 py-3 text-right text-muted">{formatDate(account.createdAt)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </>
        )}
        {rows.length > 0 && (
          <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border px-4 py-3 text-sm text-muted">
            <span>
              Showing {rows.length}
              {counts && ` of ${counts[countKey[group]]}`}
            </span>
            {accounts.hasNextPage && (
              <button type="button" onClick={() => accounts.fetchNextPage()} disabled={accounts.isFetchingNextPage} className={`${secondaryButtonClass} text-foreground`}>
                {accounts.isFetchingNextPage ? "Loading..." : "Show more"}
              </button>
            )}
          </div>
        )}
      </section>
    </AdminPage>
  );
}

const countKey = {
  All: "all",
  Evaluation: "evaluation",
  AwaitingFunding: "awaitingFunding",
  Funded: "funded",
  Ended: "ended",
} as const satisfies Record<AccountGroup, string>;
