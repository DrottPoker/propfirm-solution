import { describe, expect, it } from "vitest";

import type { Billing, Charge, Slots } from "./api/types";
import { billingNotice, cardLabel, chargeLabel, expansionText, invoiceUrl, monthlyPrices, slotsSummary, slotsTaken, unpaidCharges, vatOn, vatText } from "./billing";

const slots: Slots = { limit: "Paid", slots: 50, used: 38, reserved: 2, free: 10, paid: true, suspended: false, warning: true };

const renewal: Charge = {
  id: "0199a000-0000-7000-8000-000000000002",
  number: 1002,
  kind: "Renewal",
  status: "Pending",
  month: "2026-11-01",
  slots: 50,
  lines: [
    { description: "Package with 25 slots, November 2026", quantity: 25, amount: 500 },
    { description: "25 extra slots, November 2026", quantity: 25, amount: 125 },
  ],
  netAmount: 625,
  vatTreatment: "Charged",
  vatPercent: 25,
  vatAmount: 156.25,
  amount: 781.25,
  currency: "USD",
  invoice: null,
  failure: "The test card was declined.",
  attempts: 1,
  nextAttemptAt: "2026-10-28T00:00:01Z",
  createdAt: "2026-10-27T00:00:01Z",
  paidAt: null,
  canPay: true,
};

const billing: Billing = {
  status: "Live",
  plan: "Paid",
  provider: "Test",
  slots: { ...slots, used: 10, reserved: 0, free: 40, warning: false },
  nextMonthSlots: 50,
  autoExpandStep: null,
  card: { brand: "visa", last4: "4242", expMonth: 3, expYear: 2030 },
  unpaidSince: null,
  nextCharge: null,
  charges: [],
  prices: {
    currency: "USD",
    startupFee: 700,
    reviewDeposit: 200,
    packagePrice: 500,
    packageSlots: 25,
    slotPrices: [
      { from: 26, price: 5 },
      { from: 101, price: 4 },
    ],
    maxSlots: 10_000,
    chargeDaysBeforeMonth: 5,
    warningPercent: 80,
  },
  goLiveProblem: null,
  review: null,
  depositPaid: 0,
  suspension: null,
  shopProblem: null,
  vat: { treatment: "Charged", percent: 25 },
  sandboxAccounts: 0,
};

describe("slots", () => {
  it("are summed up with the orders that hold one", () => {
    expect(slotsSummary(slots)).toBe("40 of 50 slots taken, 2 of them by orders waiting for payment. 10 free.");
    expect(slotsTaken(slots)).toBe(80);
  });

  it("have no share taken without a limit", () => {
    const unlimited: Slots = { ...slots, limit: "Unlimited", slots: null, free: null, reserved: 0, used: 1 };

    expect(slotsSummary(unlimited)).toBe("1 open challenge. No limit.");
    expect(slotsTaken(unlimited)).toBeNull();
  });
});

describe("prices and cards", () => {
  it("name the package and then each tier's slots beyond it", () => {
    expect(monthlyPrices(billing.prices)).toEqual([
      "Package with 25 slots: 500.00 USD per month",
      "Slots 26 to 100: 5.00 USD each per month",
      "Slot 101 and up: 4.00 USD each per month",
    ]);
  });

  it("show the card's brand, last digits and expiry", () => {
    expect(cardLabel(billing.card!)).toBe("visa ending 4242, expires 03/2030");
  });

  it("say what a charge was for", () => {
    expect(chargeLabel(renewal)).toBe("Slots for November 2026");
    expect(chargeLabel({ ...renewal, kind: "Activation" })).toBe("Startup fee and the first month");
    expect(chargeLabel({ ...renewal, kind: "Deposit" })).toBe("Deposit for the review, taken off the startup fee");
  });
});

describe("VAT and invoices", () => {
  it("say how VAT applies, and work out the VAT in whole cents", () => {
    expect(vatText({ treatment: "Charged", percent: 25 })).toBe("Prices are without VAT. 25% VAT is added for your company.");
    expect(vatText({ treatment: "ReverseCharge", percent: 0 })).toMatch(/reverse charge/);
    expect(vatText({ treatment: "OutsideEu", percent: 0 })).toMatch(/outside the EU/);
    expect(vatOn(200, { treatment: "Charged", percent: 25 })).toBe(50);
    expect(vatOn(1157.25, { treatment: "Charged", percent: 25 })).toBe(289.31);
    expect(vatOn(1157.25, { treatment: "ReverseCharge", percent: 0 })).toBe(0);
  });

  it("find each paid charge's invoice, and say what an expansion adds", () => {
    expect(invoiceUrl(renewal)).toBe(`/api/portal/admin/billing/charges/${renewal.id}/invoice`);
    expect(expansionText({ slots: 10, monthlyPrice: 50, restOfMonth: 43.55, month: "2026-10-01" }, "USD")).toBe(
      "Adds 10 slots for 50.00 USD a month, and 43.55 USD for the rest of October if it happens today. Without VAT.",
    );
  });
});

describe("billingNotice", () => {
  it("is quiet when all is well", () => {
    expect(billingNotice(billing)).toBeNull();
  });

  it("says first of all that we suspended the firm, without sending it to billing", () => {
    const notice = billingNotice({
      ...billing,
      unpaidSince: "2026-11-01T00:00:01Z",
      suspension: { at: "2026-11-02T10:00:00Z", reason: "Traders report payouts that were never paid." },
    });

    expect(notice).toMatchObject({ tone: "loss", billingLink: false });
    expect(notice?.text).toMatch(/^Your firm is suspended: Traders report payouts that were never paid\. No new challenges can start/);
  });

  it("says first that the month is unpaid and the challenges paused", () => {
    expect(billingNotice({ ...billing, unpaidSince: "2026-11-01T00:00:01Z", charges: [renewal] })).toMatchObject({ tone: "loss" });
  });

  it("warns about a declined payment before the month starts", () => {
    const notice = billingNotice({ ...billing, charges: [renewal] });

    expect(notice?.tone).toBe("warning");
    expect(notice?.text).toMatch(/^Slots for November 2026: the payment was declined\. We try again on 28 Oct 2026\./);
  });

  it("warns when every slot is taken or most are", () => {
    expect(billingNotice({ ...billing, slots: { ...slots, free: 0 } })?.text).toMatch(/^Every slot is taken/);
    expect(billingNotice({ ...billing, slots })?.text).toBe(slotsSummary(slots));
  });

  it("lists the declined charges the firm can pay, oldest first", () => {
    const older = { ...renewal, id: "older", month: "2026-10-01" };

    const waiting = { ...renewal, id: "waiting", failure: null, attempts: 0 };

    expect(unpaidCharges({ ...billing, charges: [renewal, older, waiting, { ...renewal, id: "paid", canPay: false }] }).map((c) => c.id)).toEqual(["older", renewal.id]);
  });
});
