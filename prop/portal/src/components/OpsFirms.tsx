"use client";

import Link from "next/link";
import { useState } from "react";

import type { FirmFilter } from "@/lib/api/types";
import { formatDateTime } from "@/lib/format";
import { useOpsFirms } from "@/lib/opsQueries";
import { reviewStatusLabels } from "@/lib/verification";

import { Panel } from "./ui";

const tabs: { filter: FirmFilter; label: string }[] = [
  { filter: "ToReview", label: "To review" },
  { filter: "Suspended", label: "Suspended" },
  { filter: "All", label: "All firms" },
];

/** The firms for our staff: those waiting for review first, oldest first. */
export function OpsFirms() {
  const [filter, setFilter] = useState<FirmFilter>("ToReview");
  const firms = useOpsFirms(filter);

  return (
    <main className="mx-auto flex w-full max-w-5xl flex-col gap-6 p-6">
      <Panel
        title="Firms"
        actions={
          <div role="tablist" className="flex gap-2 text-sm">
            {tabs.map((tab) => (
              <button
                key={tab.filter}
                type="button"
                role="tab"
                aria-selected={filter === tab.filter}
                onClick={() => setFilter(tab.filter)}
                className={`rounded px-3 py-1 ${filter === tab.filter ? "bg-accent/20 text-accent" : "text-muted hover:text-foreground"}`}
              >
                {tab.label}
              </button>
            ))}
          </div>
        }
      >
        {firms.isError ? (
          <p className="text-sm text-loss">The firms cannot be loaded right now. Try again shortly.</p>
        ) : !firms.data ? (
          <p className="text-sm text-muted">Loading...</p>
        ) : firms.data.length === 0 ? (
          <p className="text-sm text-muted">{filter === "ToReview" ? "No firm is waiting for review." : "No firms."}</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="text-left text-muted">
                <tr>
                  <th className="py-2 font-normal">Firm</th>
                  <th className="py-2 font-normal">Status</th>
                  <th className="py-2 font-normal">Review</th>
                  <th className="py-2 font-normal">Sent</th>
                  <th className="py-2 font-normal">Suspended</th>
                </tr>
              </thead>
              <tbody>
                {firms.data.map((firm) => (
                  <tr key={firm.id} className="border-t border-border">
                    <td className="py-2">
                      <Link href={`/ops/firms/${firm.id}`} className="text-accent hover:underline">
                        {firm.name}
                      </Link>{" "}
                      <span className="text-muted">{firm.id}</span>
                    </td>
                    <td className="py-2">{firm.status}</td>
                    <td className="py-2">{firm.configured ? "Configured" : firm.review ? reviewStatusLabels[firm.review] : "-"}</td>
                    <td className="py-2 text-muted">{firm.submittedAt ? formatDateTime(firm.submittedAt) : "-"}</td>
                    <td className="py-2 text-loss">{firm.suspendedAt ? formatDateTime(firm.suspendedAt) : ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>
    </main>
  );
}
