"use client";

import { useRef, useState } from "react";

import type { Verification } from "@/lib/api/types";
import { countries, countryName } from "@/lib/countries";
import { formatMoney } from "@/lib/format";
import { FieldError, useRemoveDocument, useSaveApplication, useUploadDocument } from "@/lib/queries";
import { applicationFieldLabels, applicationOf, fileSize, formOf, maxOwners, problemsByField, sharesTotal, type ApplicationForm } from "@/lib/verification";
import { useSavedNote } from "@/lib/useSavedNote";

import { DropZone } from "./DropZone";
import { AlertIcon, CheckIcon } from "./icons";
import { buttonClass, ErrorText, fieldClass, Panel, secondaryButtonClass } from "./ui";

/**
 * The company's details for our review (ADR 0021): every field, with each one that is missing or wrong marked once it
 * has been left or the firm has saved, and then the list of them by the buttons. Saved as a draft; the next step sends
 * it. Read only once it cannot be changed.
 */
export function ApplicationEditor({ verification, onContinue }: { verification: Verification; onContinue: () => void }) {
  const [form, setForm] = useState<ApplicationForm>(() => formOf(verification.application));
  const [problem, setProblem] = useState<string | null>(null);
  // What is missing is shown for a field once it has been left, and for all of them once the firm saves, not before.
  const [attempted, setAttempted] = useState(false);
  const [touched, setTouched] = useState<ReadonlySet<string>>(() => new Set());
  const save = useSaveApplication();
  useSavedNote(save, "Draft saved.");
  // Saves the draft quietly when a changed field is left, so nothing typed is lost. Its problems wait for a real save.
  const autosave = useSaveApplication();
  const [autosavedAt, setAutosavedAt] = useState<number | null>(null);
  // A save waits for a quiet one still on its way, so an older draft never lands after it.
  const quietSave = useRef<Promise<unknown>>(Promise.resolve());
  const editable = verification.canEdit;
  const set = (changes: Partial<ApplicationForm>) => setForm((current) => ({ ...current, ...changes }));
  const inEu = verification.euCountries.includes(form.country);

  // What the service says is about the saved application, so a field changed since is not marked.
  const problems = problemsByField(verification, form);
  if (save.error instanceof FieldError && save.error.field && !problems[save.error.field]) {
    problems[save.error.field] = save.error.message;
  }

  const shown = (field: string): string | undefined => (attempted || touched.has(field) ? problems[field] : undefined);

  const send = (andContinue: boolean) => {
    setAttempted(true);
    const parsed = applicationOf(form);
    if ("problem" in parsed) {
      setProblem(parsed.problem);
      return;
    }

    setProblem(null);

    // Awaited rather than a callback of mutate, which never runs when the saved application moves the page to the next step first.
    quietSave.current.then(() => save.mutateAsync(parsed.application)).then(
      (saved) => {
        if (andContinue && saved.problems.length === 0) {
          onContinue();
        }
      },
      () => {},
    );
  };

  const changed = JSON.stringify(form) !== JSON.stringify(formOf(verification.application));
  const missing = verification.problems;

  const saveQuietly = () => {
    if (!editable || !changed || save.isPending || autosave.isPending) {
      return;
    }

    const parsed = applicationOf(form);
    if (!("problem" in parsed)) {
      quietSave.current = autosave.mutateAsync(parsed.application).then(
        () => setAutosavedAt(Date.now()),
        () => {},
      );
    }
  };

  // A part is done once nothing in it is missing in the saved draft and nothing in it has changed since.
  const done = (fields: string[]) =>
    !changed && missing.every((p) => !fields.includes(p.field)) ? (
      <span className="flex items-center gap-1.5 text-xs font-medium text-profit">
        <CheckIcon className="size-3.5" />
        Done
      </span>
    ) : undefined;
  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        send(true);
      }}
      onBlur={(event) => {
        const field = event.target.getAttribute("name");
        if (field && !touched.has(field)) {
          setTouched(new Set(touched).add(field));
        }

        saveQuietly();
      }}
      className="flex flex-col gap-6"
    >
      <Panel title="Company" actions={done(["companyName", "registrationNumber", "country", "vatNumber", "website", "address"])}>
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField name="companyName" label="Legal name" value={form.companyName} onChange={(companyName) => set({ companyName })} disabled={!editable} problem={shown("companyName")} />
          <TextField
            name="registrationNumber"
            label="Registration number"
            value={form.registrationNumber}
            onChange={(registrationNumber) => set({ registrationNumber })}
            disabled={!editable}
            problem={shown("registrationNumber")}
          />
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-muted">Country of registration</span>
            <select
              name="country"
              aria-label="Country of registration"
              aria-invalid={shown("country") ? true : undefined}
              value={form.country}
              onChange={(e) => set({ country: e.target.value, noVatNumber: form.noVatNumber && verification.euCountries.includes(e.target.value) })}
              disabled={!editable}
              className={fieldOf(shown("country"))}
            >
              <option value="">Choose a country</option>
              {countries.map((country) => (
                <option key={country.code} value={country.code}>
                  {country.name}
                </option>
              ))}
            </select>
            <FieldProblemText problem={shown("country")} />
          </label>
          <div className="flex flex-col gap-2">
            <TextField
              name="vatNumber"
              label="VAT number"
              value={form.noVatNumber ? "" : form.vatNumber}
              onChange={(vatNumber) => set({ vatNumber })}
              disabled={!editable || form.noVatNumber}
              hint={inEu ? "With the country code first, for example SE559000123401." : "The company's VAT or tax number, if it has one."}
              optional={!inEu}
              problem={shown("vatNumber")}
            />
            {inEu && (
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" checked={form.noVatNumber} onChange={(e) => set({ noVatNumber: e.target.checked })} disabled={!editable} />
                The company has no VAT number
              </label>
            )}
          </div>
          <TextField name="website" label="Website" value={form.website} onChange={(website) => set({ website })} disabled={!editable} placeholder="https://" optional problem={shown("website")} />
          <label className="flex flex-col gap-1 text-sm sm:col-span-2">
            <span className="text-muted">Registered address</span>
            <textarea
              name="address"
              aria-label="Registered address"
              aria-invalid={shown("address") ? true : undefined}
              rows={3}
              value={form.address}
              onChange={(e) => set({ address: e.target.value })}
              disabled={!editable}
              className={fieldOf(shown("address"))}
            />
            <FieldProblemText problem={shown("address")} />
          </label>
        </div>
      </Panel>

      <Panel title="Contact" actions={done(["contactName", "contactPhone"])}>
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField name="contactName" label="Contact person" value={form.contactName} onChange={(contactName) => set({ contactName })} disabled={!editable} problem={shown("contactName")} />
          <TextField name="contactPhone" label="Phone" value={form.contactPhone} onChange={(contactPhone) => set({ contactPhone })} disabled={!editable} optional problem={shown("contactPhone")} />
        </div>
      </Panel>

      <Owners form={form} onChange={(owners) => set({ owners })} disabled={!editable} problem={shown("owners")} done={done(["owners"])} />

      <Panel title="Your terms and links" actions={done(["termsUrl", "links", "description"])}>
        <TextField
          name="termsUrl"
          label="Your terms for traders"
          value={form.termsUrl}
          onChange={(termsUrl) => set({ termsUrl })}
          disabled={!editable}
          placeholder="https://"
          hint="Where your rules and payouts are described for your traders. The same address buyers accept in your shop, under Checkout: saving it here changes it there too."
          problem={shown("termsUrl")}
        />
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">Other links (optional)</span>
          <textarea
            name="links"
            aria-label="Other links"
            aria-invalid={shown("links") ? true : undefined}
            rows={3}
            value={form.links}
            onChange={(e) => set({ links: e.target.value })}
            disabled={!editable}
            placeholder="One https address per line, for example social media or reviews"
            className={fieldOf(shown("links"))}
          />
          <FieldProblemText problem={shown("links")} />
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-muted">About your firm (optional)</span>
          <textarea
            name="description"
            aria-label="About your firm"
            rows={4}
            value={form.description}
            onChange={(e) => set({ description: e.target.value })}
            disabled={!editable}
            placeholder="Who you are, how you sell challenges and how you pay out"
            className={fieldOf(shown("description"))}
          />
          <FieldProblemText problem={shown("description")} />
        </label>
      </Panel>

      <Documents verification={verification} />

      {editable && (
        <Panel>
          {missing.length > 0 && !changed && !attempted ? (
            <p className="text-sm text-muted">
              {missing.length === 1 ? "One thing is left to fill in" : `${missing.length} things are left to fill in`} before you can send. We point them out when you save.
            </p>
          ) : missing.length > 0 && !changed ? (
            <div role="status" className="flex flex-col gap-2 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm">
              <span className="flex items-center gap-2 font-medium">
                <AlertIcon className="size-4 text-warning" />
                {missing.length === 1 ? "One thing is missing before you can send" : `${missing.length} things are missing before you can send`}
              </span>
              <ul className="flex list-disc flex-col gap-0.5 pl-6 text-muted">
                {missing.map((p) => (
                  <li key={p.field}>
                    <span className="text-foreground">{applicationFieldLabels[p.field] ?? p.field}:</span> {p.problem}
                  </li>
                ))}
              </ul>
            </div>
          ) : changed ? (
            <p className="text-sm text-muted">Save to check what is still missing.</p>
          ) : (
            <p className="text-sm text-profit">Everything is filled in. Continue to send it for review.</p>
          )}
          <ErrorText error={problem ? new Error(problem) : save.error} />
          {autosavedAt !== null && !changed && !save.isSuccess && (
            <p className="text-xs text-muted">
              Saved as a draft by itself at {new Date(autosavedAt).toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" })}.
            </p>
          )}
          <div className="flex flex-wrap gap-3">
            <button type="button" disabled={save.isPending} onClick={() => send(false)} className={secondaryButtonClass}>
              {save.isPending ? "Saving..." : "Save draft"}
            </button>
            <button type="submit" disabled={save.isPending} className={buttonClass}>
              Save and continue
            </button>
          </div>
        </Panel>
      )}
    </form>
  );
}

/** The company's details as they were approved, for a firm that is live. */
export function CompanyDetails({ verification }: { verification: Verification }) {
  const a = verification.application;
  const rows: [string, string | null][] = [
    ["Legal name", a.companyName],
    ["Registration number", a.registrationNumber],
    ["Country of registration", a.country ? countryName(a.country) : null],
    ["VAT number", a.noVatNumber ? "None" : a.vatNumber],
    ["Registered address", a.address],
    ["Website", a.website],
    ["Contact person", a.contactName],
    ["Phone", a.contactPhone],
    ["Owners", (a.owners ?? []).map((o) => `${o.name ?? ""}${o.sharePercent === null ? "" : ` (${formatMoney(o.sharePercent)} %)`}`).join(", ") || null],
    ["Your terms for traders", a.termsUrl],
    ["Other links", (a.links ?? []).join("\n") || null],
    ["About your firm", a.description],
  ];
  return (
    <div className="flex flex-col gap-6">
      <Panel title="Company details">
        <p className="text-sm text-muted">What you sent for our review. Write to us if something has changed, such as the address or the owners.</p>
        <dl className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2">
          {rows.map(([label, value]) => (
            <div key={label} className="flex flex-col gap-0.5">
              <dt className="text-muted">{label}</dt>
              <dd className="whitespace-pre-line break-words">{value ?? "-"}</dd>
            </div>
          ))}
        </dl>
      </Panel>
      <Documents verification={verification} />
    </div>
  );
}

function Owners({
  form,
  onChange,
  disabled,
  problem,
  done,
}: {
  form: ApplicationForm;
  onChange: (owners: ApplicationForm["owners"]) => void;
  disabled: boolean;
  problem?: string;
  done?: React.ReactNode;
}) {
  const owners = form.owners;
  const update = (index: number, changes: Partial<ApplicationForm["owners"][number]>) =>
    onChange(owners.map((owner, i) => (i === index ? { ...owner, ...changes } : owner)));

  return (
    <Panel title="Owners" actions={done}>
      <p className="text-sm text-muted">Everyone who owns 25 percent or more of the company, or the largest owners if nobody does.</p>
      {owners.length > 0 && (
        <ul className="flex flex-col gap-3">
          {owners.map((owner, index) => (
            <li key={index} className="flex flex-wrap items-end gap-3">
              <label className="flex flex-1 flex-col gap-1 text-sm">
                <span className="text-muted">Name of owner {index + 1}</span>
                <input name="owners" aria-label={`Name of owner ${index + 1}`} value={owner.name} onChange={(e) => update(index, { name: e.target.value })} disabled={disabled} className={fieldClass} />
              </label>
              <label className="flex flex-col gap-1 text-sm">
                <span className="text-muted">Share (%)</span>
                <input
                  name="owners"
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
      <FieldProblemText problem={problem} />
      {!disabled && owners.length < maxOwners && (
        <button type="button" onClick={() => onChange([...owners, { name: "", share: "" }])} className={`${secondaryButtonClass} self-start`}>
          Add owner
        </button>
      )}
    </Panel>
  );
}

/** Documents the firm adds if it wants to, such as its certificate of registration, dropped or chosen like the logo. */
function Documents({ verification }: { verification: Verification }) {
  const upload = useUploadDocument();
  const remove = useRemoveDocument();
  const [problem, setProblem] = useState<string | null>(null);
  const editable = verification.canEdit;
  const room = verification.documents.length < verification.maxDocuments;

  const choose = (file: File) => {
    if (file.size > verification.maxDocumentBytes) {
      setProblem(`${file.name} is too large. A document can be at most ${fileSize(verification.maxDocumentBytes)}.`);
    } else {
      setProblem(null);
      upload.mutate(file);
    }
  };

  return (
    <Panel title="Documents (optional)">
      <p className="text-sm text-muted">
        For example your certificate of registration. They are stored encrypted, and only you and our staff can open them.
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
        <DropZone
          label="Add a document"
          title="Drop a document here, or "
          hint={`PDF, PNG or JPEG, at most ${fileSize(verification.maxDocumentBytes)} each.`}
          accept=".pdf,.png,.jpg,.jpeg,application/pdf,image/png,image/jpeg"
          busy={upload.isPending}
          onFile={choose}
        />
      )}
      <ErrorText error={problem ? new Error(problem) : (upload.error ?? remove.error)} />
    </Panel>
  );
}

function TextField({
  name,
  label,
  value,
  onChange,
  disabled,
  placeholder,
  hint,
  optional = false,
  problem,
}: {
  name: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  disabled: boolean;
  placeholder?: string;
  hint?: string;
  optional?: boolean;
  problem?: string;
}) {
  return (
    <label className="flex flex-col gap-1 text-sm">
      <span className="text-muted">
        {label}
        {optional && " (optional)"}
      </span>
      <input
        name={name}
        aria-label={label}
        aria-invalid={problem ? true : undefined}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        disabled={disabled}
        placeholder={placeholder}
        className={fieldOf(problem)}
      />
      {problem ? <FieldProblemText problem={problem} /> : hint && <span className="text-xs text-muted">{hint}</span>}
    </label>
  );
}

function FieldProblemText({ problem }: { problem?: string }) {
  return problem ? <span className="text-xs text-warning">{problem}</span> : null;
}

// A field with a problem has a warning border, so every missing field is seen at a glance.
function fieldOf(problem: string | undefined): string {
  return problem ? fieldClass.replace("border-border", "border-warning") : fieldClass;
}
