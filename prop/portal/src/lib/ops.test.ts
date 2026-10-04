import { describe, expect, it } from "vitest";

import type { Charge, OpsActivity, OpsCharge, OpsFirm, OpsFirmListItem, OpsNeedsUs } from "./api/types";
import { challengesText, latePayouts, latestText, mailtoAdmins, needsUsItems, opsActivityView, slotsUsed, stageOf, waitedText } from "./ops";

const now = Date.parse("2026-10-04T12:00:00Z");

const noNeeds: OpsNeedsUs = { toReview: [], unpaid: [], latePayouts: [], settingUp: [], lateAfterDays: 7 };

function charge(overrides: Partial<Charge> = {}): Charge {
  return {
    id: "c1",
    number: 1093,
    kind: "Renewal",
    status: "Pending",
    month: "2026-10-01",
    slots: 60,
    lines: [],
    netAmount: 675,
    vatTreatment: "ReverseCharge",
    vatPercent: 0,
    vatAmount: 0,
    amount: 675,
    currency: "USD",
    invoice: null,
    failure: "Card declined, insufficient funds.",
    attempts: 3,
    nextAttemptAt: null,
    createdAt: "2026-09-26T00:00:00Z",
    paidAt: null,
    canPay: true,
    ...overrides,
  };
}

function unpaid(overrides: Partial<OpsCharge> = {}): OpsCharge {
  return { firmId: "granite-prop", firmName: "Granite Prop", charge: charge(), failedAt: "2026-09-30T00:00:00Z", unpaidSince: "2026-10-01T00:00:00Z", pausedChallenges: 54, ...overrides };
}

function listed(overrides: Partial<OpsFirmListItem>): OpsFirmListItem {
  return {
    id: "acme",
    name: "Acme",
    stage: "Sandbox",
    status: "Sandbox",
    configured: false,
    createdAt: "2026-10-02T08:00:00Z",
    submittedAt: null,
    decidedAt: null,
    activatedAt: null,
    suspendedAt: null,
    unpaidSince: null,
    openChallenges: 0,
    pausedChallenges: 0,
    slots: 5,
    monthlyPrice: null,
    ...overrides,
  };
}

describe("needsUsItems", () => {
  it("names the oldest application and links to it", () => {
    const [item] = needsUsItems({ ...noNeeds, toReview: [{ id: "kestrel", name: "Kestrel Funding", since: "2026-10-03T10:00:00Z" }] }, now);

    expect(item).toMatchObject({ title: "Kestrel Funding waits for review", href: "/ops/firms/kestrel", action: "Review Kestrel Funding", tone: "accent" });
    expect(item.detail).toBe("Sent 26 hours ago. Firms are told we answer within a day.");
  });

  it("names three applications and counts the rest", () => {
    const firms = ["a", "b", "c", "d"].map((id, i) => ({ id, name: id.toUpperCase(), since: new Date(now - (4 - i) * 3_600_000).toISOString() }));
    const [item] = needsUsItems({ ...noNeeds, toReview: firms }, now);

    expect(item.title).toBe("4 applications wait for review");
    expect(item.detail).toBe("A has waited 4 hours, B has waited 3 hours, C has waited 2 hours, and 1 more. Firms are told we answer within a day.");
  });

  it("says what a firm has not paid and that its challenges are paused", () => {
    const [item] = needsUsItems({ ...noNeeds, unpaid: [unpaid()] }, now);

    expect(item).toMatchObject({ title: "Granite Prop has not paid for October 2026", href: "/ops/firms/granite-prop?tab=billing", tone: "loss" });
    expect(item.detail).toBe("675.00 USD. Card declined, insufficient funds. Tried 3 times. Its 54 open challenges have been paused since 1 Oct 2026.");
  });

  it("puts a firm's unpaid charges together and says when the card is tried again", () => {
    const items = needsUsItems(
      {
        ...noNeeds,
        unpaid: [
          unpaid({ unpaidSince: null, charge: charge({ attempts: 1, nextAttemptAt: "2026-10-05T00:00:00Z" }) }),
          unpaid({ unpaidSince: null, charge: charge({ id: "c2", kind: "Slots", amount: 25 }) }),
        ],
      },
      now,
    );

    expect(items).toHaveLength(1);
    expect(items[0].title).toBe("Granite Prop has 2 unpaid charges");
    expect(items[0].detail).toBe("700.00 USD. Card declined, insufficient funds. Tried 1 time. The card is tried again on 5 Oct 2026.");
  });

  it("says how long traders have waited for a firm's payouts", () => {
    const later = Date.parse("2026-10-12T12:00:00Z");
    const [item] = needsUsItems(
      {
        ...noNeeds,
        latePayouts: [{ firmId: "harbor-desk", firmName: "Harbor Desk", count: 3, approved: 3, totals: [{ currency: "USD", amount: 11_240 }], oldestRequestedAt: "2026-10-03T09:00:00Z" }],
      },
      later,
    );

    expect(item).toMatchObject({ title: "Harbor Desk has not paid its traders for 9 days", tone: "warning", action: "Open the firm" });
    expect(item.detail).toBe("3 payouts of 11,240.00 USD have waited more than 7 days, all approved by the firm. The oldest was asked for on 3 Oct 2026.");
  });

  it("names a firm whose trading server takes too long", () => {
    const [item] = needsUsItems({ ...noNeeds, settingUp: [{ id: "lumen", name: "Lumen", since: "2026-10-04T11:30:00Z" }] }, now);

    expect(item.title).toBe("Lumen has no trading server yet");
    expect(item.detail).toMatch(/^It signed up 30 minutes ago\./);
  });

  it("has nothing when nothing waits", () => {
    expect(needsUsItems(noNeeds, now)).toEqual([]);
  });
});

describe("latestText", () => {
  it("words the firm's latest step after its stage", () => {
    expect(latestText(listed({ stage: "ToReview", submittedAt: "2026-10-04T09:00:00Z" }), now)).toBe("Sent 3 hours ago");
    expect(latestText(listed({ stage: "Live", status: "Live", activatedAt: "2026-03-03T10:00:00Z" }), now)).toBe("Live since 3 Mar 2026");
    expect(latestText(listed({ stage: "Live", status: "Live", configured: true }), now)).toBe("Set up by us");
    expect(latestText(listed({ stage: "Unpaid", status: "Live" }), now)).toBe("A charge was declined");
    expect(latestText(listed({ stage: "Sandbox" }), now)).toBe("Signed up on 2 Oct 2026");
  });
});

describe("challengesText and slotsUsed", () => {
  it("counts test challenges in the sandbox, without a bar", () => {
    const firm = listed({ openChallenges: 3 });

    expect(challengesText(firm)).toBe("3 test challenges");
    expect(slotsUsed(firm)).toBeNull();
  });

  it("counts a live firm's challenges against its slots, and those paused", () => {
    const firm = listed({ status: "Live", openChallenges: 54, pausedChallenges: 54, slots: 60 });

    expect(challengesText(firm)).toBe("54 of 60, all paused");
    expect(slotsUsed(firm)).toBe(90);
    expect(challengesText(listed({ status: "Live", openChallenges: 46, slots: null }))).toBe("46, no limit");
  });
});

describe("opsActivityView", () => {
  const activity = (overrides: Partial<OpsActivity>): OpsActivity => ({
    kind: "SignedUp",
    time: "2026-10-04T08:15:00Z",
    firmId: "baltic",
    firmName: "Baltic Funded",
    actor: null,
    amount: null,
    currency: null,
    text: null,
    chargeKind: null,
    month: null,
    slots: null,
    ...overrides,
  });

  it("words a firm going live with its slots and payment", () => {
    expect(opsActivityView(activity({ kind: "WentLive", amount: 1_075, currency: "USD", slots: 40, chargeKind: "Activation" }))).toEqual({
      title: "Went live",
      note: "40 slots · 1,075.00 USD paid",
      tone: "profit",
      icon: "rocket",
    });
  });

  it("words a declined card with its month and reason", () => {
    expect(opsActivityView(activity({ kind: "ChargeDeclined", amount: 675, currency: "USD", chargeKind: "Renewal", month: "2026-10-01", text: "Insufficient funds." })).note).toBe(
      "October 2026 · 675.00 USD · Insufficient funds.",
    );
  });

  it("says when an application was sent by paying its deposit", () => {
    expect(opsActivityView(activity({ kind: "ApplicationSent", actor: "platform" })).note).toBe("with the deposit paid");
    expect(opsActivityView(activity({ kind: "ApplicationSent", actor: "owner@acme.example" })).note).toBe("by owner@acme.example");
  });

  it("quotes our message when we asked for changes", () => {
    expect(opsActivityView(activity({ kind: "ChangesRequested", actor: "ops@test.com", text: "Add your terms." })).note).toBe('"Add your terms."');
  });
});

describe("waitedText", () => {
  it("counts minutes, then hours up to two days, then days", () => {
    expect(waitedText("2026-10-04T11:59:30Z", now)).toBe("less than a minute");
    expect(waitedText("2026-10-04T11:59:00Z", now)).toBe("1 minute");
    expect(waitedText("2026-10-03T10:00:00Z", now)).toBe("26 hours");
    expect(waitedText("2026-09-30T12:00:00Z", now)).toBe("4 days");
  });
});

describe("a firm's page", () => {
  const firm = {
    name: "Harbor Desk",
    status: "Live",
    review: "Approved",
    suspension: null,
    admins: [
      { email: "desk@harbor.example", since: "2026-02-14T00:00:00Z" },
      { email: "anna+ops@harbor.example", since: "2026-04-02T00:00:00Z" },
    ],
    billing: { unpaidSince: null, charges: [charge({ status: "Paid", failure: null })] },
  } as unknown as OpsFirm;

  it("is live until a charge is declined or a month is unpaid", () => {
    expect(stageOf(firm)).toBe("Live");
    expect(stageOf({ ...firm, billing: { ...firm.billing, charges: [charge()] } })).toBe("Unpaid");
    expect(stageOf({ ...firm, suspension: { at: "2026-10-01T00:00:00Z", reason: "Payouts." } })).toBe("Suspended");
  });

  it("finds the payouts traders have waited too long for", () => {
    const waiting = [
      { accountNumber: 2218, status: "Approved" as const, requestedAt: "2026-09-25T09:00:00Z", approvedAt: "2026-09-26T09:00:00Z", amount: 4_120, currency: "USD" },
      { accountNumber: 2301, status: "Pending" as const, requestedAt: "2026-10-02T09:00:00Z", approvedAt: null, amount: 3_240, currency: "USD" },
    ];

    expect(latePayouts(waiting, 7, now).map((p) => p.accountNumber)).toEqual([2218]);
  });

  it("writes to every administrator", () => {
    expect(mailtoAdmins(firm)).toBe("mailto:desk%40harbor.example,anna%2Bops%40harbor.example?subject=Harbor%20Desk");
    expect(mailtoAdmins({ ...firm, admins: [] })).toBeNull();
  });
});
