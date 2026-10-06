import { accountStatus, stageLabel, type StatusTone } from "./admin";
import type { SavedReply, SupportAccount, SupportAuthor, SupportMessage, SupportTicket, SupportTicketGroup, SupportTicketStatus, SupportTicketSummary } from "./api/types";

// Support tickets between traders and their firm (ADR 0041). Prop.Api keeps them; this words and checks them.

/** The service's limits, so the portal can say what is wrong before anything is sent. The service checks them again. */
export const supportLimits = { subject: 120, message: 5_000, files: 3, fileBytes: 5 * 1024 * 1024 } as const;

/** The files a message can have, for the file input. */
export const acceptedFiles = "application/pdf,image/png,image/jpeg,.pdf,.png,.jpg,.jpeg";

/** Who looks at a ticket: its trader, or one of the firm's administrators. */
export type SupportViewer = "trader" | "admin";

/** Where a ticket is and who opened it. */
export type TicketState = { status: SupportTicketStatus; openedBy: SupportAuthor };

/**
 * Where a ticket is, in a few words for whoever looks at it, with its tone. A ticket the firm opened that waits for the
 * trader is a message from the firm, not an answer.
 */
export function ticketStatus(ticket: TicketState, viewer: SupportViewer, firmName: string): { label: string; tone: StatusTone } {
  switch (ticket.status) {
    case "Open":
      return viewer === "admin" ? { label: "Waiting for you", tone: "warning" } : { label: `Waiting for ${firmName}`, tone: "accent" };
    case "Answered":
      return viewer === "admin"
        ? { label: "Waiting for the trader", tone: "accent" }
        : ticket.openedBy === "Firm"
          ? { label: `Message from ${firmName}`, tone: "accent" }
          : { label: "Answered", tone: "profit" };
    default:
      return { label: "Closed", tone: "muted" };
  }
}

/** The admin panel's groups of tickets, in the order of its filter. */
export const supportGroups: SupportTicketGroup[] = ["Open", "Answered", "Closed", "All"];

export const supportGroupLabels: Record<SupportTicketGroup, string> = {
  Open: "Waiting for you",
  Answered: "Waiting for the trader",
  Closed: "Closed",
  All: "All",
};

/**
 * Who wrote a message, as the viewer reads it. A trader sees "You" and the firm's name, never which administrator
 * answered. An administrator sees the trader, and "You" or the administrator who answered.
 */
export function authorName(message: Pick<SupportMessage, "author" | "adminEmail">, viewer: SupportViewer, names: { firm: string; trader: string; me: string }): string {
  if (message.author === "Trader") {
    return viewer === "trader" ? "You" : names.trader;
  }

  if (viewer === "trader") {
    return names.firm;
  }

  return message.adminEmail && message.adminEmail.toLowerCase() === names.me.toLowerCase() ? "You" : (message.adminEmail ?? names.firm);
}

/** Who wrote last in a ticket, for a list, for example "You" or "Demo Firm". The admin panel's list shows the trader beside it. */
export function lastAuthorName(ticket: Pick<SupportTicketSummary, "lastAuthor">, viewer: SupportViewer, firmName: string): string {
  if (ticket.lastAuthor === "Trader") {
    return viewer === "trader" ? "You" : "Trader";
  }

  return viewer === "trader" ? firmName : "Your team";
}

/** How long a ticket has waited for the firm, for example "Waiting 12 min", "Waiting 4 hours" or "Waiting 2 days". */
export function waitingText(since: string, now: number): string {
  const minutes = Math.max(0, Math.floor((now - Date.parse(since)) / 60_000));
  if (minutes < 60) {
    return minutes < 1 ? "Waiting a moment" : `Waiting ${minutes} min`;
  }

  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return hours === 1 ? "Waiting 1 hour" : `Waiting ${hours} hours`;
  }

  const days = Math.floor(hours / 24);
  return days === 1 ? "Waiting 1 day" : `Waiting ${days} days`;
}

/** What the trader is told about where a ticket is, under its messages. */
export function traderTicketNote(ticket: TicketState, firmName: string): string {
  switch (ticket.status) {
    case "Open":
      return `${firmName} has your message and answers here. You also get an email when it does.`;
    case "Answered":
      return ticket.openedBy === "Firm"
        ? `${firmName} wrote to you. Answer here, or close the ticket if nothing more is needed.`
        : `${firmName} has answered. Write back if you need more help, or close the ticket if it is solved.`;
    default:
      return "This ticket is closed. Write in it to open it again.";
  }
}

/** Why the files cannot be added to a message, or null when they can. */
export function filesProblem(files: readonly { name: string; size: number; type: string }[]): string | null {
  if (files.length > supportLimits.files) {
    return `Add at most ${supportLimits.files} files to a message.`;
  }

  const tooLarge = files.find((f) => f.size > supportLimits.fileBytes);
  if (tooLarge) {
    return `${tooLarge.name} is larger than 5 MB.`;
  }

  const wrongType = files.find((f) => !isAccepted(f));
  return wrongType ? `${wrongType.name} is not a PDF, PNG or JPEG file.` : null;
}

/** Whether the file can be shown as a picture in the conversation. */
export function isImage(contentType: string): boolean {
  return contentType === "image/png" || contentType === "image/jpeg";
}

/** Where a file in a ticket is fetched from, by the trader or in the admin panel. */
export function attachmentUrl(attachmentId: string, viewer: SupportViewer): string {
  return viewer === "admin" ? `/api/portal/admin/support/attachments/${attachmentId}` : `/api/portal/support/attachments/${attachmentId}`;
}

/** How many characters are left of a limit, shown once fewer than a tenth are. Null before that. */
export function charactersLeft(text: string, limit: number): number | null {
  const left = limit - text.length;
  return left < limit / 10 ? left : null;
}

/**
 * Where the account a ticket is about is now, worded as in the admin panel's list of accounts: the stage it is on, for
 * example "Phase 1" or "Funded", and its status with its tone.
 */
export function ticketAccountStatus(account: Pick<SupportAccount, "status" | "stageName" | "paused">): { stage: string; label: string; tone: StatusTone } {
  return { stage: stageLabel(account), ...accountStatus(account) };
}

// Saved replies: answers the firm's administrators save, and put in an answer with the placeholders filled in.

/** The service's limits of saved replies, so the portal can say what is wrong before anything is sent. The service checks them again. */
export const savedReplyLimits = { title: 80, body: 4_000, count: 100 } as const;

/** From this many saved replies, their list has a search. */
export const savedReplySearchFrom = 9;

/** What fills in a saved reply's placeholders: {trader} and {firm}. */
export type ReplyValues = { trader: string; firm: string };

/** The values for a saved reply in the ticket: the trader's name, or the email when the trader has none, and the firm's name. */
export function replyValues(ticket: Pick<SupportTicket, "traderName" | "traderEmail">, firmName: string): ReplyValues {
  return { trader: ticket.traderName?.trim() || ticket.traderEmail, firm: firmName };
}

/** A saved reply's text with {trader} and {firm} filled in, in any case. Other braces stay as they are. */
export function fillReply(body: string, values: ReplyValues): string {
  return body.replace(/\{(trader|firm)\}/gi, (_, name: string) => (name.toLowerCase() === "trader" ? values.trader : values.firm));
}

/**
 * The answer with the text put in at the cursor, or in place of what is selected, and where the cursor goes after it.
 * An empty answer is replaced. A space keeps the text from running into the words on either side.
 */
export function insertText(current: string, text: string, start: number, end: number): { value: string; cursor: number } {
  if (current.trim() === "") {
    return { value: text, cursor: text.length };
  }

  const from = Math.min(Math.max(0, start), current.length);
  const to = Math.min(Math.max(from, end), current.length);
  const before = current.slice(0, from);
  const after = current.slice(to);
  const lead = before !== "" && !/\s$/.test(before) ? " " : "";
  const trail = after !== "" && !/^\s/.test(after) ? " " : "";
  return { value: `${before}${lead}${text}${trail}${after}`, cursor: before.length + lead.length + text.length };
}

/** The saved replies whose title or text has the search in it, in any case. All of them without a search. */
export function findReplies<T extends Pick<SavedReply, "title" | "body">>(replies: readonly T[], search: string): T[] {
  const text = search.trim().toLowerCase();
  return text === "" ? [...replies] : replies.filter((r) => r.title.toLowerCase().includes(text) || r.body.toLowerCase().includes(text));
}

/** The start of a saved reply on one line, for a list of them. */
export function replyPreview(body: string, length = 90): string {
  const line = body.split(/\s+/).filter(Boolean).join(" ");
  return line.length <= length ? line : `${line.slice(0, length - 3).trimEnd()}...`;
}

/** Why the saved reply cannot be saved, and the field it is about, or null when it can. */
export function savedReplyProblem(title: string, body: string): { field: "title" | "body"; text: string } | null {
  const cleanTitle = title.trim();
  const cleanBody = body.trim();
  if (cleanTitle.length === 0) {
    return { field: "title", text: "Write a title to find the reply by." };
  }

  if (cleanTitle.length > savedReplyLimits.title) {
    return { field: "title", text: `Keep the title to ${savedReplyLimits.title} characters.` };
  }

  if (cleanBody.length === 0) {
    return { field: "body", text: "Write the reply." };
  }

  return cleanBody.length > savedReplyLimits.body ? { field: "body", text: `Keep the reply to ${savedReplyLimits.body.toLocaleString("en-GB")} characters.` } : null;
}

// Browsers name some types, and only the extension tells for others.
function isAccepted(file: { name: string; type: string }): boolean {
  return ["application/pdf", "image/png", "image/jpeg"].includes(file.type) || /\.(pdf|png|jpe?g)$/i.test(file.name);
}
