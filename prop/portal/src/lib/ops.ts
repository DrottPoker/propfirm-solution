import { formatTotals, type StatusTone } from "./admin";
import type { Charge, OpsActivity, OpsCharge, OpsEvent, OpsFirm, OpsFirmGroup, OpsFirmListItem, OpsFirmStage, OpsNeedsUs, OpsWaitingPayout } from "./api/types";
import { monthName } from "./billing";
import { formatDate, formatMoney } from "./format";

// What our own admin view shows across the firms (ADR 0024). Prop.Api counts and sums; this only words and arranges it.

/** The groups of the list of firms, in the order of its tabs. */
export const opsGroups: OpsFirmGroup[] = ["ToReview", "Sandbox", "Live", "Unpaid", "Suspended", "Rejected", "All"];

export const opsGroupLabels: Record<OpsFirmGroup, string> = {
  ToReview: "To review",
  Sandbox: "Sandbox",
  Live: "Live",
  Unpaid: "Unpaid",
  Suspended: "Suspended",
  Rejected: "Not approved",
  All: "All",
};

/** The parts of a firm's page. A firm we set up ourselves has no review. */
export type OpsFirmTab = "review" | "overview" | "billing" | "team" | "history";

export const opsFirmTabs: OpsFirmTab[] = ["review", "overview", "billing", "team", "history"];

/** Where a firm is with us, in a few words for a badge, with its tone. */
export const stageViews: Record<OpsFirmStage, { label: string; tone: StatusTone }> = {
  SettingUp: { label: "Setting up", tone: "muted" },
  Sandbox: { label: "Sandbox", tone: "muted" },
  ToReview: { label: "To review", tone: "accent" },
  ChangesRequested: { label: "Changes asked for", tone: "warning" },
  Approved: { label: "Approved, not live", tone: "accent" },
  Rejected: { label: "Not approved", tone: "muted" },
  Live: { label: "Live", tone: "profit" },
  Unpaid: { label: "Live, unpaid", tone: "loss" },
  Suspended: { label: "Suspended", tone: "loss" },
};

/** Where the firm on its own page is with us, worked out as Prop.Api does for the list. */
export function stageOf(firm: OpsFirm): OpsFirmStage {
  if (firm.suspension) {
    return "Suspended";
  }

  if (firm.status === "Live") {
    return firm.billing.unpaidSince || firm.billing.charges.some((c) => (c.status === "Pending" || c.status === "Failed") && c.failure !== null) ? "Unpaid" : "Live";
  }

  if (firm.review === "Rejected") {
    return "Rejected";
  }

  if (firm.status === "Provisioning") {
    return "SettingUp";
  }

  return firm.review === "Submitted" ? "ToReview" : firm.review === "ChangesRequested" ? "ChangesRequested" : firm.review === "Approved" ? "Approved" : "Sandbox";
}

/** How long something has waited, for example "12 minutes", "26 hours" or "3 days". Hours up to two days. */
export function waitedText(iso: string, now: number): string {
  const minutes = Math.max(0, Math.floor((now - Date.parse(iso)) / 60_000));
  if (minutes < 1) {
    return "less than a minute";
  }

  if (minutes < 60) {
    return minutes === 1 ? "1 minute" : `${minutes} minutes`;
  }

  const hours = Math.floor(minutes / 60);
  if (hours < 48) {
    return hours === 1 ? "1 hour" : `${hours} hours`;
  }

  return `${Math.floor(hours / 24)} days`;
}

/** Whole days since a time. */
export function daysSince(iso: string, now: number): number {
  return Math.max(0, Math.floor((now - Date.parse(iso)) / 86_400_000));
}

/** The firm's latest step in the list, for example "Sent 26 hours ago" or "Live since 3 Mar 2026". */
export function latestText(firm: OpsFirmListItem, now: number): string {
  switch (firm.stage) {
    case "ToReview":
      return firm.submittedAt ? `Sent ${waitedText(firm.submittedAt, now)} ago` : "Sent";
    case "ChangesRequested":
      return firm.decidedAt ? `Changes asked for on ${formatDate(firm.decidedAt)}` : "Changes asked for";
    case "Approved":
      return firm.decidedAt ? `Approved on ${formatDate(firm.decidedAt)}` : "Approved";
    case "Rejected":
      return firm.decidedAt ? `Not approved on ${formatDate(firm.decidedAt)}` : "Not approved";
    case "Suspended":
      return firm.suspendedAt ? `Suspended on ${formatDate(firm.suspendedAt)}` : "Suspended";
    case "Unpaid":
      return firm.unpaidSince ? `Unpaid since ${formatDate(firm.unpaidSince)}` : "A charge was declined";
    case "Live":
      return firm.activatedAt ? `Live since ${formatDate(firm.activatedAt)}` : firm.configured ? "Set up by us" : "Live";
    default:
      return `Signed up on ${formatDate(firm.createdAt)}`;
  }
}

/** The firm's open challenges against what it may have, for example "88 of 100", "3 test challenges" or "21 of 25, all paused". */
export function challengesText(firm: Pick<OpsFirmListItem, "status" | "openChallenges" | "pausedChallenges" | "slots">): string {
  const { openChallenges: open, pausedChallenges: paused, slots } = firm;
  if (firm.status !== "Live") {
    return open === 0 ? "No test challenges" : `${open} test ${open === 1 ? "challenge" : "challenges"}`;
  }

  const count = slots === null ? `${open}, no limit` : `${open} of ${slots}`;
  return paused === 0 ? count : paused === open ? `${count}, all paused` : `${count}, ${paused} paused`;
}

/** How full the firm's slots are, from 0 to 100, or null when there is no limit or it is not live. */
export function slotsUsed(firm: Pick<OpsFirmListItem, "status" | "openChallenges" | "slots">): number | null {
  if (firm.status !== "Live" || firm.slots === null) {
    return null;
  }

  return firm.slots === 0 ? 100 : Math.min(100, (firm.openChallenges / firm.slots) * 100);
}

export type NeedsUsIcon = "review" | "card" | "clock" | "server";

/** Something we have to do, with where to do it. */
export type NeedsUsItem = { key: string; icon: NeedsUsIcon; tone: "accent" | "loss" | "warning"; title: string; detail: string; href: string; action: string };

/** How many applications waiting for review are named one by one before the rest are counted. */
const namedApplications = 3;

/**
 * What we have to do, the most urgent first: applications that wait for review, firms whose card was declined, firms
 * whose traders wait too long for payouts, and trading servers that take too long to create.
 */
export function needsUsItems(needsUs: OpsNeedsUs, now: number): NeedsUsItem[] {
  const items: NeedsUsItem[] = [];
  const waiting = needsUs.toReview;
  if (waiting.length > 0) {
    const oldest = waiting[0];
    const named = waiting
      .slice(0, namedApplications)
      .map((firm) => `${firm.name} has waited ${waitedText(firm.since, now)}`)
      .join(", ");
    const more = waiting.length > namedApplications ? `, and ${waiting.length - namedApplications} more` : "";
    items.push({
      key: "review",
      icon: "review",
      tone: "accent",
      title: waiting.length === 1 ? `${oldest.name} waits for review` : `${waiting.length} applications wait for review`,
      detail: `${waiting.length === 1 ? `Sent ${waitedText(oldest.since, now)} ago` : `${named}${more}`}. Firms are told we answer within a day.`,
      href: `/ops/firms/${encodeURIComponent(oldest.id)}`,
      action: `Review ${oldest.name}`,
    });
  }

  for (const [firmId, charges] of byFirm(needsUs.unpaid)) {
    const first = charges[0];
    const amounts = new Map<string, number>();
    for (const c of charges) {
      amounts.set(c.charge.currency, (amounts.get(c.charge.currency) ?? 0) + c.charge.amount);
    }

    const total = [...amounts].map(([currency, amount]) => `${formatMoney(amount)} ${currency}`).join(" + ");
    const tried = `Tried ${first.charge.attempts} ${first.charge.attempts === 1 ? "time" : "times"}.`;
    const consequence = first.unpaidSince
      ? ` Its ${first.pausedChallenges === 1 ? "open challenge has" : `${first.pausedChallenges} open challenges have`} been paused since ${formatDate(first.unpaidSince)}.`
      : first.charge.nextAttemptAt
        ? ` The card is tried again on ${formatDate(first.charge.nextAttemptAt)}.`
        : "";
    items.push({
      key: `unpaid-${firmId}`,
      icon: "card",
      tone: "loss",
      title: charges.length === 1 ? `${first.firmName} has not paid ${unpaidWhat(first.charge)}` : `${first.firmName} has ${charges.length} unpaid charges`,
      detail: `${total}. ${first.charge.failure ?? "The card was declined."} ${tried}${consequence}`,
      href: `/ops/firms/${encodeURIComponent(firmId)}?tab=billing`,
      action: "Open billing",
    });
  }

  for (const late of needsUs.latePayouts) {
    const some = late.count === 1 ? "A payout" : `${late.count} payouts`;
    const approved = late.approved === 0 ? "none approved yet" : late.approved === late.count ? (late.count === 1 ? "approved by the firm" : "all approved by the firm") : `${late.approved} of them approved by the firm`;
    items.push({
      key: `payouts-${late.firmId}`,
      icon: "clock",
      tone: "warning",
      title: `${late.firmName} has not paid its traders for ${daysSince(late.oldestRequestedAt, now)} days`,
      detail: `${some} of ${formatTotals(late.totals, "USD")} ${late.count === 1 ? "has" : "have"} waited more than ${needsUs.lateAfterDays} days, ${approved}. The oldest was asked for on ${formatDate(late.oldestRequestedAt)}.`,
      href: `/ops/firms/${encodeURIComponent(late.firmId)}`,
      action: "Open the firm",
    });
  }

  for (const firm of needsUs.settingUp) {
    items.push({
      key: `server-${firm.id}`,
      icon: "server",
      tone: "warning",
      title: `${firm.name} has no trading server yet`,
      detail: `It signed up ${waitedText(firm.since, now)} ago. Creating the server is tried again by itself; if it stays like this, look at the trading platform.`,
      href: `/ops/firms/${encodeURIComponent(firm.id)}`,
      action: "Open the firm",
    });
  }

  return items;
}

/** What an unpaid charge was for, after "has not paid", for example "for October 2026" or "to go live". */
function unpaidWhat(charge: Charge): string {
  switch (charge.kind) {
    case "Renewal":
      return `for ${monthName(charge.month)}`;
    case "Activation":
      return "to go live";
    case "Deposit":
      return "the deposit";
    case "IdentityChecks":
      return `for KYC checks in ${monthName(charge.month)}`;
    default:
      return "for more slots";
  }
}

// The charges grouped by firm, in the order of each firm's first charge.
function byFirm(charges: OpsCharge[]): [string, OpsCharge[]][] {
  const groups = new Map<string, OpsCharge[]>();
  for (const charge of charges) {
    groups.set(charge.firmId, [...(groups.get(charge.firmId) ?? []), charge]);
  }

  return [...groups];
}

export type OpsActivityIcon = "plus" | "file" | "check" | "pencil" | "cross" | "pause" | "play" | "rocket" | "card" | "declined";

/** One thing that happened to a firm, worded for the overview. The firm is shown beside it. */
export type OpsActivityView = { title: string; note: string | null; tone: StatusTone; icon: OpsActivityIcon };

export function opsActivityView(activity: OpsActivity): OpsActivityView {
  const amount = activity.amount != null && activity.currency ? `${formatMoney(activity.amount)} ${activity.currency}` : null;
  const by = activity.actor ? `by ${activity.actor}` : null;
  switch (activity.kind) {
    case "SignedUp":
      return { title: "Signed up", note: null, tone: "muted", icon: "plus" };
    case "ApplicationSent":
      // The platform sends an application when its deposit is paid.
      return { title: "Application sent", note: activity.actor === "platform" ? "with the deposit paid" : by, tone: "accent", icon: "file" };
    case "Approved":
      return { title: "Approved", note: by, tone: "profit", icon: "check" };
    case "ChangesRequested":
      return { title: "Changes asked for", note: activity.text ? `"${activity.text}"` : by, tone: "warning", icon: "pencil" };
    case "Rejected":
      return { title: "Not approved", note: activity.text ? `"${activity.text}"` : by, tone: "loss", icon: "cross" };
    case "Suspended":
      return { title: "Suspended", note: activity.text, tone: "loss", icon: "pause" };
    case "SuspensionLifted":
      return { title: "Suspension lifted", note: by, tone: "profit", icon: "play" };
    case "WentLive":
      return { title: "Went live", note: [activity.slots != null ? `${activity.slots} slots` : null, amount && `${amount} paid`].filter(Boolean).join(" · ") || null, tone: "profit", icon: "rocket" };
    case "ChargePaid":
      return {
        title: activity.chargeKind === "Renewal" && activity.month ? `Paid for ${monthName(activity.month)}` : "Paid for more slots",
        note: amount,
        tone: "muted",
        icon: "card",
      };
    case "ChargeDeclined":
      return {
        title: "Card declined",
        note: [activity.chargeKind === "Renewal" && activity.month ? monthName(activity.month) : null, amount, activity.text].filter(Boolean).join(" · ") || null,
        tone: "loss",
        icon: "declined",
      };
  }
}

/** Our checks during a review, in words and in short, with a sentence for the firm when a check is not met. */
export const reviewChecks: Record<string, { label: string; short: string; hint: string; request: string }> = {
  vat: {
    short: "VAT number",
    label: "The VAT number is valid in VIES",
    hint: "Or the company has none, which only a company outside the EU or below the threshold may say.",
    request: "We could not find your VAT number in the EU's register (VIES). Check the number, or tick that the company has none.",
  },
  register: {
    short: "Business register",
    label: "The company is in its business register, at this address",
    hint: "Compare with the register extract if they added one.",
    request: "We could not find the company in its business register as you gave it. Give its name, number and address as they are registered.",
  },
  owners: {
    short: "Owners",
    label: "The owners match the register",
    hint: "Everyone with 25 % or more.",
    request: "The owners in your application do not match the business register. Give the owners as they are registered.",
  },
  terms: {
    short: "Terms on payouts",
    label: "The terms say how and when traders are paid",
    hint: "Open the terms from the application.",
    request: "Your terms for traders do not say how and when payouts are paid. Add it to the terms, then send the application again.",
  },
  website: {
    short: "Website and links",
    label: "The website and links work and look real",
    hint: "Including the other links they gave.",
    request: "A link in your application does not open. Check the website and the other links.",
  },
};

/** The label of one of our checks, or its key when the portal does not know it. */
export function checkLabel(item: string): string {
  return reviewChecks[item]?.label ?? item;
}

/** How many times we have asked the firm for changes before. */
export function earlierRounds(events: OpsEvent[]): number {
  return events.filter((e) => e.type === "changes_requested").length;
}

/** The payouts traders have waited too long for, the oldest first. */
export function latePayouts(waiting: OpsWaitingPayout[], lateAfterDays: number, now: number): OpsWaitingPayout[] {
  return waiting.filter((p) => daysSince(p.requestedAt, now) >= lateAfterDays);
}

/** A link that writes to every administrator of the firm. */
export function mailtoAdmins(firm: Pick<OpsFirm, "admins" | "name">): string | null {
  if (firm.admins.length === 0) {
    return null;
  }

  return `mailto:${firm.admins.map((a) => encodeURIComponent(a.email)).join(",")}?subject=${encodeURIComponent(firm.name)}`;
}
