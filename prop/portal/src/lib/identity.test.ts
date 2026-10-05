import { describe, expect, it } from "vitest";

import type { MyIdentity } from "./api/types";
import { asksForIdentity, birthDate, countryText, identityStatus, myIdentityText, namesDiffer, payoutWaitsForIdentity, readinessText } from "./identity";
import { testAccount, testDetails } from "./testAccounts";

const notStarted: MyIdentity = { mode: "BuiltIn", requiredBefore: "FirstPayout", status: "NotStarted", verified: false, canStart: true, reason: null, decidedAt: null };

describe("the trader's ID check", () => {
  it("says what to do, and what waits for it", () => {
    expect(myIdentityText(notStarted, "Demo Firm")).toMatchObject({ title: "Verify your identity", action: "Verify your identity" });
    expect(myIdentityText(notStarted, "Demo Firm").detail).toContain("before your first payout");
    expect(myIdentityText({ ...notStarted, requiredBefore: "Funding" }, "Demo Firm").detail).toContain("before your funded account");
    expect(myIdentityText({ ...notStarted, mode: "External" }, "Demo Firm").action).toBe("Verify with Demo Firm");
    expect(myIdentityText({ ...notStarted, status: "Pending" }, "Demo Firm").action).toBe("Continue the check");
    expect(myIdentityText({ ...notStarted, status: "InReview", canStart: false }, "Demo Firm").action).toBeNull();
    expect(myIdentityText({ ...notStarted, status: "Declined", reason: "The document has expired." }, "Demo Firm")).toMatchObject({
      detail: "The document has expired.",
      action: "Try again",
    });
    expect(myIdentityText({ ...notStarted, status: "Approved", verified: true }, "Demo Firm")).toMatchObject({ title: "Your identity is verified", action: null });
    expect(myIdentityText({ ...notStarted, mode: null, canStart: false }, "Demo Firm")).toMatchObject({ title: "No ID check yet", action: null });
  });

  it("is asked for on the dashboard once a funded account is near", () => {
    const evaluation = testDetails();
    const funded = testDetails({ account: { ...testAccount, funded: true, status: "Active" } });
    const waiting = testDetails({ account: { ...testAccount, status: "AwaitingFunding" } });

    expect(asksForIdentity(notStarted, [evaluation])).toBe(false);
    expect(asksForIdentity(notStarted, [evaluation, funded])).toBe(true);
    expect(asksForIdentity(notStarted, [waiting])).toBe(true);
    expect(asksForIdentity({ ...notStarted, verified: true }, [funded])).toBe(false);
    expect(asksForIdentity({ ...notStarted, mode: null }, [funded])).toBe(false);
    expect(asksForIdentity(undefined, [funded])).toBe(false);
  });

  it("holds the payout back until verified, when the firm wants it before the first payout", () => {
    expect(payoutWaitsForIdentity(notStarted)).toBe(true);
    expect(payoutWaitsForIdentity({ ...notStarted, verified: true })).toBe(false);
    expect(payoutWaitsForIdentity({ ...notStarted, requiredBefore: "Funding" })).toBe(false);
    expect(payoutWaitsForIdentity({ ...notStarted, mode: null })).toBe(false);
    expect(payoutWaitsForIdentity(undefined)).toBe(false);
  });

  it("has a status for the trader card", () => {
    expect(identityStatus("Approved")).toEqual({ label: "Verified", tone: "profit" });
    expect(identityStatus("Declined").tone).toBe("loss");
    expect(identityStatus("NotStarted").label).toBe("Not started");
  });
});

describe("the firm's KYC before going live", () => {
  it("say what going live needs, in the sandbox or once live", () => {
    expect(readinessText("NotChosen", false)).toContain("You need it before you go live");
    expect(readinessText("NotChosen", true)).toContain("nothing waits for a check");
    expect(readinessText("NotTested", false)).toContain("whole flow once before you go live");
    expect(readinessText("Ready", false)).toBeNull();
  });
});

describe("the name on the ID and the account holder", () => {
  it("differ only when the names do", () => {
    expect(namesDiffer("Anna Andersson", "ANNA ANDERSSON")).toBe(false);
    expect(namesDiffer("Anna Maria Andersson", "Anna Andersson")).toBe(false);
    expect(namesDiffer("Andersson, Anna", "Anna Andersson")).toBe(false);
    expect(namesDiffer("José Müller", "Jose Muller")).toBe(false);
    expect(namesDiffer("Anna Andersson", "Bert Berg")).toBe(true);
    expect(namesDiffer("Anna Andersson", "Anna Berg")).toBe(true);
    expect(namesDiffer(null, "Bert Berg")).toBe(false);
    expect(namesDiffer("Anna Andersson", "")).toBe(false);
  });
});

describe("the country from an ID", () => {
  it("is named when it is a code", () => {
    const names = (code: string) => (code === "SE" ? "Sweden" : code);
    expect(countryText("SE", names)).toBe("Sweden");
    expect(countryText("Sweden", names)).toBe("Sweden");
    expect(countryText(null, names)).toBeNull();
  });

  it("comes with a date of birth that is the same day everywhere", () => {
    expect(birthDate("1990-04-01")).toBe("1 Apr 1990");
  });
});
