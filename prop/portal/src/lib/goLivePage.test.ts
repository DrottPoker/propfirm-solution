import { describe, expect, it } from "vitest";

import type { Billing, Verification } from "./api/types";
import { goLiveStates, goLiveStepOf } from "./goLivePage";

const verification = {
  status: "Draft",
  deposit: { amount: 200, currency: "USD", paid: false },
  problems: [{ field: "companyName", problem: "Fill in the company's legal name." }],
} as unknown as Verification;

const billing = { status: "Sandbox", review: null } as unknown as Billing;

function states(changes: Partial<Verification> = {}, billingChanges: Partial<Billing> = {}) {
  return goLiveStates({ ...verification, ...changes }, { ...billing, ...billingChanges });
}

describe("the way to live", () => {
  it("starts with the company's details, and the deposit once nothing is missing", () => {
    expect(states()).toEqual({ details: "current", deposit: "todo", answer: "todo", payment: "locked" });
    expect(states({ problems: [] })).toEqual({ details: "done", deposit: "current", answer: "todo", payment: "locked" });
  });

  it("waits for us, asks for changes, and opens the payment once we approve", () => {
    expect(states({ status: "Submitted", problems: [] })).toEqual({ details: "done", deposit: "done", answer: "waiting", payment: "locked" });
    expect(states({ status: "ChangesRequested", problems: [] })).toEqual({ details: "current", deposit: "todo", answer: "attention", payment: "locked" });
    expect(states({ status: "Approved", problems: [] }, { review: "Approved" })).toEqual({ details: "done", deposit: "done", answer: "done", payment: "current" });
    expect(states({ status: "Rejected", problems: [] }).answer).toBe("locked");
  });

  it("shows the step in the address, or the first that needs the firm or us", () => {
    expect(goLiveStepOf("payment", states())).toBe("payment");
    expect(goLiveStepOf("nonsense", states())).toBe("details");
    expect(goLiveStepOf(undefined, states({ problems: [] }))).toBe("deposit");
    expect(goLiveStepOf(undefined, states({ status: "Submitted", problems: [] }))).toBe("answer");
    expect(goLiveStepOf(undefined, states({ status: "Approved", problems: [] }, { review: "Approved" }))).toBe("payment");
    expect(goLiveStepOf(undefined, states({ status: "Rejected", problems: [] }))).toBe("answer");
  });
});
