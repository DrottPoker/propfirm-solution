"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import type { OpsFirmGroup } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { challengesText, latestText, opsGroupLabels, opsGroups, slotsUsed, stageViews } from "@/lib/ops";
import { useOpsFirms } from "@/lib/opsQueries";
import { useDebounced } from "@/lib/useDebounced";

import { SearchIcon } from "./icons";
import { AdminPage, Badge, ErrorText, FilterTabs, PageHeader, ProgressBar } from "./ui";

/** Every firm on the platform (ADR 0024): found by name, short name or an administrator's email, in a group. */
export function OpsFirms({ initialGroup, initialSearch }: { initialGroup: OpsFirmGroup; initialSearch: string }) {
  const [group, setGroup] = useState(initialGroup);
  const [search, setSearch] = useState(initialSearch);
  const query = useDebounced(search, 300);
  const firms = useOpsFirms(group, query.trim());

  // The address keeps the group and the search, so going back or sharing it shows the same firms.
  useEffect(() => {
    const params = new URLSearchParams();
    if (group !== "All") {
      params.set("group", group);
    }

    if (query.trim()) {
      params.set("search", query.trim());
    }

    window.history.replaceState(null, "", `/ops/firms${params.size > 0 ? `?${params}` : ""}`);
  }, [group, query]);

  const rows = firms.data?.firms ?? [];
  const counts = firms.data?.counts;
  const now = firms.dataUpdatedAt;
  const total = counts?.[countKey[group]];

  return (
    <AdminPage>
      <PageHeader title="Firms" description="Every firm on the platform. Those to review come oldest first, the others newest first." />

      <FilterTabs
        label="Firms to show"
        options={opsGroups.map((g) => ({ value: g, label: opsGroupLabels[g], count: counts?.[countKey[g]], highlight: g === "ToReview" || g === "Unpaid" }))}
        value={group}
        onChange={setGroup}
      />

      <section aria-label="Firms" className="flex flex-col rounded-lg border border-border bg-panel">
        <div className="border-b border-border px-4 py-3.5">
          <label className="flex max-w-xl items-center gap-2 rounded border border-border bg-background px-3 text-muted focus-within:border-accent">
            <SearchIcon className="size-4 shrink-0" />
            <span className="sr-only">Search firms</span>
            <input
              type="search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Name, short name or an administrator's email"
              className="min-w-0 flex-1 bg-transparent py-2 text-foreground outline-none"
            />
          </label>
        </div>

        {firms.error && (
          <div className="px-4 py-3">
            <ErrorText error={firms.error} />
          </div>
        )}
        {firms.isPending ? (
          <p className="px-4 py-6 text-sm text-muted">Loading...</p>
        ) : rows.length === 0 ? (
          <p className="px-4 py-6 text-sm text-muted">{query.trim() ? "No firm matches. Try a part of the name or the short name." : group === "ToReview" ? "No application waits for us." : "No firms here."}</p>
        ) : (
          <div className={`overflow-x-auto transition-opacity ${firms.isPlaceholderData ? "opacity-60" : ""}`}>
            <table className="w-full min-w-[56rem] text-sm">
              <thead className="text-left text-muted">
                <tr>
                  <th scope="col" className="px-4 py-3 font-normal">
                    Firm
                  </th>
                  <th scope="col" className="px-4 py-3 font-normal">
                    Stage
                  </th>
                  <th scope="col" className="px-4 py-3 font-normal">
                    Open challenges
                  </th>
                  <th scope="col" className="px-4 py-3 text-right font-normal">
                    Pays per month
                  </th>
                  <th scope="col" className="px-4 py-3 font-normal">
                    Latest
                  </th>
                </tr>
              </thead>
              <tbody>
                {rows.map((firm) => {
                  const stage = stageViews[firm.stage];
                  const used = slotsUsed(firm);
                  return (
                    <tr key={firm.id} className={`border-t border-border ${firm.stage === "ToReview" ? "bg-accent/5" : firm.stage === "Unpaid" || firm.stage === "Suspended" ? "bg-loss/5" : ""}`}>
                      <td className="px-4 py-3">
                        <Link href={`/ops/firms/${encodeURIComponent(firm.id)}`} className="flex flex-col">
                          <span className="font-medium text-accent">{firm.name}</span>
                          <span className="text-xs text-muted">{firm.id}</span>
                        </Link>
                      </td>
                      <td className="px-4 py-3">
                        <Badge tone={stage.tone}>{stage.label}</Badge>
                      </td>
                      <td className="px-4 py-3">
                        <span className="flex max-w-48 flex-col gap-1.5">
                          <span>{challengesText(firm)}</span>
                          {used !== null && (
                            <ProgressBar value={used} label={`${firm.openChallenges} of ${firm.slots} slots taken`} tone={firm.pausedChallenges > 0 ? "loss" : used >= 80 ? "warning" : "accent"} />
                          )}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-right font-mono tabular-nums">
                        {firm.monthlyPrice === null ? <span className="text-muted">{firm.configured ? "Free" : "-"}</span> : `${formatMoney(firm.monthlyPrice)} ${firms.data?.currency}`}
                      </td>
                      <td className="px-4 py-3 text-muted">{latestText(firm, now)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
        {rows.length > 0 && (
          <p className="border-t border-border px-4 py-3 text-sm text-muted">
            Showing {rows.length}
            {total !== undefined && total > rows.length && ` of ${total}`}. Pays per month is the firm&apos;s plan before VAT; firms in the sandbox pay nothing.
          </p>
        )}
      </section>
    </AdminPage>
  );
}

const countKey = {
  All: "all",
  ToReview: "toReview",
  Sandbox: "sandbox",
  Live: "live",
  Unpaid: "unpaid",
  Suspended: "suspended",
  Rejected: "rejected",
} as const satisfies Record<OpsFirmGroup, string>;
