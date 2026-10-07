import { describe, expect, it } from "vitest";

import type { FirmIncident, IncidentAccount, IncidentDecision, StatusIncident } from "./api/types";
import {
  agoText,
  balanceChoices,
  decisionText,
  durationText,
  filterAccounts,
  fromLocalInput,
  incidentSummary,
  needsDecision,
  periodText,
  statusDays,
  toLocalInput,
  whatHappened,
} from "./incidents";

const account = (overrides: Partial<IncidentAccount> = {}): IncidentAccount => ({
  accountId: "a1",
  number: 1042,
  tradingAccountId: "demo-firm-1042-1",
  challengeName: "Two-step 100K",
  stageName: "Phase 1",
  traderEmail: "anna@test.com",
  currency: "USD",
  status: "Active",
  openPositions: 1,
  balanceAtStart: 100_000,
  equityAtStart: 98_212.5,
  ordersRefused: 0,
  closesRefused: 0,
  changesRefused: 0,
  breach: null,
  balance: 100_000,
  equity: 100_000,
  tradingDays: 2,
  canReinstate: false,
  canCredit: true,
  ...overrides,
});

const decision = (overrides: Partial<IncidentDecision> = {}): IncidentDecision => ({
  id: "d1",
  accountId: "a1",
  number: 1042,
  kind: "Reinstated",
  amount: 98_212.5,
  reason: "The outage",
  decidedBy: "maria@aurora.test",
  decidedAt: "2026-10-06T14:52:00Z",
  ...overrides,
});

const breach = { at: "2026-10-06T14:25:01Z", floorId: "daily", level: 95_000, equity: 94_982.5 };

describe("when an incident was", () => {
  it("says how long it lasted", () => {
    expect(durationText("2026-10-06T13:12:04Z", "2026-10-06T14:25:01Z")).toBe("72 minutes");
    expect(durationText("2026-10-06T13:12:04Z", "2026-10-06T13:13:04Z")).toBe("1 minute");
    expect(durationText("2026-10-06T13:12:04Z", "2026-10-06T13:12:40Z")).toBe("under a minute");
    expect(durationText("2026-10-06T13:00:00Z", "2026-10-06T15:05:00Z")).toBe("2 hours 5 minutes");
    expect(durationText("2026-10-06T13:00:00Z", "2026-10-06T16:00:00Z")).toBe("3 hours");
  });

  it("names the day once when it began and ended on the same day, and says since when while it goes on", () => {
    expect(periodText("2026-10-06T13:12:04Z", "2026-10-06T14:25:01Z", "Europe/Stockholm")).toBe("Tue 6 Oct, 15:12 to 16:25");
    expect(periodText("2026-10-06T21:40:00Z", "2026-10-06T22:10:00Z", "Europe/Stockholm")).toBe("Tue 6 Oct, 23:40 to Wed 7 Oct, 00:10");
    expect(periodText("2026-10-06T13:12:04Z", null, "UTC", true)).toBe("Since Tue 6 Oct, 13:12:04");
  });
});

describe("the status page's days", () => {
  const incident = (startedAt: string, endedAt: string | null, parts: StatusIncident["parts"] = ["Trading", "Prices"]): StatusIncident => ({
    id: startedAt,
    title: "Price feed outage",
    startedAt,
    endedAt,
    status: endedAt ? "Resolved" : "Open",
    parts,
    updates: [],
    firmNote: null,
  });
  const now = new Date("2026-10-07T10:00:00Z");

  it("marks each day an incident reached the part, oldest first up to today", () => {
    const days = statusDays([incident("2026-10-05T23:30:00Z", "2026-10-06T00:30:00Z")], "Prices", now, "UTC", 5);

    expect(days.map((d) => d.date)).toEqual(["2026-10-03", "2026-10-04", "2026-10-05", "2026-10-06", "2026-10-07"]);
    expect(days.map((d) => d.incidents.length)).toEqual([0, 0, 1, 1, 0]);
  });

  it("leaves out the parts the incident did not reach, and runs an ongoing one up to today", () => {
    expect(statusDays([incident("2026-10-06T12:00:00Z", null, ["Prices"])], "Portal", now, "UTC", 3).every((d) => d.incidents.length === 0)).toBe(true);
    expect(statusDays([incident("2026-10-06T12:00:00Z", null, ["Prices"])], "Prices", now, "UTC", 3).map((d) => d.incidents.length)).toEqual([0, 1, 1]);
  });
});

describe("what an incident did to an account", () => {
  it("puts a broken limit first, then what was refused", () => {
    expect(whatHappened(account({ breach, closesRefused: 2 }))).toEqual({
      text: "Broke the daily loss limit at equity 94,982.50. 2 closes refused while prices were missing.",
      tone: "loss",
    });
    expect(whatHappened(account({ ordersRefused: 4, closesRefused: 1, changesRefused: 1 }))).toEqual({
      text: "4 orders, 1 close and 1 stop change refused while prices were missing.",
      tone: "warning",
    });
    expect(whatHappened(account())).toEqual({ text: "Had positions open. Nothing was refused.", tone: "muted" });
  });

  it("needs a decision while a stage can be reinstated or refused requests can be credited, until the firm decided", () => {
    expect(needsDecision(account({ canReinstate: true, canCredit: false }), [])).toBe(true);
    expect(needsDecision(account({ ordersRefused: 1 }), [])).toBe(true);
    expect(needsDecision(account(), [])).toBe(false);
    expect(needsDecision(account({ canReinstate: true }), [decision()])).toBe(false);
  });

  it("offers the equity and the balance when the incident began to reinstate with", () => {
    expect(balanceChoices(account()).map((c) => [c.key, c.amount])).toEqual([
      ["equity", 98_212.5],
      ["balance", 100_000],
    ]);
    expect(balanceChoices(account({ equityAtStart: null })).map((c) => c.key)).toEqual(["balance"]);
  });

  it("says what the firm did", () => {
    expect(decisionText(decision())).toBe("Reinstated #1042 with 98,212.50");
    expect(decisionText(decision({ kind: "Credited", amount: 120 }))).toBe("Credited #1042 with 120.00");
  });
});

describe("an incident's accounts", () => {
  const incident: FirmIncident = {
    id: "i1",
    kind: "PriceFeedOutage",
    title: "Price feed outage",
    publicText: "No prices.",
    startedAt: "2026-10-06T13:12:04Z",
    endedAt: "2026-10-06T14:25:01Z",
    status: "Resolved",
    updates: [],
    note: null,
    impactKnown: true,
    accounts: [
      account({ accountId: "a1", breach, canReinstate: true, canCredit: false, status: "Failed" }),
      account({ accountId: "a2", number: 1088, ordersRefused: 4 }),
      account({ accountId: "a3", number: 1101 }),
    ],
    decisions: [decision({ accountId: "a1" })],
  };

  it("counts the accounts with positions, broken limits, refusals and those reinstated", () => {
    expect(incidentSummary(incident)).toEqual({ withPositions: 3, breached: 1, withRefusals: 1, reinstated: 1 });
  });

  it("shows those that need a decision, those with refusals, or all", () => {
    expect(filterAccounts(incident, "needs").map((a) => a.accountId)).toEqual(["a2"]);
    expect(filterAccounts(incident, "refused").map((a) => a.accountId)).toEqual(["a2"]);
    expect(filterAccounts(incident, "all")).toHaveLength(3);
  });
});

describe("the incident form's times", () => {
  it("shows a moment in the browser's time zone and reads it back", () => {
    const iso = "2026-10-06T13:12:04.000Z";
    expect(toLocalInput(iso)).toMatch(/^2026-10-0[67]T\d{2}:12:04$/);
    expect(fromLocalInput(toLocalInput(iso))).toBe(iso);
    expect([fromLocalInput(""), fromLocalInput("not a time")]).toEqual([null, null]);
  });

  it("says how long ago the last price came", () => {
    const now = Date.parse("2026-10-06T13:20:00Z");
    expect([agoText("2026-10-06T13:19:55Z", now), agoText("2026-10-06T13:19:30Z", now), agoText("2026-10-06T13:16:00Z", now), agoText("2026-10-06T10:00:00Z", now)]).toEqual([
      "just now",
      "30 s ago",
      "4 min ago",
      "3 h ago",
    ]);
  });
});
