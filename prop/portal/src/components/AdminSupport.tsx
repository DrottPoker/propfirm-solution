"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useId, useState } from "react";

import { useBranding } from "@/app/providers";

import { whenText } from "@/lib/admin";
import type { SupportTicketCounts, SupportTicketGroup, SupportTicketSummary } from "@/lib/api/types";
import { formatDateTime } from "@/lib/format";
import { FieldError, useChallenges, useCloseTicket, useFirmTicket, useFirmTickets, useMe, useOpenTicketWithTrader, useTraderAccounts } from "@/lib/queries";
import { filesProblem, lastAuthorName, supportGroupLabels, supportGroups, supportLimits, waitingText } from "@/lib/support";
import { useDebounced } from "@/lib/useDebounced";

import { PlusIcon, SearchIcon } from "./icons";
import { Conversation, FilePicker, MessageField, MessageForm, TicketStatusBadge } from "./SupportThread";
import { AdminPage, buttonClass, ErrorText, fieldClass, FilterTabs, Message, PageHeader, Panel, secondaryButtonClass } from "./ui";

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
        actions={
          <Link href="/admin/support/new" className={`${buttonClass} flex items-center gap-1.5`}>
            <PlusIcon className="size-4" />
            Write to a trader
          </Link>
        }
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
            <TicketStatusBadge ticket={ticket} viewer="admin" firmName={firmName} />
          </span>
          <span className="truncate text-sm text-muted">
            {lastAuthorName(ticket, "admin", firmName)}: {ticket.preview}
          </span>
          <span className="text-xs text-muted">
            {ticket.traderEmail}
            {ticket.account && <> · Account #{ticket.account.number}</>} · {ticket.messages} {ticket.messages === 1 ? "message" : "messages"}
            {ticket.openedBy === "Firm" && <> · Opened by your team</>}
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

/**
 * The firm writes to one of its traders first: who, optionally about which of the trader's accounts, and the message.
 * The trader gets it by email and answers in the portal.
 */
export function AdminNewTicket({ initialEmail, initialAccountId }: { initialEmail: string; initialAccountId: string | null }) {
  const branding = useBranding();
  const open = useOpenTicketWithTrader();
  const router = useRouter();
  const challenges = useChallenges();
  const emailId = useId();
  const subjectId = useId();
  const accountFieldId = useId();
  const [email, setEmail] = useState(initialEmail);
  const [accountId, setAccountId] = useState(initialAccountId ?? "");
  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [files, setFiles] = useState<File[]>([]);
  const [problem, setProblem] = useState<{ field: string; text: string } | null>(null);

  // The trader's accounts, once the email is a whole address. The search finds parts of emails, so only exact ones count.
  const lookup = useDebounced(email.trim(), 300);
  const accounts = useTraderAccounts(lookup.includes("@") ? lookup : "");
  const tradersAccounts = (accounts.data?.accounts ?? []).filter((a) => a.email.toLowerCase() === lookup.toLowerCase());
  const chosen = tradersAccounts.some((a) => a.id === accountId) ? accountId : "";
  const unknown = lookup.includes("@") && accounts.isSuccess && tradersAccounts.length === 0;
  const challengeName = (id: string) => challenges.data?.find((c) => c.id === id)?.name ?? id;
  const field = problem?.field ?? (open.error instanceof FieldError ? open.error.field : null);

  return (
    <AdminPage narrow>
      <PageHeader
        back={
          <nav aria-label="Breadcrumb" className="text-sm text-muted">
            <Link href="/admin/support" className="hover:text-foreground">
              Support
            </Link>{" "}
            <span aria-hidden="true">/</span> <span className="text-foreground">Write to a trader</span>
          </nav>
        }
        title="Write to a trader"
        description={`The trader gets your message by email in ${branding.name}'s name, and answers in the portal. Never ask for ID documents or passwords in a ticket.`}
      />
      <form
        onSubmit={(event) => {
          event.preventDefault();
          const subjectText = subject.trim();
          const filesWrong = filesProblem(files);
          const found =
            email.trim().length === 0
              ? { field: "traderEmail", text: "Write the trader's email." }
              : subjectText.length === 0
                ? { field: "subject", text: "Write what the ticket is about." }
                : subjectText.length > supportLimits.subject
                  ? { field: "subject", text: `Keep the subject to ${supportLimits.subject} characters.` }
                  : body.trim().length === 0
                    ? { field: "body", text: "Write a message." }
                    : body.length > supportLimits.message
                      ? { field: "body", text: `Keep the message to ${supportLimits.message.toLocaleString("en-GB")} characters.` }
                      : filesWrong
                        ? { field: "files", text: filesWrong }
                        : null;
          setProblem(found);
          if (!found) {
            open.mutate(
              { traderEmail: email.trim(), subject: subjectText, body, accountId: chosen || null, files },
              { onSuccess: (ticket) => router.replace(`/admin/support/${ticket.id}`) },
            );
          }
        }}
        className="flex flex-col gap-4 rounded-lg border border-border bg-panel p-5"
      >
        <div className="flex flex-col gap-1 text-sm">
          <label htmlFor={emailId} className="text-muted">
            Trader&apos;s email
          </label>
          <input
            id={emailId}
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="trader@example.com"
            aria-invalid={field === "traderEmail" || undefined}
            disabled={open.isPending}
            className={fieldClass}
          />
          {unknown && <span className="text-xs text-warning">No accounts at your firm have this email.</span>}
        </div>
        <div className="flex flex-col gap-1 text-sm">
          <label htmlFor={accountFieldId} className="text-muted">
            About
          </label>
          <select
            id={accountFieldId}
            value={chosen}
            onChange={(e) => setAccountId(e.target.value)}
            aria-invalid={field === "accountId" || undefined}
            disabled={open.isPending}
            className={fieldClass}
          >
            <option value="">No account in particular</option>
            {[...tradersAccounts]
              .sort((a, b) => b.number - a.number)
              .map((account) => (
                <option key={account.id} value={account.id}>
                  Account #{account.number} · {challengeName(account.challengeId)} · {account.stageName}
                </option>
              ))}
          </select>
        </div>
        <div className="flex flex-col gap-1 text-sm">
          <label htmlFor={subjectId} className="text-muted">
            Subject
          </label>
          <input
            id={subjectId}
            value={subject}
            onChange={(e) => setSubject(e.target.value)}
            maxLength={supportLimits.subject}
            placeholder="For example: Your EURUSD trade on 3 October"
            aria-invalid={field === "subject" || undefined}
            disabled={open.isPending}
            className={fieldClass}
          />
        </div>
        <MessageField value={body} onChange={setBody} label="Message" rows={7} invalid={field === "body"} disabled={open.isPending} />
        <FilePicker files={files} onChange={setFiles} disabled={open.isPending} />
        {problem && (
          <p role="alert" className="text-sm text-loss">
            {problem.text}
          </p>
        )}
        <ErrorText error={open.error} />
        <div className="flex flex-wrap items-center justify-end gap-2.5">
          <Link href="/admin/support" className={secondaryButtonClass}>
            Cancel
          </Link>
          <button type="submit" disabled={open.isPending} className={buttonClass}>
            {open.isPending ? "Sending..." : "Send"}
          </button>
        </div>
      </form>
    </AdminPage>
  );
}

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
            {data.openedBy === "Firm" ? `Ticket #${data.number} to ${trader}` : `Ticket #${data.number} from ${trader}`} · Opened {formatDateTime(data.createdAt)}
          </>
        }
        actions={
          <>
            <TicketStatusBadge ticket={data} viewer="admin" firmName={branding.name} />
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
