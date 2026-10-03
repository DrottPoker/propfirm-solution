import { describe, expect, it } from "vitest";

import type { Billing, Charge, Slots } from "./api/types";
import { billingNotice, cardLabel, chargeLabel, priceTiers, slotsSummary, slotsTaken, unpaidCharges } from "./billing";

const slots: Slots = { limit: "Paid", slots: 50, used: 38, reserved: 2, free: 10, paid: true, warning: true };

const renewal: Charge = {
  id: "0199a000-0000-7000-8000-000000000002",
  number: 1002,
  kind: "Renewal",
  status: "Pending",
  month: "2026-11-01",
  slots: 50,
  lines: [{ description: "50 slots, November 2026", quantity: 50, amount: 250 }],
  amount: 250,
  currency: "USD",
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
    startupFee: 500,
    slotPrices: [
      { from: 1, price: 5 },
      { from: 101, price: 4.5 },
    ],
    minSlots: 10,
    maxSlots: 10_000,
    chargeDaysBeforeMonth: 5,
    warningPercent: 80,
  },
  goLiveProblem: null,
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
  it("name each tier's slots", () => {
    expect(priceTiers(billing.prices)).toEqual(["Slots 1 to 100: 5.00 USD each per month", "Slot 101 and up: 4.50 USD each per month"]);
  });

  it("show the card's brand, last digits and expiry", () => {
    expect(cardLabel(billing.card!)).toBe("visa ending 4242, expires 03/2030");
  });

  it("say what a charge was for", () => {
    expect(chargeLabel(renewal)).toBe("Slots for November 2026");
    expect(chargeLabel({ ...renewal, kind: "Activation" })).toBe("Startup fee and the first month");
  });
});

describe("billingNotice", () => {
  it("is quiet when all is well", () => {
    expect(billingNotice(billing)).toBeNull();
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
