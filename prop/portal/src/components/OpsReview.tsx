"use client";

import { useState } from "react";

import type { OpsFirm } from "@/lib/api/types";
import { countryName } from "@/lib/countries";
import { formatDate, formatDateTime, formatMoney } from "@/lib/format";
import { checkLabel, earlierRounds, reviewChecks } from "@/lib/ops";
import { useOpsAction, useOpsCheck, type OpsAction } from "@/lib/opsQueries";
import { fileSize, reviewStatusLabels } from "@/lib/verification";

import { Modal } from "./Dialog";
import { AlertIcon, ExternalIcon, FileIcon } from "./icons";
import { Badge, buttonClass, dangerButtonClass, ErrorText, fieldClass, Panel, secondaryButtonClass } from "./ui";

/**
 * Our review of a firm (ADRs 0021 and 0024): its application and documents and what it tried in the sandbox, beside our
 * checks and the decision.
 */
export function OpsReview({ firm }: { firm: OpsFirm }) {
  return (
    <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-[minmax(0,2fr)_minmax(19rem,1fr)]">
      <div className="flex min-w-0 flex-col gap-5">
        <Application firm={firm} />
        <Documents firm={firm} />
        <SandboxUse firm={firm} />
        <IdentityChecks firm={firm} />
      </div>
      <div className="flex min-w-0 flex-col gap-5">
        <Checks firm={firm} />
        <Decision firm={firm} />
      </div>
    </div>
  );
}

function Application({ firm }: { firm: OpsFirm }) {
  const application = firm.application;
  const owners = application.owners ?? [];
  const links = application.links ?? [];
  return (
    <>
      <Panel title="Company">
        <dl className="grid grid-cols-1 gap-x-6 gap-y-4 text-sm sm:grid-cols-2">
          <Item label="Legal name" value={application.companyName} />
          <Item label="Registration number" value={application.registrationNumber} mono />
          <Item label="Country" value={application.country ? `${countryName(application.country)} (${application.country})` : null} />
          <Item label="VAT number">
            {application.noVatNumber ? (
              "None, the company says it has no VAT number"
            ) : application.vatNumber ? (
              <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
                <span className="font-mono">{application.vatNumber}</span>
                <ExternalLink href="https://ec.europa.eu/taxation_customs/vies/" label="Check in VIES" />
              </span>
            ) : (
              "-"
            )}
          </Item>
          <Item label="Registered address" value={application.address} />
          <Item label="Contact" value={[application.contactName, application.contactPhone].filter(Boolean).join(", ") || null} />
        </dl>
      </Panel>

      <Panel title="Owners">
        {owners.length === 0 ? (
          <p className="text-sm text-muted">None given.</p>
        ) : (
          <table className="w-full text-sm">
            <thead className="text-left text-muted">
              <tr>
                <th scope="col" className="py-1.5 font-normal">
                  Name
                </th>
                <th scope="col" className="py-1.5 text-right font-normal">
                  Share
                </th>
              </tr>
            </thead>
            <tbody>
              {owners.map((owner, i) => (
                <tr key={i} className="border-t border-border">
                  <td className="py-2.5">{owner.name ?? "-"}</td>
                  <td className="py-2.5 text-right tabular-nums">{owner.sharePercent === null ? "-" : `${formatMoney(owner.sharePercent)} %`}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Panel>

      <Panel title="Terms, links and what they say about themselves">
        <dl className="grid grid-cols-1 gap-x-6 gap-y-4 text-sm sm:grid-cols-2">
          <Item label="Website">{application.website ? <ExternalLink href={application.website} /> : "-"}</Item>
          <Item label="Terms for traders">{application.termsUrl ? <ExternalLink href={application.termsUrl} /> : "-"}</Item>
          <Item label="Other links">
            {links.length === 0 ? (
              "-"
            ) : (
              <ul className="flex flex-col">
                {links.map((link) => (
                  <li key={link}>
                    <ExternalLink href={link} />
                  </li>
                ))}
              </ul>
            )}
          </Item>
        </dl>
        <div className="flex flex-col gap-1 text-sm">
          <span className="text-xs text-muted">About the firm</span>
          <p className="max-w-prose whitespace-pre-line">{application.description || "-"}</p>
        </div>
      </Panel>
    </>
  );
}

function Documents({ firm }: { firm: OpsFirm }) {
  return (
    <Panel title="Documents">
      {firm.documents.length === 0 ? (
        <p className="text-sm text-muted">None. Documents are optional.</p>
      ) : (
        <ul className="flex flex-col gap-2 text-sm">
          {firm.documents.map((document) => (
            <li key={document.id}>
              <a
                href={`/api/portal/ops/firms/${encodeURIComponent(firm.id)}/documents/${document.id}`}
                className="flex items-center gap-3 rounded-md border border-border px-3 py-2.5 hover:border-muted"
              >
                <FileIcon className="size-5 shrink-0 text-muted" />
                <span className="flex min-w-0 flex-1 flex-col">
                  <span className="truncate font-medium">{document.fileName}</span>
                  <span className="text-xs text-muted">
                    {fileSize(document.size)} · added {formatDate(document.uploadedAt)}
                  </span>
                </span>
                <span className="text-accent">Open</span>
              </a>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}

/** What the firm has tried in the sandbox. A firm that tried the whole chain is more likely to be ready. */
function SandboxUse({ firm }: { firm: OpsFirm }) {
  const use = firm.sandboxUse;
  const tiles = [
    { label: "Test challenges", value: use.challenges },
    { label: "Test purchases", value: use.purchases },
    { label: "Payouts tried", value: use.payouts },
    { label: "Challenges of its own", value: use.ownChallenges },
  ];
  return (
    <Panel title="What they have tried in the sandbox">
      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {tiles.map((tile) => (
          <div key={tile.label} className="flex flex-col gap-0.5 rounded-md bg-background px-3.5 py-3">
            <dt className="text-xs text-muted">{tile.label}</dt>
            <dd className="text-lg tabular-nums">{tile.value}</dd>
          </div>
        ))}
      </dl>
    </Panel>
  );
}

/** What the firm's KYC still needs before the firm can go live. Null when it is ready. Our approval does not wait for it. */
function identityProblem(firm: OpsFirm): string | null {
  switch (firm.identity.readiness) {
    case "NotChosen":
      return "The firm has not chosen its KYC.";
    case "NotTested":
      return "The firm's own KYC service has not worked through the whole flow yet.";
    default:
      return null;
  }
}

/**
 * How the firm checks its traders' IDs (ADR 0042). We approve it once they are ready: our built-in check, or its own
 * service once a trader was sent to its page and the service reported the outcome.
 */
function IdentityChecks({ firm }: { firm: OpsFirm }) {
  const { mode, externalUrl, externalTestedAt } = firm.identity;
  const problem = identityProblem(firm);
  return (
    <Panel title="KYC of traders">
      <dl className="grid grid-cols-1 gap-x-6 gap-y-4 text-sm sm:grid-cols-2">
        <Item label="How" value={mode === "BuiltIn" ? "Our built-in KYC" : mode === "External" ? "Their own KYC service" : "Not chosen yet"} />
        {mode === "External" && (
          <>
            <Item label="Their page">{externalUrl ? <span className="font-mono text-xs break-all">{externalUrl}</span> : "-"}</Item>
            <Item label="The whole flow" value={externalTestedAt ? `Worked on ${formatDateTime(externalTestedAt)}` : "Has not worked yet"} />
          </>
        )}
      </dl>
      {problem && (
        <p role="note" className="flex gap-2.5 rounded-md border border-warning/40 bg-warning/10 px-3.5 py-3 text-sm">
          <AlertIcon className="mt-0.5 size-4 shrink-0 text-warning" />
          {problem} It can be approved without it, but it cannot go live until it has.
        </p>
      )}
    </Panel>
  );
}

/**
 * Our checks, ticked while the application waits for us or for its changes. Each tick is saved with who made it. A tick
 * shows at once while it is saved, and goes back if it cannot be.
 */
function Checks({ firm }: { firm: OpsFirm }) {
  const check = useOpsCheck(firm.id);
  const [saving, setSaving] = useState<Record<string, boolean>>({});
  const open = firm.review === "Submitted" || firm.review === "ChangesRequested";
  const done = firm.checks.filter((c) => saving[c.item] ?? c.done).length;
  const toggle = (item: string, ticked: boolean) => {
    setSaving((current) => ({ ...current, [item]: ticked }));
    // Each tick settles on its own, also when several are saved at once. An error is shown below the checks.
    check
      .mutateAsync({ item, done: ticked })
      .catch(() => undefined)
      .finally(() =>
        setSaving((current) => {
          const rest = { ...current };
          delete rest[item];
          return rest;
        }),
      );
  };
  return (
    <Panel title="Our checks" actions={<span className="text-xs text-muted">{`${done} of ${firm.checks.length} done`}</span>}>
      <ul className="flex flex-col">
        {firm.checks.map((item) => {
          const text = reviewChecks[item.item];
          return (
            <li key={item.item} className="border-t border-border first:border-t-0">
              <label className={`flex items-start gap-3 py-3 ${open ? "cursor-pointer" : ""}`}>
                <input
                  type="checkbox"
                  checked={saving[item.item] ?? item.done}
                  disabled={!open || item.item in saving}
                  onChange={(e) => toggle(item.item, e.target.checked)}
                  className="mt-0.5 size-4 shrink-0 accent-accent"
                />
                <span className="flex min-w-0 flex-col gap-0.5 text-sm">
                  <span>{checkLabel(item.item)}</span>
                  <span className="text-xs text-muted">
                    {item.done && item.doneBy && item.doneAt ? `Ticked by ${item.doneBy}, ${formatDateTime(item.doneAt)}` : (text?.hint ?? "")}
                  </span>
                </span>
              </label>
            </li>
          );
        })}
      </ul>
      <ErrorText error={check.error} />
      <p className="text-xs text-muted">{open ? "The ticks are saved with the review, with who ticked them, and kept with our decision." : "The checks are made while an application waits for review."}</p>
    </Panel>
  );
}

type DialogKind = "approve" | "changes" | "reject" | null;

/** Where the review is, our latest message, and the decisions that can be made now. */
function Decision({ firm }: { firm: OpsFirm }) {
  const [dialog, setDialog] = useState<DialogKind>(null);
  const status = firm.review ?? "Draft";
  const rounds = earlierRounds(firm.events);
  const waiting = status === "Submitted";
  return (
    <Panel title="Decision" actions={<Badge tone={status === "Approved" ? "profit" : status === "Rejected" ? "loss" : status === "ChangesRequested" ? "warning" : "accent"}>{reviewStatusLabels[status]}</Badge>}>
      <dl className="flex flex-col gap-2 text-sm">
        <Row label="Sent">{firm.submittedAt ? formatDateTime(firm.submittedAt) : "Not yet"}</Row>
        <Row label="Deposit">{firm.billing.depositPaid > 0 ? `${formatMoney(firm.billing.depositPaid)} ${firm.billing.currency} paid` : "Not paid"}</Row>
        <Row label="Earlier rounds">{rounds === 0 ? "None, first application" : `We asked for changes ${rounds === 1 ? "once" : `${rounds} times`}`}</Row>
        {firm.decidedAt && <Row label="Decided">{`${formatDateTime(firm.decidedAt)} by ${firm.decidedBy}`}</Row>}
      </dl>
      {firm.message && <blockquote className="whitespace-pre-line rounded-md bg-background px-4 py-3 text-sm">{firm.message}</blockquote>}

      {waiting && (
        <div className="flex flex-col gap-2.5">
          <button type="button" onClick={() => setDialog("approve")} className={buttonClass}>
            Approve
          </button>
          <button type="button" onClick={() => setDialog("changes")} className={secondaryButtonClass}>
            Ask for changes
          </button>
          <button type="button" onClick={() => setDialog("reject")} className={`${secondaryButtonClass} border-loss/40 text-loss`}>
            Do not approve
          </button>
        </div>
      )}
      {status === "ChangesRequested" && (
        <>
          <p className="text-sm text-muted">The firm changes its application and sends it again, without a new deposit.</p>
          <button type="button" onClick={() => setDialog("reject")} className={`${secondaryButtonClass} border-loss/40 text-loss`}>
            Do not approve
          </button>
        </>
      )}
      {status === "Draft" && <p className="text-sm text-muted">The firm has not sent its application yet.</p>}
      {status === "Approved" && firm.status !== "Live" && <p className="text-sm text-muted">The firm goes live when it pays the startup fee and its first month.</p>}
      <p className="text-xs text-muted">The firm gets every decision by email and sees it in its admin panel.</p>

      <ApproveDialog firm={firm} open={dialog === "approve"} onClose={() => setDialog(null)} />
      <ChangesDialog firm={firm} open={dialog === "changes"} onClose={() => setDialog(null)} />
      <RejectDialog firm={firm} open={dialog === "reject"} onClose={() => setDialog(null)} />
    </Panel>
  );
}

/** Approves the application, with an optional word to the firm. Checks that are not ticked are named first. */
function ApproveDialog({ firm, open, onClose }: { firm: OpsFirm; open: boolean; onClose: () => void }) {
  const missing = firm.checks.filter((c) => !c.done);
  return (
    <DecisionDialog
      firm={firm}
      action="approve"
      open={open}
      onClose={onClose}
      title={`Approve ${firm.name}?`}
      description="The firm can then go live by paying. It gets an email, with your message if you write one."
      label="Message to the firm, optional"
      required={false}
      confirm={`Approve ${firm.name}`}
      confirmClass={buttonClass}
    >
      {identityProblem(firm) && (
        <p role="note" className="flex gap-2.5 rounded-md border border-warning/40 bg-warning/10 px-3.5 py-3 text-sm">
          <AlertIcon className="mt-0.5 size-4 shrink-0 text-warning" />
          {identityProblem(firm)} The firm cannot go live until it has.
        </p>
      )}
      {missing.length > 0 && (
        <div role="note" className="flex gap-2.5 rounded-md border border-warning/40 bg-warning/10 px-3.5 py-3 text-sm">
          <AlertIcon className="mt-0.5 size-4 shrink-0 text-warning" />
          <span className="flex flex-col gap-1">
            <span>{missing.length === 1 ? "One of our checks is not ticked:" : `${missing.length} of our checks are not ticked:`}</span>
            <ul className="list-disc pl-4 text-muted">
              {missing.map((c) => (
                <li key={c.item}>{checkLabel(c.item)}</li>
              ))}
            </ul>
          </span>
        </div>
      )}
    </DecisionDialog>
  );
}

/** Asks the firm for changes. Sentences for the checks that are not ticked can be added with one click. */
function ChangesDialog({ firm, open, onClose }: { firm: OpsFirm; open: boolean; onClose: () => void }) {
  const missing = firm.checks.filter((c) => !c.done && reviewChecks[c.item]);
  return (
    <DecisionDialog
      firm={firm}
      action="request-changes"
      open={open}
      onClose={onClose}
      title={`Ask ${firm.name} for changes`}
      description="They get your message by email and in their admin panel. They change the application and send it again, without paying a new deposit."
      label="What should they change?"
      required
      confirm="Send to the firm"
      confirmClass={buttonClass}
      suggestions={missing.map((c) => ({ label: reviewChecks[c.item].short, text: reviewChecks[c.item].request }))}
    />
  );
}

/** Turns the firm down for good. */
function RejectDialog({ firm, open, onClose }: { firm: OpsFirm; open: boolean; onClose: () => void }) {
  return (
    <DecisionDialog
      firm={firm}
      action="reject"
      open={open}
      onClose={onClose}
      title={`Do not approve ${firm.name}?`}
      description="It gets your message by email."
      label="Why we do not approve the firm"
      required
      confirm="Do not approve"
      confirmClass={dangerButtonClass}
    >
      <p role="note" className="rounded-md border border-loss/40 bg-loss/10 px-3.5 py-3 text-sm">
        This is final. The firm cannot go live, and its deposit is not paid back.
      </p>
    </DecisionDialog>
  );
}

/** A decision with a message to the firm, and sentences to add to the message when there are any. */
function DecisionDialog({
  firm,
  action,
  open,
  onClose,
  title,
  description,
  label,
  required,
  confirm,
  confirmClass,
  suggestions = [],
  children,
}: {
  firm: OpsFirm;
  action: OpsAction;
  open: boolean;
  onClose: () => void;
  title: string;
  description: string;
  label: string;
  required: boolean;
  confirm: string;
  confirmClass: string;
  suggestions?: { label: string; text: string }[];
  children?: React.ReactNode;
}) {
  const decide = useOpsAction(firm.id);
  const [message, setMessage] = useState("");
  const close = () => {
    decide.reset();
    onClose();
  };

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    decide.mutate(
      { action, text: message },
      {
        onSuccess: () => {
          setMessage("");
          onClose();
        },
      },
    );
  };

  const formId = `decision-${action}`;
  return (
    <Modal
      open={open}
      onClose={close}
      title={title}
      description={description}
      footer={
        <>
          <button type="button" onClick={close} className={secondaryButtonClass}>
            Cancel
          </button>
          <button type="submit" form={formId} disabled={decide.isPending || (required && message.trim() === "")} className={confirmClass}>
            {decide.isPending ? "Saving..." : confirm}
          </button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} className="flex flex-col gap-4">
        {children}
        {suggestions.length > 0 && (
          <div className="flex flex-col gap-2">
            <span className="text-sm text-muted">Add a sentence for a check that is not ticked</span>
            <div className="flex flex-wrap gap-2">
              {suggestions.map((s) => {
                const added = message.includes(s.text);
                return (
                  <button
                    key={s.label}
                    type="button"
                    aria-pressed={added}
                    disabled={added}
                    onClick={() => setMessage(message.trim() ? `${message.trim()}\n\n${s.text}` : s.text)}
                    className={`rounded-full border px-3 py-1.5 text-sm ${added ? "border-accent bg-accent/15 text-foreground" : "border-border hover:border-muted"}`}
                  >
                    {s.label}
                  </button>
                );
              })}
            </div>
          </div>
        )}
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">{label}</span>
          <textarea rows={5} required={required} value={message} onChange={(e) => setMessage(e.target.value)} className={fieldClass} />
        </label>
        <ErrorText error={decide.error} />
      </form>
    </Modal>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-muted">{label}</dt>
      <dd className="text-right">{children}</dd>
    </div>
  );
}

function Item({ label, value, mono = false, children }: { label: string; value?: string | null; mono?: boolean; children?: React.ReactNode }) {
  return (
    <div className="flex min-w-0 flex-col gap-0.5">
      <dt className="text-xs text-muted">{label}</dt>
      <dd className={`break-words whitespace-pre-line ${mono ? "font-mono" : ""}`}>{children ?? (value || "-")}</dd>
    </div>
  );
}

/** A link the firm gave us, opened apart from our admin view. */
function ExternalLink({ href, label }: { href: string; label?: string }) {
  return (
    <a href={href} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 break-all text-accent hover:underline">
      {label ?? href}
      <ExternalIcon className="size-3.5 shrink-0" />
    </a>
  );
}
