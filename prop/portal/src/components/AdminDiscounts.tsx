"use client";

import { useState } from "react";

import type { DiscountCode } from "@/lib/api/types";
import { discountStatus, discountText, endOfDay } from "@/lib/discounts";
import { formatDate } from "@/lib/format";
import { useChallenges, useCreateDiscount, useDiscountCommand, useDiscounts, usePrices } from "@/lib/queries";

import { AdminPage, Badge, buttonClass, ErrorText, fieldClass, PageHeader, Panel } from "./ui";

/** The firm's discount codes for its shop: new codes, how often each was used, and turning them off (ADR 0036). */
export function AdminDiscounts() {
  const discounts = useDiscounts();
  const command = useDiscountCommand();
  const challenges = useChallenges();
  const challengeName = (id: string) => challenges.data?.find((c) => c.id === id)?.name ?? id;

  return (
    <AdminPage>
      <PageHeader
        title="Discount codes"
        description="Codes buyers type in your shop to pay less. A code for retries only works for a trader whose earlier challenge failed, and the failed account offers it with Try again."
      />
      <NewCode />

      <Panel title="Your codes">
        <ErrorText error={discounts.error ?? command.error} />
        {discounts.isPending ? (
          <p className="text-sm text-muted">Loading...</p>
        ) : (discounts.data ?? []).length === 0 ? (
          <p className="text-sm text-muted">No codes yet.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[48rem] text-sm">
              <thead className="text-left text-muted">
                <tr>
                  <th scope="col" className="py-2 font-normal">
                    Code
                  </th>
                  <th scope="col" className="py-2 pl-4 font-normal">
                    Discount
                  </th>
                  <th scope="col" className="py-2 pl-4 font-normal">
                    For
                  </th>
                  <th scope="col" className="py-2 pl-4 text-right font-normal">
                    Used
                  </th>
                  <th scope="col" className="py-2 pl-4 font-normal">
                    Status
                  </th>
                  <th scope="col" className="py-2 pl-4 font-normal">
                    <span className="sr-only">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {(discounts.data ?? []).map((code) => (
                  <CodeRow
                    key={code.id}
                    code={code}
                    now={discounts.dataUpdatedAt}
                    challengeName={challengeName}
                    pending={command.isPending}
                    onCommand={command.mutate}
                  />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>
    </AdminPage>
  );
}

// Whether a code has ended is judged at the time the list was loaded.
function CodeRow({
  code,
  now,
  challengeName,
  pending,
  onCommand,
}: {
  code: DiscountCode;
  now: number;
  challengeName: (id: string) => string;
  pending: boolean;
  onCommand: (command: { codeId: string; kind: "active"; active: boolean } | { codeId: string; kind: "delete" }) => void;
}) {
  const status = discountStatus(code, now);
  return (
    <tr className="border-t border-border align-top">
      <td className="py-2.5 font-mono font-medium">{code.code}</td>
      <td className="py-2.5 pl-4">{discountText(code)}</td>
      <td className="py-2.5 pl-4">
        {code.challengeIds ? code.challengeIds.map(challengeName).join(", ") : "Every challenge"}
        {code.forRetries && <span className="block text-xs text-muted">Only for a new try after a failed challenge</span>}
        {code.expiresAt && <span className="block text-xs text-muted">Until {formatDate(code.expiresAt)}</span>}
      </td>
      <td className="py-2.5 pl-4 text-right font-mono tabular-nums">{code.maxUses == null ? code.uses : `${code.uses} of ${code.maxUses}`}</td>
      <td className="py-2.5 pl-4">
        <Badge tone={status.tone}>{status.label}</Badge>
      </td>
      <td className="py-2.5 pl-4">
        <span className="flex justify-end gap-3 whitespace-nowrap">
          <button
            type="button"
            disabled={pending}
            onClick={() => onCommand({ codeId: code.id, kind: "active", active: !code.active })}
            className="text-accent hover:underline disabled:opacity-50"
          >
            {code.active ? "Turn off" : "Turn on"}
          </button>
          {code.uses === 0 && (
            <button type="button" disabled={pending} onClick={() => onCommand({ codeId: code.id, kind: "delete" })} className="text-muted hover:text-loss disabled:opacity-50">
              Delete
            </button>
          )}
        </span>
      </td>
    </tr>
  );
}

function NewCode() {
  const create = useCreateDiscount();
  const challenges = useChallenges();
  const prices = usePrices();
  const [code, setCode] = useState("");
  const [kind, setKind] = useState<"percent" | "amount">("percent");
  const [value, setValue] = useState("");
  const [currency, setCurrency] = useState("");
  const [only, setOnly] = useState<string[]>([]);
  const [maxUses, setMaxUses] = useState("");
  const [endsOn, setEndsOn] = useState("");
  const [forRetries, setForRetries] = useState(false);
  // An amount off is in the currency the challenges are priced in, the first one by default.
  const priceCurrencies = [...new Set((prices.data ?? []).map((p) => p.currency))];
  const amountCurrency = currency || priceCurrencies[0] || "USD";

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    const amount = Number(value);
    create.mutate(
      {
        code: code.trim(),
        percentOff: kind === "percent" ? amount : null,
        amountOff: kind === "amount" ? amount : null,
        currency: kind === "amount" ? amountCurrency : null,
        challengeIds: only.length > 0 ? only : null,
        maxUses: maxUses ? Number(maxUses) : null,
        expiresAt: endsOn ? endOfDay(endsOn) : null,
        forRetries,
      },
      {
        onSuccess: () => {
          setCode("");
          setValue("");
          setOnly([]);
          setMaxUses("");
          setEndsOn("");
          setForRetries(false);
        },
      },
    );
  };

  return (
    <Panel title="New code">
      <form onSubmit={submit} className="flex flex-col gap-4 text-sm">
        <div className="grid gap-4 sm:grid-cols-3">
          <label className="flex flex-col gap-1">
            <span className="text-muted">Code</span>
            <input required maxLength={32} value={code} onChange={(e) => setCode(e.target.value)} placeholder="SPRING20" className={`${fieldClass} font-mono uppercase`} />
          </label>
          <fieldset className="flex flex-col gap-1">
            <legend className="mb-1 text-muted">Discount</legend>
            <div className="flex gap-2">
              <input
                required
                aria-label={kind === "percent" ? "Percent off" : "Amount off"}
                inputMode="decimal"
                value={value}
                onChange={(e) => setValue(e.target.value)}
                className={`${fieldClass} w-24 font-mono`}
              />
              <select aria-label="Kind of discount" value={kind} onChange={(e) => setKind(e.target.value as "percent" | "amount")} className={fieldClass}>
                <option value="percent">% off</option>
                <option value="amount">off the price</option>
              </select>
              {kind === "amount" && (
                <select aria-label="Currency" value={amountCurrency} onChange={(e) => setCurrency(e.target.value)} className={fieldClass}>
                  {(priceCurrencies.length > 0 ? priceCurrencies : ["USD"]).map((c) => (
                    <option key={c}>{c}</option>
                  ))}
                </select>
              )}
            </div>
          </fieldset>
          <label className="flex flex-col gap-1">
            <span className="text-muted">
              Most uses <span className="text-xs">(optional)</span>
            </span>
            <input inputMode="numeric" value={maxUses} onChange={(e) => setMaxUses(e.target.value)} placeholder="No limit" className={`${fieldClass} font-mono`} />
          </label>
        </div>

        <div className="grid gap-4 sm:grid-cols-3">
          <fieldset className="flex flex-col gap-1.5 sm:col-span-2">
            <legend className="mb-1 text-muted">For which challenges</legend>
            <div className="flex flex-wrap gap-x-5 gap-y-1.5">
              {(challenges.data ?? []).map((c) => (
                <label key={c.id} className="flex items-center gap-2">
                  <input
                    type="checkbox"
                    checked={only.includes(c.id)}
                    onChange={(e) => setOnly(e.target.checked ? [...only, c.id] : only.filter((id) => id !== c.id))}
                  />
                  {c.name}
                </label>
              ))}
            </div>
            <span className="text-xs text-muted">{only.length === 0 ? "None ticked: every challenge." : `Only the ${only.length === 1 ? "one" : `${only.length}`} ticked.`}</span>
          </fieldset>
          <label className="flex flex-col gap-1">
            <span className="text-muted">
              Last day <span className="text-xs">(optional)</span>
            </span>
            <input type="date" value={endsOn} onChange={(e) => setEndsOn(e.target.value)} className={fieldClass} />
          </label>
        </div>

        <label className="flex items-start gap-2">
          <input type="checkbox" checked={forRetries} onChange={(e) => setForRetries(e.target.checked)} className="mt-1" />
          <span>
            Only for a new try
            <span className="block text-xs text-muted">The buyer needs an earlier challenge at your firm that failed. A failed account offers the code with Try again.</span>
          </span>
        </label>

        <ErrorText error={create.error} />
        <div>
          <button type="submit" disabled={create.isPending} className={buttonClass}>
            {create.isPending ? "Saving..." : "Add code"}
          </button>
        </div>
      </form>
    </Panel>
  );
}
