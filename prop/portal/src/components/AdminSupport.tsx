"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

import { useBranding } from "@/app/providers";

import { whenText } from "@/lib/admin";
import type { SupportTicketCounts, SupportTicketGroup, SupportTicketSummary } from "@/lib/api/types";
import { formatDateTime } from "@/lib/format";
import { useCloseTicket, useFirmTicket, useFirmTickets, useMe } from "@/lib/queries";
import { lastAuthorName, supportGroupLabels, supportGroups, waitingText } from "@/lib/support";
import { useDebounced } from "@/lib/useDebounced";

import { SearchIcon } from "./icons";
import { Conversation, MessageForm, TicketStatusBadge } from "./SupportThread";
import { AdminPage, ErrorText, FilterTabs, Message, PageHeader, Panel, secondaryButtonClass } from "./ui";

/** A ticket that has waited this long for the firm is marked. */
const lateAfterMs = 24 * 60 * 60 * 1000;

/**
 * The firm's support tickets: those that wait for it, the longest waiting first, those that wait for the trader and
 * those closed, found by the trader's email, the subject or the ticket's number.
 */
export function AdminTickets({ initialGroup, initialSearch }: { initialGroup: SupportTicketGroup; initialSearch: string }) {
  const [group, setGroup] = useState(initialGroup);
  const [search, setSearch] = useState(initialSearch);
  const query = useDebounced(search, 300);
  const tickets = useFirmTickets({ group, search: query });
  const branding = useBranding();

  // The address keeps the group and the search, so going back or sharing it shows the same tickets.
  useEffect(() => {
    const params = new URLSearchParams();
    if (group !== "Open") {
      params.set("group", group);
    }

    if (query.trim()) {
      params.set("search", query.trim());
    }

    window.history.replaceState(null, "", `/admin/support${params.size > 0 ? `?${params}` : ""}`);
  }, [group, query]);

  const pages = tickets.data?.pages ?? [];
  const rows = pages.flatMap((p) => p.tickets);
  const counts = pages[0]?.counts;
  const now = tickets.dataUpdatedAt;

  return (
    <AdminPage>
      <PageHeader
        title="Support"
        description={`Questions from your traders. Answer them here, and the trader gets your answer by email in ${branding.name}'s name.`}
      />

      <FilterTabs
        label="Tickets to show"
        options={supportGroups.map((g) => ({ value: g, label: supportGroupLabels[g], count: counts?.[countKey[g]], highlight: g === "Open" }))}
        value={group}
        onChange={setGroup}
      />

      <section aria-label="Tickets" className="flex flex-col rounded-lg border border-border bg-panel">
        <div className="border-b border-border px-4 py-3.5">
          <label className="flex min-w-0 max-w-xl items-center gap-2 rounded border border-border bg-background px-3 text-muted focus-within:border-accent">
            <SearchIcon className="size-4 shrink-0" />
            <span className="sr-only">Search tickets</span>
            <input
              type="search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Email, subject or ticket number"
              className="min-w-0 flex-1 bg-transparent py-2 text-foreground outline-none"
            />
          </label>
        </div>

        {tickets.error && (
          <div className="px-4 py-3">
            <ErrorText error={tickets.error} />
          </div>
        )}
        {tickets.isPending ? (
          <p className="px-4 py-6 text-sm text-muted">Loading...</p>
        ) : rows.length === 0 ? (
          <p className="px-4 py-6 text-sm text-muted">{emptyText(group, query.trim() !== "")}</p>
        ) : (
          <ul className={`flex flex-col transition-opacity ${tickets.isPlaceholderData ? "opacity-60" : ""}`}>
            {rows.map((ticket) => (
              <TicketRow key={ticket.id} ticket={ticket} now={now} firmName={branding.name} />
            ))}
          </ul>
        )}
        {rows.length > 0 && (
          <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border px-4 py-3 text-sm text-muted">
            <span>
              Showing {rows.length}
              {counts && ` of ${counts[countKey[group]]}`}
            </span>
            {tickets.hasNextPage && (
              <button type="button" onClick={() => tickets.fetchNextPage()} disabled={tickets.isFetchingNextPage} className={`${secondaryButtonClass} text-foreground`}>
                {tickets.isFetchingNextPage ? "Loading..." : "Show more"}
              </button>
            )}
          </div>
        )}
      </section>
    </AdminPage>
  );
}

/** A ticket in the list: subject, trader, account, the latest message, where it is and how long it has waited. */
function TicketRow({ ticket, now, firmName }: { ticket: SupportTicketSummary; now: number; firmName: string }) {
  const late = ticket.waitingSince !== null && now - Date.parse(ticket.waitingSince) > lateAfterMs;
  return (
    <li className={`border-t border-border first:border-t-0 ${late ? "bg-warning/5" : ""}`}>
      <Link href={`/admin/support/${ticket.id}`} className="flex flex-col gap-1.5 px-4 py-3.5 hover:bg-background/50 sm:flex-row sm:items-start sm:gap-4">
        <span className="flex min-w-0 flex-1 flex-col gap-1">
          <span className="flex flex-wrap items-center gap-x-2.5 gap-y-1">
            <span className="font-mono text-sm text-accent">#{ticket.number}</span>
            <span className="min-w-0 truncate font-medium">{ticket.subject}</span>
            <TicketStatusBadge status={ticket.status} viewer="admin" firmName={firmName} />
          </span>
          <span className="truncate text-sm text-muted">
            {lastAuthorName(ticket, "admin", firmName)}: {ticket.preview}
          </span>
          <span className="text-xs text-muted">
            {ticket.traderEmail}
            {ticket.account && <> · Account #{ticket.account.number}</>} · {ticket.messages} {ticket.messages === 1 ? "message" : "messages"}
          </span>
        </span>
        <span className={`shrink-0 text-xs sm:pt-1 sm:text-right ${late ? "text-warning" : "text-muted"}`}>
          {ticket.waitingSince ? waitingText(ticket.waitingSince, now) : whenText(ticket.updatedAt, now)}
        </span>
      </Link>
    </li>
  );
}

function emptyText(group: SupportTicketGroup, searched: boolean): string {
  if (searched) {
    return "No tickets found.";
  }

  switch (group) {
    case "Open":
      return "No ticket waits for you. Traders write to you from Support in your portal.";
    case "Answered":
      return "No ticket waits for a trader's answer.";
    case "Closed":
      return "No closed tickets yet.";
    default:
      return "No tickets yet. Traders write to you from Support in your portal.";
  }
}

const countKey = { Open: "open", Answered: "answered", Closed: "closed", All: "all" } as const satisfies Record<SupportTicketGroup, keyof SupportTicketCounts>;

/** One of the firm's tickets: the trader and the account, the conversation, and the firm's answer. */
export function AdminTicket({ ticketId }: { ticketId: string }) {
  const ticket = useFirmTicket(ticketId);
  const me = useMe("admin");
  const branding = useBranding();
  const close = useCloseTicket("admin", ticketId);

  if (ticket.isError) {
    return <Message text={ticket.error.message} />;
  }

  if (!ticket.data) {
    return <Message text="Loading..." />;
  }

  const data = ticket.data;
  const trader = data.traderName ? `${data.traderName} (${data.traderEmail})` : data.traderEmail;
  return (
    <AdminPage>
      <PageHeader
        back={
          <nav aria-label="Breadcrumb" className="text-sm text-muted">
            <Link href="/admin/support" className="hover:text-foreground">
              Support
            </Link>{" "}
            <span aria-hidden="true">/</span> <span className="text-foreground">#{data.number}</span>
          </nav>
        }
        title={data.subject}
        description={
          <>
            Ticket #{data.number} from {trader} · Opened {formatDateTime(data.createdAt)}
          </>
        }
        actions={
          <>
            <TicketStatusBadge status={data.status} viewer="admin" firmName={branding.name} />
            {data.status !== "Closed" && (
              <button type="button" disabled={close.isPending} onClick={() => close.mutate()} className={secondaryButtonClass}>
                {close.isPending ? "Closing..." : "Close without answering"}
              </button>
            )}
          </>
        }
      />
      <ErrorText error={close.error} />

      <div className="grid grid-cols-1 gap-5 lg:grid-cols-[minmax(0,1fr)_18rem]">
        <div className="flex min-w-0 flex-col gap-4">
          <Conversation ticket={data} viewer="admin" names={{ firm: branding.name, trader: data.traderName ?? data.traderEmail, me: me.data?.email ?? "" }} />
          {data.status === "Closed" && data.closedAt && (
            <p className="text-sm text-muted">
              Closed {formatDateTime(data.closedAt)} by {data.closedBy === "Trader" ? "the trader" : (data.closedByAdmin ?? "your team")}. A new message opens it again.
            </p>
          )}
          <MessageForm ticket={data} viewer="admin" />
        </div>
        <Panel title="Trader">
          <dl className="flex flex-col gap-3 text-sm">
            <div className="flex flex-col gap-0.5">
              <dt className="text-xs text-muted">Email</dt>
              <dd className="break-all">{data.traderEmail}</dd>
            </div>
            {data.traderName && (
              <div className="flex flex-col gap-0.5">
                <dt className="text-xs text-muted">Name</dt>
                <dd>{data.traderName}</dd>
              </div>
            )}
            <div className="flex flex-col gap-0.5">
              <dt className="text-xs text-muted">About</dt>
              <dd>
                {data.account ? (
                  <Link href={`/admin/accounts/${data.account.id}`} className="text-accent hover:underline">
                    Account #{data.account.number} · {data.account.challengeName}
                  </Link>
                ) : (
                  "No account in particular"
                )}
              </dd>
            </div>
          </dl>
          <div className="flex flex-col gap-1.5 border-t border-border pt-3 text-sm">
            <Link href={`/admin/accounts?search=${encodeURIComponent(data.traderEmail)}`} className="text-accent hover:underline">
              The trader&apos;s accounts
            </Link>
            <Link href={`/admin/support?group=All&search=${encodeURIComponent(data.traderEmail)}`} className="text-accent hover:underline">
              The trader&apos;s tickets
            </Link>
          </div>
        </Panel>
      </div>
    </AdminPage>
  );
}
