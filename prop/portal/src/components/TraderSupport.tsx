"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useId, useState } from "react";

import { useBranding } from "@/app/providers";

import { whenText } from "@/lib/admin";
import { formatDateTime } from "@/lib/format";
import { FieldError, useCloseTicket, useMarkTicketRead, useMe, useMyAccounts, useMyTicket, useMyTickets, useOpenTicket } from "@/lib/queries";
import { filesProblem, lastAuthorName, supportLimits, traderTicketNote } from "@/lib/support";

import { PlusIcon } from "./icons";
import { Conversation, FilePicker, MessageField, MessageForm, TicketStatusBadge } from "./SupportThread";
import { buttonClass, ErrorText, fieldClass, Message, secondaryButtonClass } from "./ui";

/** The trader's tickets to the firm, the latest written in first, with a way to open a new one. */
export function TraderTickets() {
  const tickets = useMyTickets();
  const branding = useBranding();

  if (tickets.isError) {
    return <Message text="Your tickets cannot be loaded right now. Try again shortly." />;
  }

  if (!tickets.data) {
    return <Message text="Loading..." />;
  }

  const rows = tickets.data.pages.flatMap((p) => p.tickets);
  const now = tickets.dataUpdatedAt;
  return (
    <main className="mx-auto flex w-full max-w-4xl flex-col gap-6 px-4 py-8 sm:px-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight">Support</h1>
          <p className="text-muted">Ask {branding.name} about your accounts, payouts or anything else. The answers come here, and by email.</p>
        </div>
        <Link href="/support/new" className={`${buttonClass} flex items-center gap-1.5`}>
          <PlusIcon className="size-4" />
          New ticket
        </Link>
      </div>

      {rows.length === 0 ? (
        <p className="rounded-lg border border-border bg-panel px-5 py-8 text-center text-muted">No tickets yet. Ask {branding.name} a question with New ticket.</p>
      ) : (
        <section aria-label="Your tickets" className="flex flex-col rounded-lg border border-border bg-panel">
          <ul className="flex flex-col">
            {rows.map((ticket) => (
              <li key={ticket.id} className="border-t border-border first:border-t-0">
                <Link href={`/support/${ticket.id}`} className="flex items-start gap-3 px-4 py-3.5 hover:bg-background/50 sm:px-5">
                  <span aria-hidden="true" className={`mt-2 size-2 shrink-0 rounded-full ${ticket.unread ? "bg-accent" : "bg-transparent"}`} />
                  <span className="flex min-w-0 flex-1 flex-col gap-1">
                    <span className="flex flex-wrap items-center gap-x-2.5 gap-y-1">
                      <span className={`min-w-0 truncate ${ticket.unread ? "font-semibold" : "font-medium"}`}>{ticket.subject}</span>
                      {ticket.unread && <span className="sr-only">(new answer)</span>}
                      <TicketStatusBadge status={ticket.status} viewer="trader" firmName={branding.name} />
                    </span>
                    <span className="truncate text-sm text-muted">
                      {lastAuthorName(ticket, "trader", branding.name)}: {ticket.preview}
                    </span>
                    <span className="text-xs text-muted">
                      #{ticket.number}
                      {ticket.account && <> · Account #{ticket.account.number}</>}
                    </span>
                  </span>
                  <time dateTime={ticket.updatedAt} className="shrink-0 text-xs text-muted">
                    {whenText(ticket.updatedAt, now)}
                  </time>
                </Link>
              </li>
            ))}
          </ul>
          {tickets.hasNextPage && (
            <div className="border-t border-border px-4 py-3 text-center">
              <button type="button" onClick={() => tickets.fetchNextPage()} disabled={tickets.isFetchingNextPage} className={secondaryButtonClass}>
                {tickets.isFetchingNextPage ? "Loading..." : "Show more"}
              </button>
            </div>
          )}
        </section>
      )}
    </main>
  );
}

/** A new ticket: what it is about, optionally which of the trader's accounts, the question and files. */
export function NewTicket({ initialAccountId }: { initialAccountId: string | null }) {
  const branding = useBranding();
  const accounts = useMyAccounts();
  const open = useOpenTicket();
  const router = useRouter();
  const subjectId = useId();
  const accountFieldId = useId();
  const [subject, setSubject] = useState("");
  const [accountId, setAccountId] = useState(initialAccountId ?? "");
  const [body, setBody] = useState("");
  const [files, setFiles] = useState<File[]>([]);
  const [problem, setProblem] = useState<{ field: string; text: string } | null>(null);

  const field = problem?.field ?? (open.error instanceof FieldError ? open.error.field : null);
  const choices = [...(accounts.data ?? [])].sort((a, b) => b.account.number - a.account.number);

  return (
    <main className="mx-auto flex w-full max-w-3xl flex-col gap-6 px-4 py-6 sm:px-6">
      <nav aria-label="Breadcrumb" className="text-sm text-muted">
        <Link href="/support" className="hover:text-foreground">
          Support
        </Link>{" "}
        <span aria-hidden="true">/</span> <span className="text-foreground">New ticket</span>
      </nav>
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">New ticket</h1>
        <p className="text-muted">{branding.name} answers here, and you get an email when it does.</p>
      </div>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          const subjectText = subject.trim();
          const filesWrong = filesProblem(files);
          const found =
            subjectText.length === 0
              ? { field: "subject", text: "Write what your question is about." }
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
            open.mutate({ subject: subjectText, body, accountId: accountId || null, files }, { onSuccess: (ticket) => router.replace(`/support/${ticket.id}`) });
          }
        }}
        className="flex flex-col gap-4 rounded-lg border border-border bg-panel p-5"
      >
        <div className="flex flex-col gap-1 text-sm">
          <label htmlFor={subjectId} className="text-muted">
            Subject
          </label>
          <input
            id={subjectId}
            value={subject}
            onChange={(e) => setSubject(e.target.value)}
            maxLength={supportLimits.subject}
            placeholder="For example: When is my payout paid?"
            aria-invalid={field === "subject" || undefined}
            disabled={open.isPending}
            className={fieldClass}
          />
        </div>
        <div className="flex flex-col gap-1 text-sm">
          <label htmlFor={accountFieldId} className="text-muted">
            About
          </label>
          <select
            id={accountFieldId}
            value={accountId}
            onChange={(e) => setAccountId(e.target.value)}
            aria-invalid={field === "accountId" || undefined}
            disabled={open.isPending}
            className={fieldClass}
          >
            <option value="">No account in particular</option>
            {choices.map((details) => (
              <option key={details.account.id} value={details.account.id}>
                Account #{details.account.number} · {details.challenge.name} · {details.account.stageName}
              </option>
            ))}
          </select>
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
          <Link href="/support" className={secondaryButtonClass}>
            Cancel
          </Link>
          <button type="submit" disabled={open.isPending} className={buttonClass}>
            {open.isPending ? "Sending..." : "Send"}
          </button>
        </div>
      </form>
    </main>
  );
}

/** One of the trader's tickets: the conversation, where it is, and a way to write back or close it. */
export function TraderTicket({ ticketId }: { ticketId: string }) {
  const ticket = useMyTicket(ticketId);
  const me = useMe("trader");
  const branding = useBranding();
  const markRead = useMarkTicketRead(ticketId);
  const close = useCloseTicket("trader", ticketId);
  const unread = ticket.data?.unread === true;
  const { mutate: markAsRead } = markRead;

  // Seeing an answer reads it, also one that arrives while the page is open.
  useEffect(() => {
    if (unread) {
      markAsRead();
    }
  }, [unread, markAsRead]);

  if (ticket.isError) {
    return <Message text={ticket.error.message} />;
  }

  if (!ticket.data) {
    return <Message text="Loading..." />;
  }

  const data = ticket.data;
  return (
    <main className="mx-auto flex w-full max-w-4xl flex-col gap-5 px-4 py-6 sm:px-6">
      <nav aria-label="Breadcrumb" className="text-sm text-muted">
        <Link href="/support" className="hover:text-foreground">
          Support
        </Link>{" "}
        <span aria-hidden="true">/</span> <span className="text-foreground">#{data.number}</span>
      </nav>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex min-w-0 flex-col gap-1.5">
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="break-words text-2xl font-semibold tracking-tight">{data.subject}</h1>
            <TicketStatusBadge status={data.status} viewer="trader" firmName={branding.name} />
          </div>
          <p className="text-sm text-muted">
            Ticket #{data.number}
            {data.account && (
              <>
                {" "}
                · About{" "}
                <Link href={`/accounts/${data.account.id}`} className="text-accent hover:underline">
                  account #{data.account.number}
                </Link>
              </>
            )}{" "}
            · Opened {formatDateTime(data.createdAt)}
          </p>
        </div>
        {data.status !== "Closed" && (
          <button type="button" disabled={close.isPending} onClick={() => close.mutate()} className={secondaryButtonClass}>
            {close.isPending ? "Closing..." : "Close ticket"}
          </button>
        )}
      </div>
      <ErrorText error={close.error} />

      <Conversation ticket={data} viewer="trader" names={{ firm: branding.name, trader: me.data?.email ?? "You", me: me.data?.email ?? "" }} />
      <p className="text-sm text-muted">{traderTicketNote(data.status, branding.name)}</p>
      <MessageForm ticket={data} viewer="trader" />
    </main>
  );
}
