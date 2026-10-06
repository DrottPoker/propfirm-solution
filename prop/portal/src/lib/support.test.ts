import { describe, expect, it } from "vitest";

import {
  attachmentUrl,
  authorName,
  charactersLeft,
  filesProblem,
  fillReply,
  findReplies,
  insertText,
  isImage,
  lastAuthorName,
  replyPreview,
  replyValues,
  savedReplyLimits,
  savedReplyProblem,
  supportLimits,
  ticketAccountStatus,
  ticketStatus,
  traderTicketNote,
  waitingText,
} from "./support";

const names = { firm: "Demo Firm", trader: "anna@test.com", me: "admin@test.com" };

describe("a support ticket's status", () => {
  it("says who it waits for, to whoever looks", () => {
    expect(ticketStatus({ status: "Open", openedBy: "Trader" }, "trader", "Demo Firm")).toEqual({ label: "Waiting for Demo Firm", tone: "accent" });
    expect(ticketStatus({ status: "Open", openedBy: "Trader" }, "admin", "Demo Firm")).toEqual({ label: "Waiting for you", tone: "warning" });
    expect(ticketStatus({ status: "Answered", openedBy: "Trader" }, "trader", "Demo Firm")).toEqual({ label: "Answered", tone: "profit" });
    expect(ticketStatus({ status: "Answered", openedBy: "Trader" }, "admin", "Demo Firm")).toEqual({ label: "Waiting for the trader", tone: "accent" });
    expect(ticketStatus({ status: "Closed", openedBy: "Trader" }, "trader", "Demo Firm").label).toBe("Closed");
  });

  it("is a message from the firm, not an answer, when the firm wrote first", () => {
    expect(ticketStatus({ status: "Answered", openedBy: "Firm" }, "trader", "Demo Firm")).toEqual({ label: "Message from Demo Firm", tone: "accent" });
    expect(ticketStatus({ status: "Answered", openedBy: "Firm" }, "admin", "Demo Firm").label).toBe("Waiting for the trader");
    expect(ticketStatus({ status: "Open", openedBy: "Firm" }, "trader", "Demo Firm").label).toBe("Waiting for Demo Firm");
  });

  it("tells the trader what happens next", () => {
    expect(traderTicketNote({ status: "Open", openedBy: "Trader" }, "Demo Firm")).toContain("Demo Firm has your message");
    expect(traderTicketNote({ status: "Answered", openedBy: "Trader" }, "Demo Firm")).toContain("close the ticket if it is solved");
    expect(traderTicketNote({ status: "Answered", openedBy: "Firm" }, "Demo Firm")).toBe("Demo Firm wrote to you. Answer here, or close the ticket if nothing more is needed.");
    expect(traderTicketNote({ status: "Closed", openedBy: "Firm" }, "Demo Firm")).toContain("Write in it to open it again");
  });
});

describe("who wrote a message", () => {
  it("is the firm to the trader, never which administrator", () => {
    expect(authorName({ author: "Trader", adminEmail: null }, "trader", names)).toBe("You");
    expect(authorName({ author: "Firm", adminEmail: null }, "trader", names)).toBe("Demo Firm");
  });

  it("is the trader or the administrator in the admin panel", () => {
    expect(authorName({ author: "Trader", adminEmail: null }, "admin", names)).toBe("anna@test.com");
    expect(authorName({ author: "Firm", adminEmail: "Admin@Test.com" }, "admin", names)).toBe("You");
    expect(authorName({ author: "Firm", adminEmail: "bea@test.com" }, "admin", names)).toBe("bea@test.com");
  });

  it("is named in a list by who wrote last", () => {
    expect(lastAuthorName({ lastAuthor: "Trader" }, "trader", "Demo Firm")).toBe("You");
    expect(lastAuthorName({ lastAuthor: "Firm" }, "trader", "Demo Firm")).toBe("Demo Firm");
    expect(lastAuthorName({ lastAuthor: "Trader" }, "admin", "Demo Firm")).toBe("Trader");
    expect(lastAuthorName({ lastAuthor: "Firm" }, "admin", "Demo Firm")).toBe("Your team");
  });
});

describe("the files of a message", () => {
  const png = { name: "screen.png", size: 1_000, type: "image/png" };

  it("are at most three PDF, PNG or JPEG files of 5 MB", () => {
    expect(filesProblem([])).toBeNull();
    expect(filesProblem([png, { name: "bank.PDF", size: 2_000, type: "" }, { name: "photo.jpeg", size: 3_000, type: "image/jpeg" }])).toBeNull();
    expect(filesProblem([png, png, png, png])).toBe("Add at most 3 files to a message.");
    expect(filesProblem([{ ...png, name: "huge.png", size: supportLimits.fileBytes + 1 }])).toBe("huge.png is larger than 5 MB.");
    expect(filesProblem([{ name: "notes.txt", size: 10, type: "text/plain" }])).toBe("notes.txt is not a PDF, PNG or JPEG file.");
  });

  it("are shown as pictures when they are images, from the viewer's own address", () => {
    expect(isImage("image/png")).toBe(true);
    expect(isImage("application/pdf")).toBe(false);
    expect(attachmentUrl("abc", "trader")).toBe("/api/portal/support/attachments/abc");
    expect(attachmentUrl("abc", "admin")).toBe("/api/portal/admin/support/attachments/abc");
  });
});

describe("the characters left of a message", () => {
  it("are shown only near the limit", () => {
    expect(charactersLeft("a".repeat(100), 5_000)).toBeNull();
    expect(charactersLeft("a".repeat(4_600), 5_000)).toBe(400);
    expect(charactersLeft("a".repeat(5_010), 5_000)).toBe(-10);
  });
});

describe("how long a ticket has waited", () => {
  const now = Date.parse("2026-10-07T12:00:00Z");

  it("is said in minutes, hours or days", () => {
    expect(waitingText("2026-10-07T11:59:40Z", now)).toBe("Waiting a moment");
    expect(waitingText("2026-10-07T11:48:00Z", now)).toBe("Waiting 12 min");
    expect(waitingText("2026-10-07T11:00:00Z", now)).toBe("Waiting 1 hour");
    expect(waitingText("2026-10-07T07:30:00Z", now)).toBe("Waiting 4 hours");
    expect(waitingText("2026-10-06T10:00:00Z", now)).toBe("Waiting 1 day");
    expect(waitingText("2026-10-04T12:00:00Z", now)).toBe("Waiting 3 days");
  });
});

describe("where the account a ticket is about is", () => {
  it("is its stage and its status, as in the list of accounts", () => {
    expect(ticketAccountStatus({ status: "Active", stageName: "Phase 1", paused: false })).toEqual({ stage: "Phase 1", label: "Trading", tone: "accent" });
    expect(ticketAccountStatus({ status: "Active", stageName: "Funded", paused: true })).toEqual({ stage: "Funded", label: "Paused", tone: "warning" });
    expect(ticketAccountStatus({ status: "Failed", stageName: "Phase 2", paused: false })).toEqual({ stage: "Phase 2", label: "Failed", tone: "loss" });
    expect(ticketAccountStatus({ status: "Cancelled", stageName: "Phase 1", paused: false })).toEqual({ stage: "Phase 1", label: "Cancelled", tone: "muted" });
    expect(ticketAccountStatus({ status: "OpeningAccount", stageName: "Phase 2", paused: false }).label).toBe("Opening its account");
  });

  it("has passed every stage while it waits for the firm's approval", () => {
    expect(ticketAccountStatus({ status: "AwaitingFunding", stageName: "Phase 2", paused: false })).toEqual({
      stage: "Every stage passed",
      label: "Waiting for your approval",
      tone: "warning",
    });
  });
});

describe("a saved reply", () => {
  const values = { trader: "Anna Berg", firm: "Demo Firm" };

  it("has the trader's and the firm's names filled in, wherever and however often they are", () => {
    expect(fillReply("Hi {trader},\nthanks for writing to {firm}.\n{firm}", values)).toBe("Hi Anna Berg,\nthanks for writing to Demo Firm.\nDemo Firm");
    expect(fillReply("Hi {Trader} from {FIRM}", values)).toBe("Hi Anna Berg from Demo Firm");
  });

  it("leaves other braces, and names that are not placeholders, as they are", () => {
    expect(fillReply("Use {account} or { trader } or {{trader}}", values)).toBe("Use {account} or { trader } or {Anna Berg}");
    expect(fillReply("Costs $& and $1 at {firm}", { trader: "$&", firm: "$1" })).toBe("Costs $& and $1 at $1");
  });

  it("names the trader by name, or by email when the trader has none", () => {
    expect(replyValues({ traderName: "Anna Berg", traderEmail: "anna@test.com" }, "Demo Firm")).toEqual(values);
    expect(replyValues({ traderName: null, traderEmail: "anna@test.com" }, "Demo Firm").trader).toBe("anna@test.com");
    expect(replyValues({ traderName: "  ", traderEmail: "anna@test.com" }, "Demo Firm").trader).toBe("anna@test.com");
  });

  it("is found by its title or text, in any case", () => {
    const replies = [
      { title: "Payout times", body: "Payouts are paid within 2 days." },
      { title: "Terminal", body: "Choose the server in the terminal." },
    ];
    expect(findReplies(replies, "")).toEqual(replies);
    expect(findReplies(replies, "  PAYOUT ")).toEqual([replies[0]]);
    expect(findReplies(replies, "server")).toEqual([replies[1]]);
    expect(findReplies(replies, "refund")).toEqual([]);
  });

  it("is previewed on one line", () => {
    expect(replyPreview("Hi {trader},\n\npayouts are paid\twithin 2 days.")).toBe("Hi {trader}, payouts are paid within 2 days.");
    expect(replyPreview("a ".repeat(60), 20)).toBe("a a a a a a a a a...");
  });

  it("needs a title and a text within their limits", () => {
    expect(savedReplyProblem(" ", "Hello")).toEqual({ field: "title", text: "Write a title to find the reply by." });
    expect(savedReplyProblem("a".repeat(savedReplyLimits.title + 1), "Hello")?.field).toBe("title");
    expect(savedReplyProblem("Greeting", " \n ")).toEqual({ field: "body", text: "Write the reply." });
    expect(savedReplyProblem("Greeting", "a".repeat(savedReplyLimits.body + 1))).toEqual({ field: "body", text: "Keep the reply to 4,000 characters." });
    expect(savedReplyProblem(` ${"a".repeat(savedReplyLimits.title)} `, ` ${"a".repeat(savedReplyLimits.body)}\n`)).toBeNull();
  });
});

describe("putting a saved reply in an answer", () => {
  it("replaces an empty answer", () => {
    expect(insertText("", "Hi Anna", 0, 0)).toEqual({ value: "Hi Anna", cursor: 7 });
    expect(insertText(" \n ", "Hi Anna", 3, 3)).toEqual({ value: "Hi Anna", cursor: 7 });
  });

  it("goes in at the cursor, with a space where it would run into a word", () => {
    expect(insertText("Hello\n", "Payouts take 2 days.", 6, 6)).toEqual({ value: "Hello\nPayouts take 2 days.", cursor: 26 });
    expect(insertText("Hello.", "Payouts take 2 days.", 6, 6)).toEqual({ value: "Hello. Payouts take 2 days.", cursor: 27 });
    expect(insertText("Hello. Bye.", "Payouts take 2 days.", 7, 7)).toEqual({ value: "Hello. Payouts take 2 days. Bye.", cursor: 27 });
    expect(insertText("Bye.", "Hello.", 0, 0)).toEqual({ value: "Hello. Bye.", cursor: 6 });
  });

  it("takes the place of what is selected", () => {
    expect(insertText("Hello XXX bye", "Anna", 6, 9)).toEqual({ value: "Hello Anna bye", cursor: 10 });
    expect(insertText("Hello", "Anna", 9, 2)).toEqual({ value: "Hello Anna", cursor: 10 });
  });
});
