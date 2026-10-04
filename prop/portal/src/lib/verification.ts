import type { FirmApplication, OpsEvent, ReviewStatus, Verification } from "./api/types";

export const reviewStatusLabels: Record<ReviewStatus, string> = {
  Draft: "Not sent",
  Submitted: "Waiting for review",
  ChangesRequested: "Changes needed",
  Approved: "Approved",
  Rejected: "Not approved",
};

/** The application as the form holds it: text in every field, shares as typed and the links one per line. */
export type ApplicationForm = {
  companyName: string;
  registrationNumber: string;
  country: string;
  vatNumber: string;
  noVatNumber: boolean;
  address: string;
  website: string;
  contactName: string;
  contactPhone: string;
  owners: { name: string; share: string }[];
  termsUrl: string;
  links: string;
  description: string;
};

export const maxOwners = 10;

/** The application's fields as the form names them, for the list of what is missing. */
export const applicationFieldLabels: Record<string, string> = {
  companyName: "Legal name",
  registrationNumber: "Registration number",
  country: "Country of registration",
  vatNumber: "VAT number",
  address: "Registered address",
  website: "Website",
  contactName: "Contact person",
  contactPhone: "Phone",
  owners: "Owners",
  termsUrl: "Your terms for traders",
  links: "Other links",
  description: "About your firm",
};

/**
 * What the service says is wrong with each field of the saved application, by field. A field the form has changed since
 * is left out, since what was said is about the saved value.
 */
export function problemsByField(verification: Verification, form: ApplicationForm): Record<string, string> {
  const saved = formOf(verification.application);
  const changed = (field: string) =>
    field === "vatNumber"
      ? form.vatNumber !== saved.vatNumber || form.noVatNumber !== saved.noVatNumber
      : JSON.stringify(form[field as keyof ApplicationForm]) !== JSON.stringify(saved[field as keyof ApplicationForm]);
  return Object.fromEntries(verification.problems.filter((p) => !changed(p.field)).map((p) => [p.field, p.problem]));
}

export function formOf(application: FirmApplication): ApplicationForm {
  return {
    companyName: application.companyName ?? "",
    registrationNumber: application.registrationNumber ?? "",
    country: application.country ?? "",
    vatNumber: application.vatNumber ?? "",
    noVatNumber: application.noVatNumber === true,
    address: application.address ?? "",
    website: application.website ?? "",
    contactName: application.contactName ?? "",
    contactPhone: application.contactPhone ?? "",
    owners: (application.owners ?? []).map((o) => ({ name: o.name ?? "", share: o.sharePercent === null ? "" : String(o.sharePercent) })),
    termsUrl: application.termsUrl ?? "",
    links: (application.links ?? []).join("\n"),
    description: application.description ?? "",
  };
}

/** The application to save, or what is wrong with a share that is not a number. The service checks the rest. */
export function applicationOf(form: ApplicationForm): { application: FirmApplication } | { problem: string } {
  const owners = [];
  for (const [i, owner] of form.owners.entries()) {
    const share = shareOf(owner.share);
    if (share === undefined) {
      return { problem: `Write the share of owner ${i + 1} as a number, for example 25 or 33.33.` };
    }

    owners.push({ name: text(owner.name), sharePercent: share });
  }

  return {
    application: {
      companyName: text(form.companyName),
      registrationNumber: text(form.registrationNumber),
      country: text(form.country),
      vatNumber: form.noVatNumber ? null : text(form.vatNumber),
      noVatNumber: form.noVatNumber ? true : null,
      address: text(form.address),
      website: text(form.website),
      contactName: text(form.contactName),
      contactPhone: text(form.contactPhone),
      owners,
      termsUrl: text(form.termsUrl),
      links: form.links
        .split(/\r?\n/)
        .map((l) => l.trim())
        .filter((l) => l.length > 0),
      description: text(form.description),
    },
  };
}

/** The owners' shares added up, ignoring those not typed as numbers. */
export function sharesTotal(form: ApplicationForm): number {
  return form.owners.reduce((total, owner) => total + (shareOf(owner.share) ?? 0), 0);
}

/** A file size, for example "820 KB" or "2.4 MB". */
export function fileSize(bytes: number): string {
  if (bytes < 1024 * 1024) {
    return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  }

  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** Whether sending the application takes the firm to a page where it pays the deposit first. */
export function depositDue(verification: Verification): boolean {
  return verification.status === "Draft" && !verification.deposit.paid && verification.deposit.amount > 0;
}

const eventLabels: Record<string, string> = {
  submitted: "Sent for review",
  changes_requested: "Changes requested",
  approved: "Approved",
  rejected: "Not approved",
  document_added: "Document added",
  document_removed: "Document removed",
  suspended: "Suspended",
  suspension_lifted: "Suspension lifted",
};

/** What happened, for example "Changes requested: Your terms do not say how payouts work." */
export function eventText(event: OpsEvent): string {
  const label = eventLabels[event.type] ?? event.type;
  const detail = typeof event.detail === "object" && event.detail !== null ? (event.detail as Record<string, unknown>) : {};
  const note = [detail.message, detail.reason, detail.fileName].find((v): v is string => typeof v === "string" && v.length > 0);
  return note ? `${label}: ${note}` : label;
}

function text(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
}

// A share typed as a number, null when it is empty, and undefined when it is not a number.
function shareOf(typed: string): number | null | undefined {
  const value = typed.trim().replace(",", ".");
  if (value === "") {
    return null;
  }

  const share = Number(value);
  return Number.isFinite(share) ? share : undefined;
}
