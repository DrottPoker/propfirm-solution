import type { StatusTone } from "./admin";
import type { AccountDetails, IdentityMode, IdentityReadiness, IdentityRequirement, IdentityStatus, MyIdentity } from "./api/types";

// KYC, the ID checks of traders (ADR 0042). Prop.Api keeps the outcome; this words it. The firm reads KYC, the trader its identity.

/** The firm's ways to check its traders, in the order the admin panel lists them. Neither is chosen for the firm. */
export const identityModes: { mode: IdentityMode; label: string; description: string }[] = [
  {
    mode: "BuiltIn",
    label: "Our built-in KYC",
    description: "The trader checks their ID document and face on their phone or computer, in a few minutes. Approval ticks ID checked for you.",
  },
  {
    mode: "External",
    label: "Your own KYC service",
    description: "The trader is sent to your own page, and your systems tell us the outcome through the firm API.",
  },
];

/** What the firm's KYC needs while it is not ready, in the sandbox before going live or once live. Null when ready. */
export function readinessText(readiness: IdentityReadiness, live: boolean): string | null {
  switch (readiness) {
    case "NotChosen":
      return live
        ? "Choose how your traders are checked. Until you do, nothing waits for a check."
        : "Choose how your traders are checked. You need it before you go live.";
    case "NotTested":
      return live
        ? "Your own KYC service has not worked through the whole flow since its address was saved. Try it once, as below."
        : "Your own KYC service must work through the whole flow once before you go live.";
    default:
      return null;
  }
}

export const requirementLabels: Record<IdentityRequirement, string> = {
  FirstPayout: "Before the first payout",
  Funding: "Before the funded account",
};

/** Where a trader's check is, for the trader card and the trader, with its tone. */
export function identityStatus(status: IdentityStatus): { label: string; tone: StatusTone } {
  switch (status) {
    case "Approved":
      return { label: "Verified", tone: "profit" };
    case "Declined":
      return { label: "Not approved", tone: "loss" };
    case "InReview":
      return { label: "Being reviewed", tone: "warning" };
    case "Pending":
      return { label: "Started", tone: "accent" };
    case "Expired":
      return { label: "Not finished", tone: "muted" };
    default:
      return { label: "Not started", tone: "muted" };
  }
}

/** What the trader is told about the check, and what the button does, or null for no button. */
export function myIdentityText(identity: MyIdentity, firmName: string): { title: string; detail: string; action: string | null } {
  const waits = identity.requiredBefore === "Funding" ? "your funded account" : "your first payout";
  if (identity.verified) {
    return { title: "Your identity is verified", detail: `Nothing about it stands in the way of ${waits}.`, action: null };
  }

  if (identity.mode === null) {
    return { title: "No ID check yet", detail: `${firmName} has not set up ID checks yet, so nothing waits for one.`, action: null };
  }

  const start = identity.mode === "External" ? `Verify with ${firmName}` : "Verify your identity";
  switch (identity.status) {
    case "InReview":
      return { title: "Your check is being reviewed", detail: "This usually takes a few hours. You get an email when it is done.", action: null };
    case "Pending":
      return { title: "Finish your ID check", detail: "You started the check but have not sent it in yet.", action: "Continue the check" };
    case "Declined":
      return { title: "Your ID check did not pass", detail: identity.reason ?? "Try again with a valid ID document.", action: "Try again" };
    case "Expired":
      return { title: "Your ID check was not finished", detail: `Start it again. ${firmName} needs it before ${waits}.`, action: start };
    default:
      return {
        title: "Verify your identity",
        detail: `${firmName} needs to know who you are before ${waits}. Have your passport, ID card or driving licence ready; it takes a few minutes.`,
        action: start,
      };
  }
}

/**
 * Whether the dashboard asks the trader to verify now: the firm wants the check, it is not done, and the trader has
 * reached what waits for it, a funded account or one waiting to be funded.
 */
export function asksForIdentity(identity: MyIdentity | undefined, accounts: AccountDetails[]): boolean {
  if (!identity || identity.mode === null || identity.verified) {
    return false;
  }

  return accounts.some(
    (a) => a.account.status === "AwaitingFunding" || (a.account.funded && (a.account.status === "Active" || a.account.status === "OpeningAccount")),
  );
}

/** Whether the trader's next payout waits for the ID check, which the firm wants before the first payout. */
export function payoutWaitsForIdentity(identity: MyIdentity | undefined): boolean {
  return identity !== undefined && identity.mode !== null && identity.requiredBefore === "FirstPayout" && !identity.verified;
}

/**
 * Whether the name on the trader's ID differs from the account holder the payout goes to. Case, accents and the order
 * of the names do not matter, and a middle name on one side only does not count. Unknown names never differ.
 */
export function namesDiffer(idName: string | null | undefined, accountHolder: string | null | undefined): boolean {
  const words = (name: string) =>
    name
      .normalize("NFD")
      .replace(/\p{Diacritic}/gu, "")
      .toLowerCase()
      .split(/[^\p{L}\p{N}]+/u)
      .filter((w) => w.length > 0);
  if (!idName || !accountHolder) {
    return false;
  }

  const id = words(idName);
  const holder = words(accountHolder);
  if (id.length === 0 || holder.length === 0) {
    return false;
  }

  const [shorter, longer] = id.length <= holder.length ? [id, holder] : [holder, id];
  return !shorter.every((w) => longer.includes(w));
}

/** A country from an ID, as the provider gave it: a code of two letters is shown by its name. */
export function countryText(country: string | null | undefined, nameOf: (code: string) => string): string | null {
  if (!country) {
    return null;
  }

  return /^[A-Z]{2}$/.test(country) ? nameOf(country) : country;
}

/** A date of birth from an ID, for example "1 Apr 1990". */
export function birthDate(isoDate: string): string {
  return new Date(`${isoDate}T00:00:00Z`).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" });
}
