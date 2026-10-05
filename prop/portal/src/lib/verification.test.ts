import { describe, expect, it } from "vitest";

import type { FirmApplication, OpsEvent, Verification } from "./api/types";
import { countries, countryName } from "./countries";
import { applicationOf, depositDue, eventText, fileSize, formOf, problemsByField, sharesTotal } from "./verification";

const application: FirmApplication = {
  companyName: "Acme Trading Ltd",
  registrationNumber: "559000-1234",
  country: "SE",
  vatNumber: "SE559000123401",
  noVatNumber: null,
  address: "Storgatan 1\n111 22 Stockholm",
  website: null,
  contactName: "Anna Andersson",
  contactPhone: null,
  owners: [
    { name: "Anna Andersson", sharePercent: 60 },
    { name: "Bert Berg", sharePercent: 40 },
  ],
  termsUrl: "https://acme.test/terms",
  links: ["https://x.com/acme", "https://trustpilot.test/acme"],
  description: null,
};

const verification: Verification = {
  status: "Draft",
  application,
  documents: [],
  message: null,
  submittedAt: null,
  decidedAt: null,
  deposit: { amount: 100, currency: "USD", paid: false },
  canEdit: true,
  submitProblem: null,
  maxDocuments: 10,
  maxDocumentBytes: 10 * 1024 * 1024,
  euCountries: ["DE", "SE"],
  problems: [],
  identity: "Ready",
};

describe("the application form", () => {
  it("holds the application as text and gives it back the same", () => {
    const form = formOf(application);

    expect(form.links).toBe("https://x.com/acme\nhttps://trustpilot.test/acme");
    expect(form.owners).toEqual([
      { name: "Anna Andersson", share: "60" },
      { name: "Bert Berg", share: "40" },
    ]);
    expect(applicationOf(form)).toEqual({ application });
  });

  it("leaves out the VAT number when the company has none", () => {
    const form = { ...formOf(application), noVatNumber: true };

    expect(applicationOf(form)).toMatchObject({ application: { vatNumber: null, noVatNumber: true } });
    expect(formOf({ ...application, vatNumber: null, noVatNumber: true }).noVatNumber).toBe(true);
  });

  it("sends empty fields as none, trims text and drops empty links", () => {
    const form = { ...formOf(application), website: "   ", contactPhone: " +46 70 ", links: "\n https://x.com/acme \r\n\n" };

    expect(applicationOf(form)).toMatchObject({ application: { website: null, contactPhone: "+46 70", links: ["https://x.com/acme"] } });
  });

  it("reads shares with a comma or a point, and refuses one that is not a number", () => {
    const form = { ...formOf(application), owners: [{ name: "Anna", share: "33,5" }, { name: "Bert", share: "" }, { name: "Cleo", share: "a third" }] };

    expect(sharesTotal(form)).toBe(33.5);
    expect(applicationOf(form)).toEqual({ problem: "Write the share of owner 3 as a number, for example 25 or 33.33." });
    expect(applicationOf({ ...form, owners: form.owners.slice(0, 2) })).toMatchObject({
      application: { owners: [{ name: "Anna", sharePercent: 33.5 }, { name: "Bert", sharePercent: null }] },
    });
  });
});

describe("the review", () => {
  it("takes the deposit when a draft is sent the first time", () => {
    expect(depositDue(verification)).toBe(true);
    expect(depositDue({ ...verification, deposit: { ...verification.deposit, paid: true } })).toBe(false);
    expect(depositDue({ ...verification, status: "ChangesRequested" })).toBe(false);
    expect(depositDue({ ...verification, deposit: { ...verification.deposit, amount: 0 } })).toBe(false);
  });

  it("marks the fields the saved application is missing, but not one changed since", () => {
    const missing: Verification = {
      ...verification,
      application: { ...application, companyName: null },
      problems: [
        { field: "companyName", problem: "Fill in the company's legal name." },
        { field: "vatNumber", problem: "Fill in the company's VAT number, or tick that it has none." },
      ],
    };
    const form = formOf(missing.application);

    expect(problemsByField(missing, form)).toEqual({
      companyName: "Fill in the company's legal name.",
      vatNumber: "Fill in the company's VAT number, or tick that it has none.",
    });
    expect(problemsByField(missing, { ...form, companyName: "Acme" })).toEqual({ vatNumber: "Fill in the company's VAT number, or tick that it has none." });
    expect(problemsByField(missing, { ...form, noVatNumber: true })).toEqual({ companyName: "Fill in the company's legal name." });
  });

  it("describes what happened, with the message, reason or file", () => {
    const event: OpsEvent = { id: 1, type: "changes_requested", recordedAt: "2026-10-05T10:00:00Z", actor: "ops@test.com", detail: { message: "Add your terms." } };

    expect(eventText(event)).toBe("Changes requested: Add your terms.");
    expect(eventText({ ...event, type: "suspended", detail: { reason: "No payouts." } })).toBe("Suspended: No payouts.");
    expect(eventText({ ...event, type: "submitted", detail: { application, depositCharge: 1001 } })).toBe("Sent for review");
    expect(eventText({ ...event, type: "something_new", detail: null })).toBe("something_new");
  });
});

describe("files and countries", () => {
  it("show sizes in kilobytes and megabytes", () => {
    expect(fileSize(200)).toBe("1 KB");
    expect(fileSize(820 * 1024)).toBe("820 KB");
    expect(fileSize(2.4 * 1024 * 1024)).toBe("2.4 MB");
  });

  it("name countries from their codes, sorted by name", () => {
    expect(countryName("SE")).toBe("Sweden");
    expect(countries.find((c) => c.code === "GB")?.name).toBe("United Kingdom");
    expect(countries.map((c) => c.name)).toEqual([...countries.map((c) => c.name)].sort((a, b) => a.localeCompare(b, "en")));
  });
});
