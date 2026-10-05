import { describe, expect, it } from "vitest";

import { attachmentUrl, authorName, charactersLeft, filesProblem, isImage, lastAuthorName, supportLimits, ticketStatus, traderTicketNote, waitingText } from "./support";

const names = { firm: "Demo Firm", trader: "anna@test.com", me: "admin@test.com" };

describe("a support ticket's status", () => {
  it("says who it waits for, to whoever looks", () => {
    expect(ticketStatus("Open", "trader", "Demo Firm")).toEqual({ label: "Waiting for Demo Firm", tone: "accent" });
    expect(ticketStatus("Open", "admin", "Demo Firm")).toEqual({ label: "Waiting for you", tone: "warning" });
    expect(ticketStatus("Answered", "trader", "Demo Firm")).toEqual({ label: "Answered", tone: "profit" });
    expect(ticketStatus("Answered", "admin", "Demo Firm")).toEqual({ label: "Waiting for the trader", tone: "accent" });
    expect(ticketStatus("Closed", "trader", "Demo Firm").label).toBe("Closed");
  });

  it("tells the trader what happens next", () => {
    expect(traderTicketNote("Open", "Demo Firm")).toContain("Demo Firm has your message");
    expect(traderTicketNote("Answered", "Demo Firm")).toContain("close the ticket if it is solved");
    expect(traderTicketNote("Closed", "Demo Firm")).toContain("Write in it to open it again");
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
