"use client";

import { useRef, useState } from "react";

import type { Verification } from "@/lib/api/types";
import { countries } from "@/lib/countries";
import { formatDateTime, formatMoney } from "@/lib/format";
import { useRemoveDocument, useSaveApplication, useSubmitApplication, useUploadDocument, useVerification } from "@/lib/queries";
import {
  applicationOf,
  depositDue,
  fileSize,
  formOf,
  maxOwners,
  reviewStatusLabels,
  reviewText,
  sharesTotal,
  type ApplicationForm,
} from "@/lib/verification";

import { buttonClass, ErrorText, fieldClass, Message, Panel, secondaryButtonClass } from "./ui";

/**
 * Our review of the firm before it goes live (ADR 0021). The firm fills in its company's details, adds documents if
 * it wants to, and sends them, paying the deposit the first time. It then waits for our decision.
 */
export function AdminVerification({ returnedFromCheckout }: { returnedFromCheckout: boolean }) {
  const [waiting, setWaiting] = useState(returnedFromCheckout);
  const verification = useVerification(waiting);

  if (verification.isError) {
    return <Message text="The review cannot be loaded right now. Try again shortly." />;
  }

  if (!verification.data) {
    return <Message text="Loading..." />;
  }

  const data = verification.data;
  return (
    <main className="mx-auto flex w-full max-w-4xl flex-col gap-6 p-6">
      {waiting && (data.status === "Draft" || data.status === "Submitted") && <DepositConfirmation verification={data} onDone={() => setWaiting(false)} />}
      <Panel title="Verification" actions={<span className="rounded bg-accent/20 px-2 py-0.5 text-sm text-accent">{reviewStatusLabels[data.status]}</span>}>
        <p className="text-sm text-muted">{reviewText(data)}</p>
        {data.message && (data.status === "ChangesRequested" || data.status === "Rejected" || data.status === "Approved") && (
          <blockquote role="status" className="rounded border-l-4 border-accent bg-background px-4 py-2 text-sm whitespace-pre-line">
            {data.message}
          </blockquote>
        )}
        {data.submittedAt && <p className="text-xs text-muted">Sent {formatDateTime(data.submittedAt)}</p>}
      </Panel>
      <ApplicationEditor key={`${data.status}-${data.canEdit}`} verification={data} />
    </main>
  );
}

/** After the payment page, the provider tells the platform the deposit is paid. Until then, the page waits. */
function DepositConfirmation({ verification, onDone }: { verification: Verification; onDone: () => void }) {
  const confirmed = verification.deposit.paid;
  return (
    <p role="status" className={`rounded border px-4 py-2 text-sm ${confirmed ? "border-profit/40 text-profit" : "border-warning/40 text-warning"}`}>
      {!confirmed
        ? "Waiting for the deposit to be confirmed..."
        : verification.status === "Draft"
          ? "Thank you. The deposit is paid. Fill in what is missing and send your application."
          : "Thank you. The deposit is paid and your application is sent."}{" "}
      {confirmed && (
        <button type="button" onClick={onDone} className="underline">
          Close
        </button>
      )}
    </p>
  );
}

/** The application's fields, its documents and the button that sends it. Read only once it cannot be changed. */
function ApplicationEditor({ verification }: { verification: Verification }) {
  const [form, setForm] = useState<ApplicationForm>(() => formOf(verification.application));
  const [problem, setProblem] = useState<string | null>(null);
  const save = useSaveApplication();
  const submit = useSubmitApplication();
  const editable = verification.canEdit;

  // What the service says is missing is about the saved application, so it is shown only while nothing is changed.
  const changed = JSON.stringify(form) !== JSON.stringify(formOf(verification.application));
  const set = (changes: Partial<ApplicationForm>) => setForm((current) => ({ ...current, ...changes }));

  const send = (action: "save" | "submit") => {
    const parsed = applicationOf(form);
    if ("problem" in parsed) {
      setProblem(parsed.problem);
      return;
    }

    setProblem(null);
    if (action === "save") {
      save.mutate(parsed.application);
    } else {
      submit.mutate(parsed.application, {
        onSuccess: (submitted) => {
          if (submitted.checkoutUrl) {
            window.location.assign(submitted.checkoutUrl);
          }
        },
      });
    }
  };

  const busy = save.isPending || submit.isPending || (submit.isSuccess && submit.data.checkoutUrl !== null);
  const deposit = `${formatMoney(verification.deposit.amount)} ${verification.deposit.currency}`;
  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        send("save");
      }}
      className="flex flex-col gap-6"
    >
      <Panel title="Company">
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField label="Legal name" value={form.companyName} onChange={(companyName) => set({ companyName })} disabled={!editable} />
          <TextField label="Registration number" value={form.registrationNumber} onChange={(registrationNumber) => set({ registrationNumber })} disabled={!editable} />
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-muted">Country of registration</span>
            <select aria-label="Country of registration" value={form.country} onChange={(e) => set({ country: e.target.value })} disabled={!editable} className={fieldClass}>
              <option value="">Choose a country</option>
              {countries.map((country) => (
                <option key={country.code} value={country.code}>
                  {country.name}
                </option>
              ))}
            </select>
          </label>
          <TextField label="Website" value={form.website} onChange={(website) => set({ website })} disabled={!editable} placeholder="https://" optional />
          <label className="flex flex-col gap-1 text-sm sm:col-span-2">
            <span className="text-muted">Registered address</span>
            <textarea aria-label="Registered address" rows={3} value={form.address} onChange={(e) => set({ address: e.target.value })} disabled={!editable} className={fieldClass} />
          </label>
        </div>
      </Panel>

      <Panel title="Contact">
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField label="Contact person" value={form.contactName} onChange={(contactName) => set({ contactName })} disabled={!editable} />
          <TextField label="Phone" value={form.contactPhone} onChange={(contactPhone) => set({ contactPhone })} disabled={!editable} optional />
        </div>
      </Panel>

      <Owners form={form} onChange={(owners) => set({ owners })} disabled={!editable} />

      <Panel title="Your terms and links">
        <TextField
          label="Your terms for traders"
          value={form.termsUrl}
          onChange={(termsUrl) => set({ termsUrl })}
          disabled={!editable}
          placeholder="https://"
          hint="Where your rules and payouts are described for your traders."
        />
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Other links (optional)</span>
          <textarea
            aria-label="Other links"
            rows={3}
            value={form.links}
            onChange={(e) => set({ links: e.target.value })}
            disabled={!editable}
            placeholder="One https address per line, for example social media or reviews"
            className={fieldClass}
          />
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">About your firm (optional)</span>
          <textarea
            aria-label="About your firm"
            rows={4}
            value={form.description}
            onChange={(e) => set({ description: e.target.value })}
            disabled={!editable}
            placeholder="Who you are, how you sell challenges and how you pay out"
            className={fieldClass}
          />
        </label>
      </Panel>

      <Documents verification={verification} />

      {editable && (
        <Panel title="Send for review">
          {depositDue(verification) && (
            <p className="text-sm text-muted">
              You pay a deposit of {deposit} when you send. It is taken off the startup fee when you go live, and it is not paid back, also if your
              firm is not approved.
            </p>
          )}
          {verification.submitProblem && !changed && <p className="text-sm text-muted">Before you send: {verification.submitProblem}</p>}
          <ErrorText error={problem ? new Error(problem) : (save.error ?? submit.error)} />
          {save.isSuccess && !save.isPending && <p className="text-sm text-profit">Saved.</p>}
          <div className="flex flex-wrap gap-3">
            <button type="submit" disabled={busy} className={secondaryButtonClass}>
              {save.isPending ? "Saving..." : "Save draft"}
            </button>
            <button type="button" disabled={busy} onClick={() => send("submit")} className={buttonClass}>
              {submit.isPending || busy ? "Sending..." : depositDue(verification) ? `Pay ${deposit} and send for review` : "Send for review"}
            </button>
          </div>
        </Panel>
      )}
    </form>
  );
}

function Owners({ form, onChange, disabled }: { form: ApplicationForm; onChange: (owners: ApplicationForm["owners"]) => void; disabled: boolean }) {
  const owners = form.owners;
  const update = (index: number, changes: Partial<ApplicationForm["owners"][number]>) =>
    onChange(owners.map((owner, i) => (i === index ? { ...owner, ...changes } : owner)));

  return (
    <Panel title="Owners">
      <p className="text-sm text-muted">Everyone who owns 25 percent or more of the company, or the largest owners if nobody does.</p>
      {owners.length > 0 && (
        <ul className="flex flex-col gap-3">
          {owners.map((owner, index) => (
            <li key={index} className="flex flex-wrap items-end gap-3">
              <label className="flex flex-1 flex-col gap-1 text-sm">
                <span className="text-muted">Name of owner {index + 1}</span>
                <input aria-label={`Name of owner ${index + 1}`} value={owner.name} onChange={(e) => update(index, { name: e.target.value })} disabled={disabled} className={fieldClass} />
              </label>
              <label className="flex flex-col gap-1 text-sm">
                <span className="text-muted">Share (%)</span>
                <input
                  aria-label={`Share of owner ${index + 1}`}
                  inputMode="decimal"
                  value={owner.share}
                  onChange={(e) => update(index, { share: e.target.value })}
                  disabled={disabled}
                  className={`${fieldClass} w-24 text-right`}
                />
              </label>
              {!disabled && (
                <button type="button" onClick={() => onChange(owners.filter((_, i) => i !== index))} className={secondaryButtonClass}>
                  Remove
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {owners.length > 0 && <p className="text-xs text-muted">Total: {formatMoney(sharesTotal(form))} %</p>}
      {!disabled && owners.length < maxOwners && (
        <button type="button" onClick={() => onChange([...owners, { name: "", share: "" }])} className={`${secondaryButtonClass} self-start`}>
          Add owner
        </button>
      )}
    </Panel>
  );
}

/** Documents the firm adds if it wants to, such as its certificate of registration. */
function Documents({ verification }: { verification: Verification }) {
  const upload = useUploadDocument();
  const remove = useRemoveDocument();
  const input = useRef<HTMLInputElement>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const editable = verification.canEdit;
  const room = verification.documents.length < verification.maxDocuments;

  const choose = (file: File | undefined) => {
    if (!file) {
      return;
    }

    if (file.size > verification.maxDocumentBytes) {
      setProblem(`${file.name} is too large. A document can be at most ${fileSize(verification.maxDocumentBytes)}.`);
    } else {
      setProblem(null);
      upload.mutate(file);
    }

    if (input.current) {
      input.current.value = "";
    }
  };

  return (
    <Panel title="Documents (optional)">
      <p className="text-sm text-muted">
        For example your certificate of registration. PDF, PNG or JPEG, at most {fileSize(verification.maxDocumentBytes)} each. They are stored
        encrypted, and only you and our staff can open them.
      </p>
      {verification.documents.length > 0 && (
        <ul className="flex flex-col gap-2 text-sm">
          {verification.documents.map((document) => (
            <li key={document.id} className="flex flex-wrap items-center gap-3">
              <a href={`/api/portal/admin/verification/documents/${document.id}`} className="text-accent hover:underline">
                {document.fileName}
              </a>
              <span className="text-muted">{fileSize(document.size)}</span>
              {editable && (
                <button type="button" disabled={remove.isPending} onClick={() => remove.mutate(document.id)} className="text-muted hover:text-loss">
                  Remove
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {editable && room && (
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">{upload.isPending ? "Uploading..." : "Add a document"}</span>
          <input
            ref={input}
            aria-label="Add a document"
            type="file"
            accept=".pdf,.png,.jpg,.jpeg,application/pdf,image/png,image/jpeg"
            disabled={upload.isPending}
            onChange={(e) => choose(e.target.files?.[0])}
            className="text-sm"
          />
        </label>
      )}
      <ErrorText error={problem ? new Error(problem) : (upload.error ?? remove.error)} />
    </Panel>
  );
}

function TextField({
  label,
  value,
  onChange,
  disabled,
  placeholder,
  hint,
  optional = false,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  disabled: boolean;
  placeholder?: string;
  hint?: string;
  optional?: boolean;
}) {
  return (
    <label className="flex flex-col gap-1 text-sm">
      <span className="text-muted">
        {label}
        {optional && " (optional)"}
      </span>
      <input aria-label={label} value={value} onChange={(e) => onChange(e.target.value)} disabled={disabled} placeholder={placeholder} className={fieldClass} />
      {hint && <span className="text-xs text-muted">{hint}</span>}
    </label>
  );
}
