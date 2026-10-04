"use client";

import { useState } from "react";

import { useBranding } from "@/app/providers";

import type { PayoutMethod } from "@/lib/api/types";
import { payoutMethodFields, payoutMethodForm, payoutMethodKindLabels, payoutMethodLines, payoutMethodOf, type PayoutMethodForm } from "@/lib/payoutMethods";
import { useMyPayoutMethod, useSavePayoutMethod } from "@/lib/queries";

import { buttonClass, ErrorText, fieldClass, secondaryButtonClass, SegmentedControl } from "./ui";

/** Where the trader's payouts are sent, and a way to change it. A payout can be asked for once it is given. */
export function PayoutMethodPanel() {
  const branding = useBranding();
  const method = useMyPayoutMethod();
  const [editing, setEditing] = useState(false);

  return (
    <section id="payout-method" aria-labelledby="payout-method-heading" className="flex flex-col gap-4 rounded-lg border border-border bg-panel p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-1">
          <h2 id="payout-method-heading" className="font-medium">
            How you get paid
          </h2>
          <p className="text-sm text-muted">{branding.name} sends your payouts here. A payout keeps the details it was asked for with.</p>
        </div>
        {method.data && !editing && (
          <button type="button" onClick={() => setEditing(true)} className={`${secondaryButtonClass} text-sm`}>
            Change
          </button>
        )}
      </div>
      {method.isError ? (
        <ErrorText error={method.error} />
      ) : method.data === undefined ? (
        <p className="text-sm text-muted">Loading...</p>
      ) : method.data === null || editing ? (
        <MethodForm method={method.data} onSaved={() => setEditing(false)} onCancel={method.data ? () => setEditing(false) : null} />
      ) : (
        <MethodView method={method.data} />
      )}
    </section>
  );
}

function MethodView({ method }: { method: PayoutMethod }) {
  return (
    <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-2">
      <div className="flex flex-col gap-0.5">
        <dt className="text-muted">Method</dt>
        <dd>{payoutMethodKindLabels[method.kind]}</dd>
      </div>
      {payoutMethodLines(method).map((line) => (
        <div key={line.label} className="flex flex-col gap-0.5">
          <dt className="text-muted">{line.label}</dt>
          <dd className={`break-all ${line.copy ? "font-mono" : ""}`}>{line.value}</dd>
        </div>
      ))}
    </dl>
  );
}

function MethodForm({ method, onSaved, onCancel }: { method: PayoutMethod | null; onSaved: () => void; onCancel: (() => void) | null }) {
  const save = useSavePayoutMethod();
  const [form, setForm] = useState<PayoutMethodForm>(() => payoutMethodForm(method));

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    save.mutate(payoutMethodOf(form), { onSuccess: onSaved });
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-4 text-sm">
      <div className="flex">
        <SegmentedControl
          label="Method"
          value={form.kind}
          options={(["Bank", "Crypto", "Other"] as const).map((kind) => ({ value: kind, label: payoutMethodKindLabels[kind] }))}
          onChange={(kind) => setForm({ ...form, kind })}
        />
      </div>
      <div className="grid gap-3 sm:grid-cols-2">
        {payoutMethodFields[form.kind].map((field) => (
          <label key={field.key} className="flex flex-col gap-1">
            <span className="text-muted">
              {field.label}
              {!field.required && " (if any)"}
            </span>
            <input
              required={field.required}
              maxLength={200}
              value={form[field.key]}
              placeholder={field.placeholder}
              onChange={(e) => setForm({ ...form, [field.key]: e.target.value })}
              className={`${fieldClass} ${field.copy ? "font-mono" : ""}`}
            />
          </label>
        ))}
      </div>
      <ErrorText error={save.error} />
      <div className="flex flex-wrap gap-2">
        <button type="submit" disabled={save.isPending} className={buttonClass}>
          {save.isPending ? "Saving..." : "Save"}
        </button>
        {onCancel && (
          <button type="button" onClick={onCancel} className={secondaryButtonClass}>
            Cancel
          </button>
        )}
      </div>
    </form>
  );
}
