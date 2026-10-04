"use client";

import { useState } from "react";

import type { OpsFirm as Firm } from "@/lib/api/types";
import { countryName } from "@/lib/countries";
import { formatDateTime, formatMoney } from "@/lib/format";
import { useOpsAction, useOpsFirm, type OpsAction } from "@/lib/opsQueries";
import { eventText, fileSize, reviewStatusLabels } from "@/lib/verification";

import { buttonClass, ErrorText, fieldClass, Message, Panel, secondaryButtonClass } from "./ui";

/** One firm for our staff: its application and documents, our decision, its billing, its suspension and its history. */
export function OpsFirm({ firmId }: { firmId: string }) {
  const firm = useOpsFirm(firmId);

  if (firm.isError) {
    return <Message text="This firm cannot be found." />;
  }

  if (!firm.data) {
    return <Message text="Loading..." />;
  }

  const data = firm.data;
  return (
    <main className="mx-auto flex w-full max-w-5xl flex-col gap-6 p-6">
      <Panel title={data.name} actions={<span className="text-sm text-muted">{data.id}</span>}>
        {data.suspension && (
          <p role="status" className="rounded border border-loss/40 bg-loss/10 px-4 py-2 text-sm text-loss">
            Suspended since {formatDateTime(data.suspension.at)}: {data.suspension.reason}
          </p>
        )}
        <dl className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-3">
          <Item label="Status" value={data.configured ? `${data.status}, configured` : data.status} />
          <Item label="Signed up" value={formatDateTime(data.createdAt)} />
          <Item label="Portal">
            <ExternalLink href={data.portalUrl} />
          </Item>
          <Item label="Administrators" value={data.admins.join(", ") || "None"} />
        </dl>
      </Panel>
      {!data.configured && <Review firm={data} />}
      {!data.configured && <Application firm={data} />}
      <Billing firm={data} />
      <Suspension firm={data} />
      <History firm={data} />
    </main>
  );
}

/** Our decision on the application, and the buttons that make it. */
function Review({ firm }: { firm: Firm }) {
  const action = useOpsAction(firm.id);
  const [message, setMessage] = useState("");
  const status = firm.review ?? "Draft";
  const waiting = status === "Submitted";
  const canReject = waiting || status === "ChangesRequested";
  const decide = (decision: OpsAction) => action.mutate({ action: decision, text: message }, { onSuccess: () => setMessage("") });

  return (
    <Panel title="Review" actions={<span className="rounded bg-accent/20 px-2 py-0.5 text-sm text-accent">{reviewStatusLabels[status]}</span>}>
      <dl className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-3">
        <Item label="Sent" value={firm.submittedAt ? formatDateTime(firm.submittedAt) : "Not yet"} />
        <Item label="Decided" value={firm.decidedAt ? `${formatDateTime(firm.decidedAt)} by ${firm.decidedBy}` : "Not yet"} />
        <Item label="Deposit" value={firm.billing.depositPaid > 0 ? `${formatMoney(firm.billing.depositPaid)} ${firm.billing.currency} paid` : "Not paid"} />
      </dl>
      {firm.message && (
        <blockquote className="rounded border-l-4 border-accent bg-background px-4 py-2 text-sm whitespace-pre-line">{firm.message}</blockquote>
      )}
      {canReject ? (
        <div className="flex flex-col gap-3 border-t border-border pt-4">
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-muted">Message to the firm</span>
            <textarea
              aria-label="Message to the firm"
              rows={3}
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              placeholder="Required when you ask for changes or reject. The firm gets it by email."
              className={fieldClass}
            />
          </label>
          <ErrorText error={action.error} />
          <div className="flex flex-wrap gap-3">
            {waiting && (
              <button type="button" disabled={action.isPending} onClick={() => decide("approve")} className={buttonClass}>
                Approve
              </button>
            )}
            {waiting && (
              <button type="button" disabled={action.isPending} onClick={() => decide("request-changes")} className={secondaryButtonClass}>
                Ask for changes
              </button>
            )}
            <button type="button" disabled={action.isPending} onClick={() => decide("reject")} className={`${secondaryButtonClass} text-loss`}>
              Reject for good
            </button>
          </div>
        </div>
      ) : (
        status === "Draft" && <p className="text-sm text-muted">The firm has not sent its application yet.</p>
      )}
    </Panel>
  );
}

function Application({ firm }: { firm: Firm }) {
  const application = firm.application;
  return (
    <Panel title="Application">
      <dl className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2">
        <Item label="Legal name" value={application.companyName} />
        <Item label="Registration number" value={application.registrationNumber} />
        <Item label="Country" value={application.country ? `${countryName(application.country)} (${application.country})` : null} />
        <Item label="VAT number" value={application.noVatNumber ? "None, the company says it has no VAT number" : application.vatNumber} />
        <Item label="Registered address" value={application.address} />
        <Item label="Website">{application.website ? <ExternalLink href={application.website} /> : "-"}</Item>
        <Item label="Contact" value={[application.contactName, application.contactPhone].filter(Boolean).join(", ") || null} />
        <Item label="Owners">
          {(application.owners ?? []).length === 0 ? (
            "-"
          ) : (
            <ul>
              {(application.owners ?? []).map((owner, i) => (
                <li key={i}>
                  {owner.name ?? "-"}, {owner.sharePercent === null ? "-" : `${formatMoney(owner.sharePercent)} %`}
                </li>
              ))}
            </ul>
          )}
        </Item>
        <Item label="Terms for traders">{application.termsUrl ? <ExternalLink href={application.termsUrl} /> : "-"}</Item>
        <Item label="Other links">
          {(application.links ?? []).length === 0 ? (
            "-"
          ) : (
            <ul>
              {(application.links ?? []).map((link) => (
                <li key={link}>
                  <ExternalLink href={link} />
                </li>
              ))}
            </ul>
          )}
        </Item>
        <Item label="About the firm" value={application.description} />
      </dl>
      <div className="flex flex-col gap-2 border-t border-border pt-4 text-sm">
        <h3 className="text-muted">Documents</h3>
        {firm.documents.length === 0 ? (
          <p className="text-muted">None.</p>
        ) : (
          <ul className="flex flex-col gap-1">
            {firm.documents.map((document) => (
              <li key={document.id} className="flex flex-wrap gap-3">
                <a href={`/api/portal/ops/firms/${firm.id}/documents/${document.id}`} className="text-accent hover:underline">
                  {document.fileName}
                </a>
                <span className="text-muted">
                  {fileSize(document.size)}, {formatDateTime(document.uploadedAt)}
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Panel>
  );
}

function Billing({ firm }: { firm: Firm }) {
  const billing = firm.billing;
  return (
    <Panel title="Billing">
      <dl className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-3">
        <Item label="Plan" value={billing.plan ?? "Not paying yet"} />
        <Item label="Slots" value={billing.slots === null ? "No limit" : String(billing.slots)} />
        <Item label="Open challenges" value={String(billing.openChallenges)} />
        <Item label="Live since" value={billing.activatedAt ? formatDateTime(billing.activatedAt) : "-"} />
        <Item label="Unpaid since" value={billing.unpaidSince ? formatDateTime(billing.unpaidSince) : "-"} />
      </dl>
    </Panel>
  );
}

/** Suspends the firm with a reason its administrators see, or lifts the suspension. */
function Suspension({ firm }: { firm: Firm }) {
  const action = useOpsAction(firm.id);
  const [reason, setReason] = useState("");
  return (
    <Panel title="Suspension">
      {firm.suspension ? (
        <>
          <p className="text-sm">
            Suspended since {formatDateTime(firm.suspension.at)}. No challenges can start, the shop is closed and the traders&apos; accounts are paused.
          </p>
          <button type="button" disabled={action.isPending} onClick={() => action.mutate({ action: "unsuspend", text: "" })} className={`${buttonClass} self-start`}>
            Lift the suspension
          </button>
        </>
      ) : (
        <>
          <p className="text-sm text-muted">
            For example when the firm does not pay its traders. No challenges can start, the shop closes and the traders&apos; accounts are paused until
            you lift it. The firm&apos;s administrators get the reason by email.
          </p>
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-muted">Reason</span>
            <textarea aria-label="Reason" rows={2} value={reason} onChange={(e) => setReason(e.target.value)} className={fieldClass} />
          </label>
          <button
            type="button"
            disabled={action.isPending}
            onClick={() => action.mutate({ action: "suspend", text: reason }, { onSuccess: () => setReason("") })}
            className={`${secondaryButtonClass} self-start text-loss`}
          >
            Suspend the firm
          </button>
        </>
      )}
      <ErrorText error={action.error} />
    </Panel>
  );
}

function History({ firm }: { firm: Firm }) {
  return (
    <Panel title="History">
      {firm.events.length === 0 ? (
        <p className="text-sm text-muted">Nothing yet.</p>
      ) : (
        <ul className="flex flex-col gap-2 text-sm">
          {firm.events.map((event) => (
            <li key={event.id} className="flex flex-wrap gap-x-3">
              <span className="text-muted">{formatDateTime(event.recordedAt)}</span>
              <span className="whitespace-pre-line">{eventText(event)}</span>
              <span className="text-muted">{event.actor}</span>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}

function Item({ label, value, children }: { label: string; value?: string | null; children?: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-muted">{label}</dt>
      <dd className="break-words whitespace-pre-line">{children ?? (value || "-")}</dd>
    </div>
  );
}

/** A link the firm gave us, opened apart from our admin view. */
function ExternalLink({ href }: { href: string }) {
  return (
    <a href={href} target="_blank" rel="noopener noreferrer" className="text-accent hover:underline">
      {href}
    </a>
  );
}
