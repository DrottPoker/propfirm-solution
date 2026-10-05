"use client";

import { useId, useRef, useState } from "react";

import type { SupportTicket } from "@/lib/api/types";
import { formatDateTime } from "@/lib/format";
import { useWriteInTicket } from "@/lib/queries";
import { acceptedFiles, attachmentUrl, authorName, charactersLeft, filesProblem, isImage, supportLimits, ticketStatus, type SupportViewer, type TicketState } from "@/lib/support";
import { fileSize } from "@/lib/verification";

import { CloseIcon, FileIcon, PaperclipIcon } from "./icons";
import { Badge, buttonClass, ErrorText, fieldClass, secondaryButtonClass } from "./ui";

/** Where a ticket is, as a badge, worded for whoever looks at it. */
export function TicketStatusBadge({ ticket, viewer, firmName }: { ticket: TicketState; viewer: SupportViewer; firmName: string }) {
  const { label, tone } = ticketStatus(ticket, viewer, firmName);
  return <Badge tone={tone}>{label}</Badge>;
}

/**
 * The ticket's messages, oldest first, each with who wrote it, when and its files. The viewer's own messages stand out.
 * Pictures are shown small, and every file opens as a download.
 */
export function Conversation({ ticket, viewer, names }: { ticket: SupportTicket; viewer: SupportViewer; names: { firm: string; trader: string; me: string } }) {
  return (
    <ol aria-label="Messages" className="flex flex-col gap-3">
      {ticket.messages.map((message) => {
        const own = viewer === "trader" ? message.author === "Trader" : message.author === "Firm";
        return (
          <li key={message.id} className={`flex ${own ? "sm:justify-end" : ""}`}>
            <article className={`flex w-full flex-col gap-2 rounded-lg border px-4 py-3.5 sm:w-[85%] ${own ? "border-accent/40 bg-accent/5" : "border-border bg-panel"}`}>
              <header className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5 text-sm">
                <span className="font-medium">{authorName(message, viewer, names)}</span>
                <time dateTime={message.createdAt} className="text-xs text-muted">
                  {formatDateTime(message.createdAt)}
                </time>
              </header>
              <p className="whitespace-pre-wrap break-words">{message.body}</p>
              {message.attachments.length > 0 && (
                <ul aria-label="Files" className="flex flex-wrap gap-2.5 pt-1">
                  {message.attachments.map((file) => {
                    const href = attachmentUrl(file.id, viewer);
                    return (
                      <li key={file.id}>
                        {isImage(file.contentType) ? (
                          <a href={href} download={file.fileName} title={`Download ${file.fileName}`} className="block overflow-hidden rounded-md border border-border hover:border-muted">
                            {/* eslint-disable-next-line @next/next/no-img-element -- a file behind the session, which next/image cannot fetch */}
                            <img src={href} alt={file.fileName} className="h-24 w-auto max-w-56 bg-background object-contain" />
                          </a>
                        ) : (
                          <a href={href} download={file.fileName} className="flex items-center gap-2 rounded-md border border-border bg-background px-3 py-2 text-sm hover:border-muted">
                            <FileIcon className="size-4 shrink-0 text-muted" />
                            <span className="max-w-48 truncate">{file.fileName}</span>
                            <span className="text-xs text-muted">{fileSize(file.size)}</span>
                          </a>
                        )}
                      </li>
                    );
                  })}
                </ul>
              )}
            </article>
          </li>
        );
      })}
    </ol>
  );
}

/** Files chosen for a message, each with a button that takes it away, and a button that adds more. */
export function FilePicker({ files, onChange, disabled }: { files: File[]; onChange: (files: File[]) => void; disabled?: boolean }) {
  const input = useRef<HTMLInputElement>(null);
  const full = files.length >= supportLimits.files;
  return (
    <div className="flex flex-wrap items-center gap-2 text-sm">
      {files.map((file, index) => (
        <span key={`${file.name}-${index}`} className="flex items-center gap-1.5 rounded-md border border-border bg-background py-1 pl-2.5 pr-1">
          <span className="max-w-48 truncate">{file.name}</span>
          <span className="text-xs text-muted">{fileSize(file.size)}</span>
          <button
            type="button"
            aria-label={`Remove ${file.name}`}
            disabled={disabled}
            onClick={() => onChange(files.filter((_, i) => i !== index))}
            className="grid size-6 place-items-center rounded text-muted hover:bg-panel hover:text-foreground"
          >
            <CloseIcon className="size-3.5" />
          </button>
        </span>
      ))}
      {!full && (
        <button type="button" disabled={disabled} onClick={() => input.current?.click()} className="flex items-center gap-1.5 rounded-md px-2 py-1.5 text-muted hover:text-foreground">
          <PaperclipIcon className="size-4" />
          {files.length === 0 ? "Add files" : "Add more"}
        </button>
      )}
      <input
        ref={input}
        type="file"
        multiple
        aria-label="Add files"
        accept={acceptedFiles}
        disabled={disabled}
        onChange={(e) => {
          onChange([...files, ...Array.from(e.target.files ?? [])]);
          e.target.value = "";
        }}
        className="sr-only"
      />
      <span className="text-xs text-muted">Up to {supportLimits.files} PDF, PNG or JPEG files of 5 MB, such as screenshots.</span>
    </div>
  );
}

/** The message box, its characters left near the limit, and why it cannot be sent. */
export function MessageField({
  value,
  onChange,
  label,
  placeholder,
  invalid,
  disabled,
  rows = 5,
}: {
  value: string;
  onChange: (value: string) => void;
  label: string;
  placeholder?: string;
  invalid?: boolean;
  disabled?: boolean;
  rows?: number;
}) {
  const id = useId();
  const left = charactersLeft(value, supportLimits.message);
  return (
    <div className="flex flex-col gap-1 text-sm">
      <label htmlFor={id} className="text-muted">
        {label}
      </label>
      <textarea
        id={id}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        rows={rows}
        placeholder={placeholder}
        aria-invalid={invalid || (left !== null && left < 0) || undefined}
        disabled={disabled}
        className={`${fieldClass} resize-y`}
      />
      {left !== null && <span className={`self-end text-xs ${left < 0 ? "text-loss" : "text-muted"}`}>{left < 0 ? `${-left} characters too many` : `${left} characters left`}</span>}
    </div>
  );
}

/**
 * Writes in the ticket. The trader's message opens a closed ticket again. An administrator can answer and close the
 * ticket at once, for an answer that solves it.
 */
export function MessageForm({ ticket, viewer }: { ticket: SupportTicket; viewer: SupportViewer }) {
  const write = useWriteInTicket(viewer, ticket.id);
  const [body, setBody] = useState("");
  const [files, setFiles] = useState<File[]>([]);
  const [problem, setProblem] = useState<string | null>(null);
  const closed = ticket.status === "Closed";

  const send = (close: boolean) => {
    const filesWrong = filesProblem(files);
    if (body.trim().length === 0 || body.length > supportLimits.message || filesWrong) {
      setProblem(filesWrong ?? (body.trim().length === 0 ? "Write a message." : `Keep the message to ${supportLimits.message.toLocaleString("en-GB")} characters.`));
      return;
    }

    setProblem(null);
    write.mutate(
      { body, files, close },
      {
        onSuccess: () => {
          setBody("");
          setFiles([]);
        },
      },
    );
  };

  return (
    <form
      aria-label={viewer === "admin" ? "Answer the trader" : "Write in the ticket"}
      onSubmit={(event) => {
        event.preventDefault();
        send(false);
      }}
      className="flex flex-col gap-3 rounded-lg border border-border bg-panel p-4"
    >
      <MessageField
        value={body}
        onChange={setBody}
        label={viewer === "admin" ? (closed ? "Write to the trader again" : "Your answer") : closed ? "Write to open the ticket again" : "Write back"}
        placeholder={viewer === "admin" ? "The trader gets your answer by email, in your firm's name." : undefined}
        invalid={problem !== null && body.trim().length === 0}
        disabled={write.isPending}
      />
      <FilePicker files={files} onChange={setFiles} disabled={write.isPending} />
      {problem && (
        <p role="alert" className="text-sm text-loss">
          {problem}
        </p>
      )}
      <ErrorText error={write.error} />
      <div className="flex flex-wrap items-center justify-end gap-2.5">
        {viewer === "admin" && !closed && (
          <button type="button" disabled={write.isPending} onClick={() => send(true)} className={secondaryButtonClass}>
            Send and close
          </button>
        )}
        <button type="submit" disabled={write.isPending} className={buttonClass}>
          {write.isPending ? "Sending..." : "Send"}
        </button>
      </div>
    </form>
  );
}
